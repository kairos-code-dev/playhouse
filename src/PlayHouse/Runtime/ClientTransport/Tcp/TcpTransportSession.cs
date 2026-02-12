#nullable enable

using System.Buffers;
using System.Buffers.Binary;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Infrastructure.Memory;

namespace PlayHouse.Runtime.ClientTransport.Tcp;

/// <summary>
/// Task-less TCP transport session using SocketAsyncEventArgs for zero-task overhead per session.
/// Only uses system resources when actual I/O occurs.
/// </summary>
internal sealed class TcpTransportSession : ITransportSession
{
    private readonly Socket _socket;
    private readonly Stream? _stream; // TLS용 Stream (null이면 Socket 직접 사용)
    private readonly TransportOptions _options;
    private readonly MessageReceivedCallback _onMessage;
    private readonly SessionDisconnectedCallback _onDisconnect;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts;
    private readonly string _remoteEndpoint;
    private Exception? _disconnectException;

    // Buffer for receiving data
    private readonly byte[] _receiveBuffer;
    private int _bytesInBuffer;
    private readonly SocketAsyncEventArgs? _receiveArgs; // TLS에서는 사용 안 함

    // Locking for sending to ensure order without a permanent task
    private readonly object _sendLock = new();
    private bool _isSending;
    private readonly Queue<SendItem> _sendQueue = new();

    private DateTime _lastActivity;
    private bool _disposed;
    private Task? _streamReceiveTask; // TLS용 수신 Task

    private readonly record struct SendItem(byte[] Buffer, int Size);

    public long SessionId { get; }
    public string AccountId { get; set; } = string.Empty;
    public bool IsAuthenticated { get; set; }
    public string StageId { get; set; } = string.Empty;
    public bool IsConnected => !_disposed && _socket.Connected;
    public object? ProcessorContext { get; set; }

    internal TcpTransportSession(
        long sessionId,
        Socket socket,
        Stream? stream,
        TransportOptions options,
        MessageReceivedCallback onMessage,
        SessionDisconnectedCallback onDisconnect,
        ILogger logger,
        CancellationToken externalCt)
    {
        SessionId = sessionId;
        _socket = socket;
        _remoteEndpoint = socket.RemoteEndPoint?.ToString() ?? "unknown";
        _stream = stream is NetworkStream ? null : stream; // NetworkStream이면 Socket 직접 사용, 그 외(SslStream)는 Stream 사용
        _options = options;
        _onMessage = onMessage;
        _onDisconnect = onDisconnect;
        _logger = logger;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        _lastActivity = DateTime.UtcNow;

        ConfigureSocket();

        // Initialize receive buffer using MessagePool
        _receiveBuffer = MessagePool.Rent(_options.ReceiveBufferSize * 2);

        // Socket 모드일 때만 SocketAsyncEventArgs 사용
        if (_stream == null)
        {
            _receiveArgs = new SocketAsyncEventArgs();
            _receiveArgs.Completed += OnReceiveCompleted;
        }
    }

    public void Start()
    {
        if (_stream != null)
        {
            // TLS 모드: Stream 기반 수신 Task 시작
            _streamReceiveTask = Task.Run(() => StreamReceiveLoopAsync(_cts.Token));
            _logger.LogDebug("TCP session {SessionId} started (Stream-based for TLS)", SessionId);
        }
        else
        {
            // Socket 모드: Task-less 수신 시작
            StartReceive();
            _logger.LogDebug("TCP session {SessionId} started (Task-less with MessagePool)", SessionId);
        }
    }

    /// <summary>
    /// TLS 모드용 Stream 기반 수신 루프
    /// </summary>
    private async Task StreamReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && !_disposed)
            {
                var bytesRead = await _stream!.ReadAsync(
                    _receiveBuffer.AsMemory(_bytesInBuffer, _receiveBuffer.Length - _bytesInBuffer), ct);

                if (bytesRead == 0)
                {
                    // Connection closed
                    _disconnectException ??= new IOException("Remote closed the connection.");
                    await DisconnectAsync();
                    return;
                }

                _bytesInBuffer += bytesRead;
                _lastActivity = DateTime.UtcNow;

                // Parse and dispatch messages
                _bytesInBuffer = ParseAndDispatchMessages(_receiveBuffer, _bytesInBuffer);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _disconnectException ??= ex;
            if (!_disposed)
                _logger.LogError(ex, "Error in TLS receive loop for session {SessionId} ({Remote})", SessionId, _remoteEndpoint);
            await DisconnectAsync();
        }
    }

    private void StartReceive()
    {
        if (_disposed || _receiveArgs == null) return;

        try
        {
            _receiveArgs.SetBuffer(_receiveBuffer, _bytesInBuffer, _receiveBuffer.Length - _bytesInBuffer);

            if (!_socket.ReceiveAsync(_receiveArgs))
            {
                // Completed synchronously
                ProcessReceive(_receiveArgs);
            }
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
    }

    private void OnReceiveCompleted(object? sender, SocketAsyncEventArgs e)
    {
        ProcessReceive(e);
    }

    private void ProcessReceive(SocketAsyncEventArgs e)
    {
        if (e.SocketError != SocketError.Success || e.BytesTransferred == 0)
        {
            if (e.SocketError != SocketError.Success)
            {
                _disconnectException ??= new SocketException((int)e.SocketError);
            }
            else
            {
                _disconnectException ??= new IOException("Remote closed the connection.");
            }
            _ = DisconnectAsync();
            return;
        }

        _bytesInBuffer += e.BytesTransferred;
        _lastActivity = DateTime.UtcNow;

        // Parse and dispatch messages
        _bytesInBuffer = ParseAndDispatchMessages(_receiveBuffer, _bytesInBuffer);

        // Continue receiving
        StartReceive();
    }

    private int ParseAndDispatchMessages(byte[] buffer, int bytesInBuffer)
    {
        int consumed = 0;
        while (true)
        {
            var remainingSpan = buffer.AsSpan(consumed, bytesInBuffer - consumed);
            if (TryParseMessage(remainingSpan, out var msgId, out var msgSeq, out var stageId, out var payload, out int packetSize))
            {
                try
                {
                    _onMessage(this, msgId, msgSeq, stageId, payload);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing message {MsgId} on session {SessionId}", msgId, SessionId);
                }
                consumed += packetSize;
            }
            else
            {
                break;
            }
        }

        if (consumed > 0)
        {
            int remaining = bytesInBuffer - consumed;
            if (remaining > 0)
            {
                buffer.AsSpan(consumed, remaining).CopyTo(buffer);
                return remaining;
            }
            return 0;
        }
        return bytesInBuffer;
    }

    private bool TryParseMessage(ReadOnlySpan<byte> span, out string msgId, out ushort msgSeq, out string stageId, out IPayload payload, out int totalPacketSize)
    {
        msgId = string.Empty; msgSeq = 0; stageId = string.Empty; payload = null!; totalPacketSize = 0;
        if (span.Length < 4) return false;
        var packetLength = BinaryPrimitives.ReadInt32LittleEndian(span);
        if (packetLength <= 0 || packetLength > _options.MaxPacketSize) throw new InvalidDataException($"Invalid packet length: {packetLength}");
        totalPacketSize = 4 + packetLength;
        if (span.Length < totalPacketSize) return false;
        var bodySpan = span.Slice(4, packetLength);
        if (!MessageCodec.TryParseMessage(bodySpan, out msgId, out msgSeq, out stageId, out var payloadOffset)) throw new InvalidDataException("Invalid message format");
        var payloadLength = packetLength - payloadOffset;
        
        // Use MessagePool for payload data
        var rentedBuffer = MessagePool.Rent(payloadLength);
        bodySpan.Slice(payloadOffset, payloadLength).CopyTo(rentedBuffer);
        payload = MessagePoolPayload.Create(rentedBuffer, payloadLength);
        
        return true;
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_stream != null)
            {
                await _stream.WriteAsync(data, cancellationToken);
                await _stream.FlushAsync(cancellationToken);
            }
            else
            {
                await _socket.SendAsync(data, SocketFlags.None, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
    }

    public void SendResponse(string msgId, ushort msgSeq, string stageId, ushort errorCode, ReadOnlySpan<byte> payload)
    {
        if (_disposed) return;

        var totalSize = MessageCodec.CalculateResponseSize(msgId, payload.Length, includeLengthPrefix: true);
        
        // Use MessagePool for output buffer
        var buffer = MessagePool.Rent(totalSize);
        
        var span = buffer.AsSpan(0, totalSize);
        BinaryPrimitives.WriteInt32LittleEndian(span, totalSize - 4);
        MessageCodec.WriteResponseBody(span[4..], msgId, msgSeq, stageId, errorCode, payload);

        lock (_sendLock)
        {
            _sendQueue.Enqueue(new SendItem(buffer, totalSize));
            if (!_isSending)
            {
                _isSending = true;
                _ = ProcessSendQueueAsync();
            }
        }
    }

    private async Task ProcessSendQueueAsync()
    {
        try
        {
            while (true)
            {
                SendItem item;
                lock (_sendLock)
                {
                    if (_sendQueue.Count == 0)
                    {
                        _isSending = false;
                        return;
                    }
                    item = _sendQueue.Dequeue();
                }

                try
                {
                    if (_stream != null)
                    {
                        await _stream.WriteAsync(item.Buffer.AsMemory(0, item.Size));
                        await _stream.FlushAsync();
                    }
                    else
                    {
                        await _socket.SendAsync(item.Buffer.AsMemory(0, item.Size), SocketFlags.None);
                    }
                }
                finally
                {
                    // Return to MessagePool
                    MessagePool.Return(item.Buffer);
                }
            }
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
    }

    private void HandleError(Exception ex)
    {
        if (!_disposed)
        {
            _disconnectException ??= ex;
            _logger.LogError(ex, "Error in session {SessionId} ({Remote})", SessionId, _remoteEndpoint);
            _ = DisconnectAsync();
        }
    }

    public ValueTask DisconnectAsync()
    {
        _ = DisposeAsync();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _cts.Cancel();

            // TLS 모드에서 수신 Task 대기
            if (_streamReceiveTask != null)
            {
                try { await _streamReceiveTask.WaitAsync(TimeSpan.FromSeconds(3)); }
                catch { }
            }

            _receiveArgs?.Dispose();
            _stream?.Dispose();

            // Return receive buffer to MessagePool
            MessagePool.Return(_receiveBuffer);

            if (_socket.Connected)
            {
                _socket.Shutdown(SocketShutdown.Both);
            }
            _socket.Close();
        }
        catch { }
        finally
        {
            _cts.Dispose();
            if (_disconnectException != null)
            {
                _logger.LogDebug("TCP session {SessionId} ({Remote}) disconnected with error: {ErrorType} {Message}",
                    SessionId,
                    _remoteEndpoint,
                    _disconnectException.GetType().Name,
                    _disconnectException.Message);
            }
            else
            {
                _logger.LogDebug("TCP session {SessionId} ({Remote}) disconnected", SessionId, _remoteEndpoint);
            }
            _onDisconnect(this, _disconnectException);
        }

        await Task.CompletedTask;
    }

    private void ConfigureSocket()
    {
        _socket.NoDelay = true;
        _socket.ReceiveBufferSize = _options.ReceiveBufferSize;
        _socket.SendBufferSize = _options.SendBufferSize;
        if (_options.EnableKeepAlive) _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
    }
}
