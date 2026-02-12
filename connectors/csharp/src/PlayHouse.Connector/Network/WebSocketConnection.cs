#nullable enable

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PlayHouse.Connector.Infrastructure.Buffers;
using PlayHouse.Connector.Protocol;

namespace PlayHouse.Connector.Network;

/// <summary>
/// WebSocket 기반 연결 구현
/// Zero-copy receive with RingBuffer and direct packet parsing
/// </summary>
internal sealed class WebSocketConnection : IConnection
{
    private readonly ConnectorConfig _config;
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private volatile bool _isConnected;

    private const int ReceiveBufferSize = 65536;

    // Zero-copy receive buffer
    private readonly RingBuffer _receiveBuffer = new(ReceiveBufferSize);
    private int _expectedPacketSize = -1;

    public event EventHandler<IPacket>? PacketReceived;
    public event EventHandler<Exception?>? Disconnected;

    public bool IsConnected => _isConnected;

    public WebSocketConnection(ConnectorConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public async Task ConnectAsync(string host, int port, bool useSsl = false, CancellationToken cancellationToken = default)
    {
        if (_isConnected)
        {
            throw new InvalidOperationException("Already connected.");
        }

        try
        {
            // ws:// 또는 wss:// 자동 선택
            var scheme = useSsl ? "wss" : "ws";
            var path = _config.WebSocketPath.StartsWith("/") ? _config.WebSocketPath : "/" + _config.WebSocketPath;
            var uri = new Uri($"{scheme}://{host}:{port}{path}");

            _webSocket = new ClientWebSocket();

            // Configure options
            _webSocket.Options.KeepAliveInterval = TimeSpan.FromMilliseconds(_config.HeartBeatIntervalMs);

            // 테스트용: 자체 서명 인증서 허용
            if (_config.SkipServerCertificateValidation)
            {
                _webSocket.Options.RemoteCertificateValidationCallback =
                    (sender, certificate, chain, errors) => true;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_config.ConnectionIdleTimeoutMs);

            await _webSocket.ConnectAsync(uri, timeoutCts.Token).ConfigureAwait(false);

            _isConnected = true;

            // Start receiving data
            _receiveCts = new CancellationTokenSource();
            _receiveTask = ReceiveLoopAsync(_receiveCts.Token);
        }
        catch (Exception)
        {
            await CleanupAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        if (!_isConnected)
        {
            return;
        }

        _isConnected = false;

        // Gracefully close WebSocket
        if (_webSocket?.State == WebSocketState.Open)
        {
            try
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Client disconnecting",
                    closeCts.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Ignore errors during graceful close
            }
        }

        // Cancel receive loop
        _receiveCts?.Cancel();

        // Wait for receive task to complete
        if (_receiveTask != null)
        {
            try
            {
                await _receiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when canceling
            }
            catch (Exception)
            {
                // Ignore errors during disconnect
            }
        }

        await CleanupAsync().ConfigureAwait(false);
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        await SendAsync(data, ReadOnlyMemory<byte>.Empty, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask SendAsync(
        ReadOnlyMemory<byte> header,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        if (!_isConnected || _webSocket == null || _webSocket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("Not connected.");
        }

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (payload.IsEmpty)
            {
                await _webSocket.SendAsync(
                    header,
                    WebSocketMessageType.Binary,
                    endOfMessage: true,
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                if (!header.IsEmpty)
                {
                    await _webSocket.SendAsync(
                        header,
                        WebSocketMessageType.Binary,
                        endOfMessage: false,
                        cancellationToken).ConfigureAwait(false);
                }

                await _webSocket.SendAsync(
                    payload,
                    WebSocketMessageType.Binary,
                    endOfMessage: true,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            HandleDisconnection(ex);
            throw;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var tempBuffer = ArrayPool<byte>.Shared.Rent(ReceiveBufferSize);

        try
        {
            while (!cancellationToken.IsCancellationRequested && _isConnected && _webSocket != null)
            {
                try
                {
                    // WebSocket receives complete messages, so we need to accumulate fragments
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _webSocket.ReceiveAsync(
                            new ArraySegment<byte>(tempBuffer),
                            cancellationToken).ConfigureAwait(false);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            HandleDisconnection(null);
                            return;
                        }

                        if (result.Count > 0)
                        {
                            // Write to RingBuffer
                            _receiveBuffer.WriteBytes(new ReadOnlySpan<byte>(tempBuffer, 0, result.Count));
                        }
                    }
                    while (!result.EndOfMessage);

                    // Parse packets from RingBuffer
                    ProcessReceiveBuffer();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Expected when canceling
                    break;
                }
                catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
                {
                    HandleDisconnection(ex);
                    break;
                }
                catch (Exception ex)
                {
                    HandleDisconnection(ex);
                    break;
                }
            }
        }
        finally
        {
            // Fix #13: Clear temporary receive buffer
            ArrayPool<byte>.Shared.Return(tempBuffer, clearArray: true);
        }
    }

    private void ProcessReceiveBuffer()
    {
        while (true)
        {
            // Read packet size header (4 bytes)
            if (_expectedPacketSize == -1)
            {
                if (_receiveBuffer.Count < 4)
                {
                    break;
                }

                // Peek and parse packet size (zero-copy)
                _expectedPacketSize = ReadInt32LittleEndian(_receiveBuffer);

                if (_expectedPacketSize <= 0 || _expectedPacketSize > 10 * 1024 * 1024)
                {
                    throw new InvalidOperationException($"Invalid packet size: {_expectedPacketSize}");
                }

                // Consume the size header
                _receiveBuffer.Consume(4);
            }

            // Check if we have the complete packet
            if (_receiveBuffer.Count < _expectedPacketSize)
            {
                break;
            }

            // Parse packet from RingBuffer
            try
            {
                var packet = ParseServerPacket(_receiveBuffer, _expectedPacketSize);
                _receiveBuffer.Consume(_expectedPacketSize);
                _expectedPacketSize = -1;

                // Raise event
                PacketReceived?.Invoke(this, packet);
            }
            catch (Exception)
            {
                // Parsing error - skip this packet
                _receiveBuffer.Consume(_expectedPacketSize);
                _expectedPacketSize = -1;
            }
        }
    }

    private static int ReadInt32LittleEndian(RingBuffer buffer)
    {
        // Handle wrapped case
        if (buffer.Count < 4)
        {
            throw new InvalidOperationException("Not enough data");
        }

        Span<byte> temp = stackalloc byte[4];
        for (int i = 0; i < 4; i++)
        {
            temp[i] = buffer.PeekByte(i);
        }
        return BinaryPrimitives.ReadInt32LittleEndian(temp);
    }

    private static IPacket ParseServerPacket(RingBuffer buffer, int packetSize)
    {
        int offset = 0;

        // Protocol: MsgIdLen(1) + MsgId(N) + MsgSeq(2) + ErrorCode(2) + OriginalSize(4) + Payload

        // MsgIdLen (1 byte)
        var msgIdLen = buffer.PeekByte(offset++);

        // Fix #7: Protocol boundary validation before reading payload
        // Minimum packet size: MsgIdLen(1) + MsgId(1+) + MsgSeq(2) + ErrorCode(2) + OriginalSize(4) = 10 bytes
        // Validate msgIdLen is within bounds (1-255) and reasonable (1-128 practical limit)
        if (msgIdLen == 0 || msgIdLen > 128)
        {
            throw new InvalidOperationException($"Invalid MsgIdLen: {msgIdLen}");
        }

        var minimumPacketSize = 1 + msgIdLen + 2 + 2 + 4;
        if (packetSize < minimumPacketSize)
        {
            throw new InvalidOperationException($"Invalid packet structure: packetSize={packetSize}, msgIdLen={msgIdLen}, minimumPacketSize={minimumPacketSize}");
        }

        // Validate payload size is non-negative
        var headerSize = 1 + msgIdLen + 2 + 2 + 4;
        var payloadSize = packetSize - headerSize;
        if (payloadSize < 0)
        {
            throw new InvalidOperationException($"Invalid payload size: {payloadSize}");
        }

        // MsgId (N bytes)
        Span<byte> msgIdBytes = stackalloc byte[msgIdLen];
        for (int i = 0; i < msgIdLen; i++)
        {
            msgIdBytes[i] = buffer.PeekByte(offset++);
        }
        var msgId = Encoding.UTF8.GetString(msgIdBytes);

        // MsgSeq (2 bytes)
        Span<byte> msgSeqBytes = stackalloc byte[2];
        msgSeqBytes[0] = buffer.PeekByte(offset++);
        msgSeqBytes[1] = buffer.PeekByte(offset++);
        var msgSeq = BinaryPrimitives.ReadUInt16LittleEndian(msgSeqBytes);

        // ErrorCode (2 bytes)
        Span<byte> errorCodeBytes = stackalloc byte[2];
        errorCodeBytes[0] = buffer.PeekByte(offset++);
        errorCodeBytes[1] = buffer.PeekByte(offset++);
        var errorCode = BinaryPrimitives.ReadUInt16LittleEndian(errorCodeBytes);

        // OriginalSize (4 bytes)
        Span<byte> originalSizeBytes = stackalloc byte[4];
        for (int i = 0; i < 4; i++)
        {
            originalSizeBytes[i] = buffer.PeekByte(offset++);
        }
        var originalSize = BinaryPrimitives.ReadInt32LittleEndian(originalSizeBytes);

        // Payload (with optional decompression) - payloadSize already calculated and validated above
        IPayload payload;

        if (originalSize > 0)
        {
            // Decompression path with size validation
            const int MaxDecompressedSize = 10 * 1024 * 1024; // 10MB limit
            if (originalSize > MaxDecompressedSize)
            {
                throw new InvalidOperationException($"Decompressed size exceeds limit: {originalSize} > {MaxDecompressedSize}");
            }

            // Decompression path: Use ArrayPool for temporary compressed data
            var compressedBuffer = ArrayPool<byte>.Shared.Rent(payloadSize);
            try
            {
                buffer.PeekBytes(offset, compressedBuffer.AsSpan(0, payloadSize));
                offset += payloadSize;

                var decompressed = K4os.Compression.LZ4.LZ4Pickler.Unpickle(compressedBuffer.AsSpan(0, payloadSize));

                // Verify actual decompressed size matches declared size
                if (decompressed.Length != originalSize)
                {
                    throw new InvalidOperationException($"Decompressed size mismatch: expected={originalSize}, actual={decompressed.Length}");
                }

                payload = new MemoryPayload(new ReadOnlyMemory<byte>(decompressed));
            }
            finally
            {
                // Fix #13: Clear compressed buffer after decompression
                ArrayPool<byte>.Shared.Return(compressedBuffer, clearArray: true);
            }
        }
        else
        {
            // Non-compressed path: Use ArrayPool
            var payloadBuffer = ArrayPool<byte>.Shared.Rent(payloadSize);
            buffer.PeekBytes(offset, payloadBuffer.AsSpan(0, payloadSize));
            offset += payloadSize;
            payload = new ArrayPoolPayload(payloadBuffer, payloadSize);
        }

        // Create ParsedPacket wrapper to pass metadata
        return new ParsedPacket(msgId, msgSeq, 0, errorCode, payload);
    }

    private void HandleDisconnection(Exception? exception)
    {
        if (!_isConnected)
        {
            return;
        }

        _isConnected = false;
        Disconnected?.Invoke(this, exception);
    }

    private Task CleanupAsync()
    {
        _receiveCts?.Dispose();
        _receiveCts = null;

        _webSocket?.Dispose();
        _webSocket = null;

        _receiveBuffer.Clear();
        _expectedPacketSize = -1;

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _sendLock.Dispose();
        _receiveBuffer.Dispose();
    }
}
