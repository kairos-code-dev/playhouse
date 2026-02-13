#nullable enable

using System.IO;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Infrastructure.Memory;

namespace PlayHouse.Runtime.ClientTransport.Zlink;

/// <summary>
/// Stream session backed by a shared Zlink STREAM socket.
/// </summary>
internal sealed class ZlinkStreamTransportSession : ITransportSession
{
    private readonly byte[] _routingId;
    private readonly string _routingKey;
    private readonly MessageReceivedCallback _onMessage;
    private readonly SessionDisconnectedCallback _onDisconnect;
    private readonly ILogger _logger;
    private readonly ZlinkStreamTransportServer _server;
    private readonly string _remoteEndpoint;

    private readonly object _sendLock = new();
    private readonly Queue<SendItem> _sendQueue = new();

    private bool _isSending;
    private bool _disposed;
    private Exception? _disconnectException;

    private readonly record struct SendItem(byte[] Buffer, int Size);

    internal ZlinkStreamTransportSession(
        long sessionId,
        string routingKey,
        byte[] routingId,
        MessageReceivedCallback onMessage,
        SessionDisconnectedCallback onDisconnect,
        ILogger logger,
        ZlinkStreamTransportServer server,
        string? remoteEndpoint = null)
    {
        SessionId = sessionId;
        _routingKey = routingKey;
        _routingId = routingId;
        _onMessage = onMessage;
        _onDisconnect = onDisconnect;
        _logger = logger;
        _server = server;
        _remoteEndpoint = string.IsNullOrWhiteSpace(remoteEndpoint) ? "unknown" : remoteEndpoint;
    }

    public long SessionId { get; }
    public string AccountId { get; set; } = string.Empty;
    public bool IsAuthenticated { get; set; }
    public string StageId { get; set; } = string.Empty;
    public bool IsConnected => !_disposed;
    public object? ProcessorContext { get; set; }

    internal string RoutingKey => _routingKey;

    internal void OnIncomingData(ReadOnlySpan<byte> data)
    {
        if (_disposed || data.IsEmpty)
        {
            return;
        }

        try
        {
            if (!MessageCodec.TryParseMessage(data, out var msgId, out var msgSeq, out var stageId, out var payloadOffset))
            {
                throw new InvalidDataException("Invalid message format");
            }

            var payloadLength = data.Length - payloadOffset;
            var rented = MessagePool.Rent(payloadLength);
            data[payloadOffset..].CopyTo(rented);

            _onMessage(this, msgId, msgSeq, stageId, MessagePoolPayload.Create(rented, payloadLength));
        }
        catch (Exception ex)
        {
            _disconnectException ??= ex;
            _logger.LogError(ex, "Failed to parse stream data for session {SessionId}", SessionId);
            _ = DisconnectAsync();
        }
    }

    public ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (_disposed || data.IsEmpty)
        {
            return ValueTask.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            _server.SendFrame(_routingId, data.Span);
        }
        catch (Exception ex)
        {
            _disconnectException ??= ex;
            _logger.LogError(ex, "Failed to send stream data for session {SessionId}", SessionId);
            _ = DisconnectAsync();
        }

        return ValueTask.CompletedTask;
    }

    public void SendResponse(string msgId, ushort msgSeq, string stageId, ushort errorCode, ReadOnlySpan<byte> payload)
    {
        if (_disposed)
        {
            return;
        }

        // STREAM RAW transport adds wire length prefix internally.
        var totalSize = MessageCodec.CalculateResponseSize(msgId, payload.Length, includeLengthPrefix: false);
        var buffer = MessagePool.Rent(totalSize);

        var span = buffer.AsSpan(0, totalSize);
        MessageCodec.WriteResponseBody(span, msgId, msgSeq, stageId, errorCode, payload);

        lock (_sendLock)
        {
            _sendQueue.Enqueue(new SendItem(buffer, totalSize));
            if (_isSending)
            {
                return;
            }

            _isSending = true;
            _ = ProcessSendQueueAsync();
        }
    }

    public ValueTask DisconnectAsync()
    {
        _ = DisposeAsync();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;

        try
        {
            _server.TryDisconnect(_routingId);
        }
        catch (Exception ex)
        {
            _disconnectException ??= ex;
            _logger.LogDebug(ex, "Stream disconnect signal failed for session {SessionId}", SessionId);
        }

        lock (_sendLock)
        {
            while (_sendQueue.Count > 0)
            {
                var item = _sendQueue.Dequeue();
                MessagePool.Return(item.Buffer);
            }
        }

        if (_disconnectException != null)
        {
            _logger.LogDebug(
                "Stream session {SessionId} ({Remote}) disconnected with error: {ErrorType} {Message}",
                SessionId,
                _remoteEndpoint,
                _disconnectException.GetType().Name,
                _disconnectException.Message);
        }
        else
        {
            _logger.LogDebug("Stream session {SessionId} ({Remote}) disconnected", SessionId, _remoteEndpoint);
        }

        _onDisconnect(this, _disconnectException);
        return ValueTask.CompletedTask;
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
                    if (_sendQueue.Count == 0 || _disposed)
                    {
                        _isSending = false;
                        return;
                    }

                    item = _sendQueue.Dequeue();
                }

                try
                {
                    _server.SendFrame(_routingId, item.Buffer.AsSpan(0, item.Size));
                }
                catch (Exception ex)
                {
                    _disconnectException ??= ex;
                    _logger.LogError(ex, "Failed to send response for session {SessionId}", SessionId);
                    _ = DisconnectAsync();
                    return;
                }
                finally
                {
                    MessagePool.Return(item.Buffer);
                }

                await Task.Yield();
            }
        }
        finally
        {
            lock (_sendLock)
            {
                _isSending = false;
            }
        }
    }
}
