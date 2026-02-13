#nullable enable

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PlayHouse.Connector.Protocol;

namespace PlayHouse.Connector.Network;

/// <summary>
/// TCP 기반 연결 구현
/// Direct stream reading without intermediate buffering for zero-copy receive
/// </summary>
internal sealed class TcpConnection : IConnection
{
    private readonly ConnectorConfig _config;
    private TcpClient? _client;
    private Stream? _stream; // NetworkStream 또는 SslStream
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly List<ArraySegment<byte>> _gatherSendSegments = new(2);
    private volatile bool _isConnected;
    private volatile bool _useSsl;

    private const int SendBufferSize = 65536;
    private const int ReceiveBufferSize = 262144; // 256KB for large payloads

    // Header buffer for reading packet metadata (4 + 1 + 255 + 16 = 276 bytes max)
    private readonly byte[] _headerBuffer = new byte[276];

    public event EventHandler<IPacket>? PacketReceived;
    public event EventHandler<Exception?>? Disconnected;

    public bool IsConnected => _isConnected;

    public TcpConnection(ConnectorConfig config)
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
            _client = new TcpClient
            {
                SendBufferSize = SendBufferSize,
                ReceiveBufferSize = ReceiveBufferSize,
                NoDelay = true
            };

            // Configure keep-alive
            _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            // netstandard2.1에서는 TcpClient.ConnectAsync가 CancellationToken을 지원하지 않음
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_config.ConnectionIdleTimeoutMs);

            // Task.WhenAny를 사용하여 타임아웃 처리
            var connectTask = _client.ConnectAsync(host, port);
            var timeoutTask = Task.Delay(_config.ConnectionIdleTimeoutMs, timeoutCts.Token);
            var completedTask = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);

            if (completedTask == timeoutTask)
            {
                throw new TimeoutException($"Connection timeout after {_config.ConnectionIdleTimeoutMs}ms");
            }

            await connectTask.ConfigureAwait(false);

            var networkStream = _client.GetStream();

            // SSL/TLS 래핑
            if (useSsl)
            {
                SslStream sslStream;
                if (_config.SkipServerCertificateValidation)
                {
                    // 테스트용: 자체 서명 인증서 허용
                    sslStream = new SslStream(networkStream, false,
                        (sender, certificate, chain, errors) => true);
                }
                else
                {
                    sslStream = new SslStream(networkStream, false);
                }
                await sslStream.AuthenticateAsClientAsync(host).ConfigureAwait(false);
                _stream = sslStream;
            }
            else
            {
                _stream = networkStream;
            }

            _isConnected = true;
            _useSsl = useSsl;

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
        _useSsl = false;

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
        if (!_isConnected || _stream == null)
        {
            throw new InvalidOperationException("Not connected.");
        }

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_useSsl &&
                !header.IsEmpty &&
                !payload.IsEmpty &&
                _client != null &&
                TryGetArraySegment(header, out var headerSegment) &&
                TryGetArraySegment(payload, out var payloadSegment))
            {
                SendWithGatherLoop(_client.Client, headerSegment, payloadSegment, cancellationToken);
                return;
            }

            if (!header.IsEmpty)
            {
                await _stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            }

            if (!payload.IsEmpty)
            {
                await _stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
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

    private static bool TryGetArraySegment(ReadOnlyMemory<byte> memory, out ArraySegment<byte> segment)
    {
        return MemoryMarshal.TryGetArray(memory, out segment);
    }

    private void SendWithGatherLoop(
        Socket socket,
        ArraySegment<byte> headerSegment,
        ArraySegment<byte> payloadSegment,
        CancellationToken cancellationToken)
    {
        int headerSent = 0;
        int payloadSent = 0;

        while (headerSent < headerSegment.Count || payloadSent < payloadSegment.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _gatherSendSegments.Clear();

            if (headerSent < headerSegment.Count)
            {
                _gatherSendSegments.Add(new ArraySegment<byte>(
                    headerSegment.Array!,
                    headerSegment.Offset + headerSent,
                    headerSegment.Count - headerSent));
            }

            if (payloadSent < payloadSegment.Count)
            {
                _gatherSendSegments.Add(new ArraySegment<byte>(
                    payloadSegment.Array!,
                    payloadSegment.Offset + payloadSent,
                    payloadSegment.Count - payloadSent));
            }

            var bytesSent = socket.Send(_gatherSendSegments, SocketFlags.None);
            if (bytesSent <= 0)
            {
                throw new IOException("Socket send returned 0 bytes.");
            }

            if (headerSent < headerSegment.Count)
            {
                var headerRemaining = headerSegment.Count - headerSent;
                var sentFromHeader = Math.Min(bytesSent, headerRemaining);
                headerSent += sentFromHeader;
                bytesSent -= sentFromHeader;
            }

            if (bytesSent > 0 && payloadSent < payloadSegment.Count)
            {
                var payloadRemaining = payloadSegment.Count - payloadSent;
                var sentFromPayload = Math.Min(bytesSent, payloadRemaining);
                payloadSent += sentFromPayload;
            }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _isConnected && _stream != null)
            {
                try
                {
                    // 1. Read packet length (4 bytes)
                    await ReadExactlyAsync(_headerBuffer, 0, 4, cancellationToken).ConfigureAwait(false);
                    var contentSize = ReadContentLength(_headerBuffer);

                    // Fix #7: Validate packet size bounds before reading
                    if (contentSize <= 0 || contentSize > 10 * 1024 * 1024)
                    {
                        throw new InvalidOperationException($"Invalid packet size: {contentSize}");
                    }

                    // 2. Read MsgIdLen (1 byte)
                    await ReadExactlyAsync(_headerBuffer, 0, 1, cancellationToken).ConfigureAwait(false);
                    var msgIdLen = _headerBuffer[0];

                    // 3. Fix #7: Protocol boundary validation before reading payload
                    // Minimum header size: MsgIdLen(1) + MsgId(1+) + MsgSeq(2) + ErrorCode(2) + OriginalSize(4) = 10 bytes
                    // Validate msgIdLen is within bounds (1-255) and reasonable (1-128 practical limit)
                    if (msgIdLen == 0 || msgIdLen > 128)
                    {
                        throw new InvalidOperationException($"Invalid MsgIdLen: {msgIdLen}");
                    }

                    var fixedHeaderSize = msgIdLen + 8; // MsgId + MsgSeq(2) + ErrorCode(2) + OriginalSize(4)
                    var headerTotalSize = 1 + fixedHeaderSize; // MsgIdLen(1) + fixedHeaderSize

                    if (contentSize < headerTotalSize)
                    {
                        throw new InvalidOperationException($"Invalid packet structure: contentSize={contentSize}, msgIdLen={msgIdLen}, headerTotalSize={headerTotalSize}");
                    }

                    // Validate payload size is non-negative
                    var payloadSize = contentSize - headerTotalSize;
                    if (payloadSize < 0)
                    {
                        throw new InvalidOperationException($"Invalid payload size: {payloadSize}");
                    }

                    // 4. Read MsgId + fixed fields
                    await ReadExactlyAsync(_headerBuffer, 0, fixedHeaderSize, cancellationToken).ConfigureAwait(false);

                    // 5. Parse header fields
                    var msgId = Encoding.UTF8.GetString(_headerBuffer, 0, msgIdLen);
                    var offset = msgIdLen;
                    var msgSeq = BinaryPrimitives.ReadUInt16LittleEndian(_headerBuffer.AsSpan(offset));
                    offset += 2;
                    var errorCode = BinaryPrimitives.ReadUInt16LittleEndian(_headerBuffer.AsSpan(offset));
                    offset += 2;
                    var originalSize = BinaryPrimitives.ReadInt32LittleEndian(_headerBuffer.AsSpan(offset));

                    // 6. Read payload (payloadSize already calculated and validated above)

                    IPayload payload;
                    if (payloadSize > 0)
                    {
                        var payloadBuffer = ArrayPool<byte>.Shared.Rent(payloadSize);
                        try
                        {
                            await ReadExactlyAsync(payloadBuffer, 0, payloadSize, cancellationToken).ConfigureAwait(false);

                            if (originalSize > 0)
                            {
                                // Decompression path with size validation
                                const int MaxDecompressedSize = 10 * 1024 * 1024; // 10MB limit
                                if (originalSize > MaxDecompressedSize)
                                {
                                    // Fix #13: Clear buffer on error path
                                    ArrayPool<byte>.Shared.Return(payloadBuffer, clearArray: true);
                                    throw new InvalidOperationException($"Decompressed size exceeds limit: {originalSize} > {MaxDecompressedSize}");
                                }

                                var decompressed = K4os.Compression.LZ4.LZ4Pickler.Unpickle(payloadBuffer.AsSpan(0, payloadSize));
                                // Fix #13: Clear compressed buffer after decompression
                                ArrayPool<byte>.Shared.Return(payloadBuffer, clearArray: true);

                                // Verify actual decompressed size matches declared size
                                if (decompressed.Length != originalSize)
                                {
                                    throw new InvalidOperationException($"Decompressed size mismatch: expected={originalSize}, actual={decompressed.Length}");
                                }

                                payload = new MemoryPayload(new ReadOnlyMemory<byte>(decompressed));
                            }
                            else
                            {
                                // Non-compressed path: keep ArrayPool buffer
                                payload = new ArrayPoolPayload(payloadBuffer, payloadSize);
                            }
                        }
                        catch
                        {
                            // Fix #13: Clear buffer on exception path
                            ArrayPool<byte>.Shared.Return(payloadBuffer, clearArray: true);
                            throw;
                        }
                    }
                    else
                    {
                        payload = EmptyPayload.Instance;
                    }

                    // 7. Create packet and raise event
                    var packet = new ParsedPacket(msgId, msgSeq, 0, errorCode, payload);
                    PacketReceived?.Invoke(this, packet);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Expected when canceling
                    break;
                }
                catch (Exception ex)
                {
                    HandleDisconnection(ex);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            HandleDisconnection(ex);
        }
    }

    /// <summary>
    /// Reads exactly the specified number of bytes from the stream
    /// </summary>
    private async Task ReadExactlyAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            var bytesRead = await _stream!.ReadAsync(buffer, offset + totalRead, count - totalRead, cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                throw new EndOfStreamException("Connection closed while reading packet");
            }
            totalRead += bytesRead;
        }
    }

    private int ReadContentLength(ReadOnlySpan<byte> bytes)
    {
        // Zlink STREAM raw framing uses network byte order on all transports.
        return BinaryPrimitives.ReadInt32BigEndian(bytes);
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

    private async Task CleanupAsync()
    {
        _receiveCts?.Dispose();
        _receiveCts = null;

        if (_stream != null)
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
            _stream = null;
        }

        _client?.Dispose();
        _client = null;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _sendLock.Dispose();
    }
}

/// <summary>
/// Internal parsed packet wrapper for passing metadata
/// </summary>
internal sealed class ParsedPacket : IPacket
{
    public string MsgId { get; }
    public ushort MsgSeq { get; }
    public long StageId { get; }
    public ushort ErrorCode { get; }
    public IPayload Payload { get; }

    public ParsedPacket(string msgId, ushort msgSeq, long stageId, ushort errorCode, IPayload payload)
    {
        MsgId = msgId;
        MsgSeq = msgSeq;
        StageId = stageId;
        ErrorCode = errorCode;
        Payload = payload;
    }

    public void Dispose()
    {
        Payload?.Dispose();
    }
}
