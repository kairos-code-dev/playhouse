#nullable enable

using System.Buffers;
using System.Buffers.Binary;
using System.Net.WebSockets;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Infrastructure.Memory;
using WS = System.Net.WebSockets.WebSocket;

namespace PlayHouse.Runtime.ClientTransport.WebSocket;

/// <summary>
/// WebSocket transport session for bidirectional communication using MessagePool.
/// </summary>
internal sealed class WebSocketTransportSession : ITransportSession
{
    /// <summary>
    /// Pooled buffer for collecting fragmented WebSocket messages.
    /// </summary>
    private sealed class PooledBuffer : IDisposable
    {
        private byte[]? _buffer;
        private int _position;

        public PooledBuffer(int initialSize)
        {
            _buffer = MessagePool.Rent(initialSize);
            _position = 0;
        }

        public void Write(ReadOnlySpan<byte> data)
        {
            EnsureCapacity(_position + data.Length);
            if (_buffer != null)
            {
                data.CopyTo(_buffer.AsSpan(_position));
                _position += data.Length;
            }
        }

        public ReadOnlyMemory<byte> GetMemory() => _buffer != null ? _buffer.AsMemory(0, _position) : ReadOnlyMemory<byte>.Empty;
        public int Length => _position;

        private void EnsureCapacity(int requiredSize)
        {
            if (_buffer != null && _buffer.Length >= requiredSize)
                return;

            var newSize = Math.Max(_buffer?.Length * 2 ?? 1024, requiredSize);
            var newBuffer = MessagePool.Rent(newSize);
            
            if (_buffer != null)
            {
                _buffer.AsSpan(0, _position).CopyTo(newBuffer);
                MessagePool.Return(_buffer);
            }
            
            _buffer = newBuffer;
        }

        public void Dispose()
        {
            if (_buffer != null)
            {
                MessagePool.Return(_buffer);
                _buffer = null;
            }
        }
    }

    private readonly WS _webSocket;
    private readonly TransportOptions _options;
    private readonly MessageReceivedCallback _onMessage;
    private readonly SessionDisconnectedCallback _onDisconnect;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts;
    private readonly Task _receiveTask;
    private readonly Channel<SendItem> _sendChannel;
    private readonly Task _sendTask;

    private bool _disposed;
    private readonly string _remoteEndpoint;
    private Exception? _disconnectException;

    private readonly record struct SendItem(byte[] Buffer, int Size);

    public long SessionId { get; }
    public string AccountId { get; set; } = string.Empty;
    public bool IsAuthenticated { get; set; }
    public string StageId { get; set; } = string.Empty;
    public bool IsConnected => !_disposed && _webSocket.State == WebSocketState.Open;
    public object? ProcessorContext { get; set; }

    internal WebSocketTransportSession(
        long sessionId,
        WS webSocket,
        TransportOptions options,
        MessageReceivedCallback onMessage,
        SessionDisconnectedCallback onDisconnect,
        ILogger logger,
        CancellationToken externalCt,
        string? remoteEndpoint = null)
    {
        SessionId = sessionId;
        _webSocket = webSocket;
        _options = options;
        _onMessage = onMessage;
        _onDisconnect = onDisconnect;
        _logger = logger;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
        _remoteEndpoint = string.IsNullOrWhiteSpace(remoteEndpoint) ? "unknown" : remoteEndpoint;

        _sendChannel = Channel.CreateUnbounded<SendItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        _sendTask = Task.Run(() => SendLoopAsync(_cts.Token));

        _logger.LogDebug("WebSocket session {SessionId} started", sessionId);
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_disposed || _webSocket.State != WebSocketState.Open) return;
        try
        {
            await _webSocket.SendAsync(data, WebSocketMessageType.Binary, endOfMessage: true, cancellationToken);
        }
        catch (Exception ex)
        {
            _disconnectException ??= ex;
            _logger.LogError(ex, "Error sending data on WebSocket session {SessionId}", SessionId);
            await DisconnectAsync();
        }
    }

    public void SendResponse(string msgId, ushort msgSeq, string stageId, ushort errorCode, ReadOnlySpan<byte> payload)
    {
        if (_disposed || _webSocket.State != WebSocketState.Open) return;

        // 클라이언트는 [4바이트 길이] + [메시지 본문] 형식을 기대함 (TCP와 동일)
        var totalSize = MessageCodec.CalculateResponseSize(msgId, payload.Length, includeLengthPrefix: true);
        var buffer = MessagePool.Rent(totalSize);
        var span = buffer.AsSpan(0, totalSize);
        BinaryPrimitives.WriteInt32LittleEndian(span, totalSize - 4);
        MessageCodec.WriteResponseBody(span[4..], msgId, msgSeq, stageId, errorCode, payload);
        _sendChannel.Writer.TryWrite(new SendItem(buffer, totalSize));
    }

    private async Task SendLoopAsync(CancellationToken ct)
    {
        try
        {
            while (await _sendChannel.Reader.WaitToReadAsync(ct))
            {
                while (_sendChannel.Reader.TryRead(out var item))
                {
                    try
                    {
                        await _webSocket.SendAsync(item.Buffer.AsMemory(0, item.Size), WebSocketMessageType.Binary, true, ct);
                    }
                    catch (Exception ex)
                    {
                        _disconnectException ??= ex;
                        _logger.LogError(ex, "Error sending WebSocket data on session {SessionId}", SessionId);
                        break;
                    }
                    finally { MessagePool.Return(item.Buffer); }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _disconnectException ??= ex;
            _logger?.LogError(ex, "Error in send loop for WebSocket session {SessionId}", SessionId);
        }
    }

    public async ValueTask DisconnectAsync()
    {
        if (_disposed) return;
        try
        {
            if (_webSocket.State == WebSocketState.Open)
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
        }
        catch { }
        await DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _cts.Cancel();
            _sendChannel.Writer.Complete();
            var timeout = Task.Delay(TimeSpan.FromSeconds(5));
            await Task.WhenAny(Task.WhenAll(_receiveTask, _sendTask), timeout);
        }
        catch { }
        finally
        {
            _webSocket.Dispose();
            _cts.Dispose();
            if (_disconnectException != null)
            {
                _logger.LogDebug("WebSocket session {SessionId} ({Remote}) disconnected with error: {ErrorType} {Message}",
                    SessionId,
                    _remoteEndpoint,
                    _disconnectException.GetType().Name,
                    _disconnectException.Message);
            }
            else
            {
                _logger.LogDebug("WebSocket session {SessionId} ({Remote}) disconnected", SessionId, _remoteEndpoint);
            }
            _onDisconnect(this, _disconnectException);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = MessagePool.Rent(_options.ReceiveBufferSize);
        try
        {
            while (!ct.IsCancellationRequested && _webSocket.State == WebSocketState.Open)
            {
                using var pooledBuffer = new PooledBuffer(_options.ReceiveBufferSize);
                ValueWebSocketReceiveResult result;
                do
                {
                    result = await _webSocket.ReceiveAsync(new Memory<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _disconnectException ??= new WebSocketException(WebSocketError.ConnectionClosedPrematurely);
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        return;
                    }
                    pooledBuffer.Write(buffer.AsSpan(0, result.Count));
                    if (pooledBuffer.Length > _options.MaxPacketSize)
                    {
                        _disconnectException ??= new InvalidDataException("Message too large.");
                        await _webSocket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Message too large", CancellationToken.None);
                        return;
                    }
                }
                while (!result.EndOfMessage);

                if (pooledBuffer.Length > 0)
                {
                    try { ParseAndDispatch(pooledBuffer.GetMemory()); }
                    catch (Exception ex)
                    {
                        _disconnectException ??= ex;
                        _logger.LogError(ex, "Error parsing WebSocket message on session {SessionId}", SessionId);
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _disconnectException ??= ex;
            if (!_disposed) _logger.LogError(ex, "Error in receive loop for WebSocket session {SessionId}", SessionId);
        }
        finally { MessagePool.Return(buffer); await DisconnectAsync(); }
    }

    private void ParseAndDispatch(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;

        // 클라이언트 메시지 형식: [4바이트 길이] + [메시지 본문]
        // TCP와 동일한 형식 사용
        if (span.Length < 4)
            throw new InvalidDataException("Invalid message: too short for length prefix");

        var contentLength = BinaryPrimitives.ReadInt32LittleEndian(span);
        if (contentLength <= 0 || contentLength > _options.MaxPacketSize)
            throw new InvalidDataException($"Invalid content length: {contentLength}");

        if (span.Length < 4 + contentLength)
            throw new InvalidDataException("Invalid message: incomplete content");

        var bodySpan = span.Slice(4, contentLength);

        if (!MessageCodec.TryParseMessage(bodySpan, out var msgId, out var msgSeq, out var stageId, out var payloadOffset))
            throw new InvalidDataException("Invalid message format");

        var payloadLength = contentLength - payloadOffset;
        var rentedBuffer = MessagePool.Rent(payloadLength);
        bodySpan.Slice(payloadOffset, payloadLength).CopyTo(rentedBuffer);

        _onMessage(this, msgId, msgSeq, stageId, MessagePoolPayload.Create(rentedBuffer, payloadLength));
    }
}
