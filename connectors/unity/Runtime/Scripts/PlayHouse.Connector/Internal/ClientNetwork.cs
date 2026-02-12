#nullable enable

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PlayHouse.Connector.Network;
using PlayHouse.Connector.Protocol;

namespace PlayHouse.Connector.Internal;

/// <summary>
/// 클라이언트 네트워크 - 연결 관리 및 메시지 송수신
/// </summary>
internal sealed class ClientNetwork : IAsyncDisposable
{
    private readonly ConnectorConfig _config;
    private readonly IConnectorCallback _callback;
    private readonly AsyncManager _asyncManager = new();
    private readonly SynchronizationContext? _syncContext;

    private IConnection? _connection;
    private readonly ConcurrentDictionary<ushort, PendingRequest> _pendingRequests = new();
    private readonly ConcurrentQueue<ParsedPacket> _packetQueue = new();
    private int _msgSeqCounter;
    // Fix #9: Mark as volatile for thread-safe access (written on network thread, read on main thread)
    private volatile bool _isAuthenticated;
    private long _stageId;
    // Fix #10: Mark as volatile for thread-safe access (written on main thread, read on network thread)
    private volatile bool _debugMode;

    // HeartBeat & IdleTimeout
    private readonly Stopwatch _lastReceivedTime = new();
    private readonly Stopwatch _lastSendHeartBeatTime = new();

    public ClientNetwork(ConnectorConfig config, IConnectorCallback callback)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        _syncContext = SynchronizationContext.Current;
    }

    public bool IsConnect() => _connection?.IsConnected ?? false;

    public bool IsAuthenticated() => _isAuthenticated;
    public long StageId => Interlocked.Read(ref _stageId);

    public bool IsDebugMode() => _debugMode;

    public void Connect(string host, int port, bool debugMode = false)
    {
        _debugMode = debugMode;
        _ = ConnectInternalAsync(host, port, debugMode);
    }

    public async Task<bool> ConnectAsync(string host, int port, bool debugMode = false)
    {
        _debugMode = debugMode;
        return await ConnectInternalAsync(host, port, debugMode);
    }

    private async Task<bool> ConnectInternalAsync(string host, int port, bool debugMode)
    {
        try
        {
            // 기존 연결 정리 (재연결 지원)
            await CleanupConnectionAsync();

            // 상태 초기화
            _isAuthenticated = false;
            Interlocked.Exchange(ref _stageId, 0);
            ClearPendingRequests();

            _connection = _config.UseWebsocket
                ? CreateWebSocketConnection()
                : CreateTcpConnection();

            _connection.PacketReceived += OnPacketReceived;
            _connection.Disconnected += OnDisconnected;

            await _connection.ConnectAsync(host, port, _config.UseSsl);

            // 타이머 시작
            _lastReceivedTime.Restart();
            _lastSendHeartBeatTime.Restart();

            _asyncManager.AddJob(() => _callback.ConnectCallback(true));
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Connector] Connect failed: {ex.GetType().Name} {ex.Message}");
            _asyncManager.AddJob(() => _callback.ConnectCallback(false));
            return false;
        }
    }

    private async Task CleanupConnectionAsync()
    {
        if (_connection != null)
        {
            _connection.PacketReceived -= OnPacketReceived;
            _connection.Disconnected -= OnDisconnected;
            await _connection.DisposeAsync();
            _connection = null;
        }
    }

    private IConnection CreateTcpConnection()
    {
        return new TcpConnection(_config);
    }

    private IConnection CreateWebSocketConnection()
    {
        return new WebSocketConnection(_config);
    }

    public async Task DisconnectAsync()
    {
        _isAuthenticated = false;
        Interlocked.Exchange(ref _stageId, 0);
        ClearPendingRequests();
        await CleanupConnectionAsync();
    }

    public void MainThreadAction()
    {
        // 큐에서 패킷 꺼내서 처리
        while (_packetQueue.TryDequeue(out var packet))
        {
            ProcessPacket(packet);
        }

        UpdateClientConnection();
        _asyncManager.MainThreadAction();
    }

    private void UpdateClientConnection()
    {
        if (IsConnect())
        {
            if (!_debugMode)
            {
                SendHeartBeat();

                if (IsHeartbeatTimeout() || IsIdleState())
                {
                    // Heartbeat/Idle timeout - 직접 콜백 호출 후 연결 정리
                    _callback.DisconnectCallback();
                    _ = DisconnectAsync();
                }
            }
        }
    }

    private bool IsHeartbeatTimeout()
    {
        if (_config.HeartbeatTimeoutMs == 0)
        {
            return false;
        }

        return _lastReceivedTime.ElapsedMilliseconds > _config.HeartbeatTimeoutMs;
    }

    private void SendHeartBeat()
    {
        if (_config.HeartBeatIntervalMs == 0)
        {
            return;
        }

        if (_lastSendHeartBeatTime.ElapsedMilliseconds > _config.HeartBeatIntervalMs)
        {
            using var packet = Packet.Empty(PacketConst.HeartBeat);
            Send(packet, 0);
            _lastSendHeartBeatTime.Restart();
        }
    }

    private bool IsIdleState()
    {
        if (!_isAuthenticated || _config.ConnectionIdleTimeoutMs == 0)
        {
            return false;
        }

        return _lastReceivedTime.ElapsedMilliseconds > _config.ConnectionIdleTimeoutMs;
    }

    #region Send/Request

    public void Send(IPacket packet, long stageId)
    {
        if (!IsConnect())
        {
            return;
        }

        var (buffer, length) = EncodePacket(packet, 0, stageId);
        _ = SendAndReturnBufferAsync(buffer, length);
    }

    private async Task SendAndReturnBufferAsync(byte[] buffer, int length)
    {
        try
        {
            await _connection!.SendAsync(buffer.AsMemory(0, length));
        }
        finally
        {
            // Fix #13: Clear buffer to avoid leaking sensitive data
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    public void Request(IPacket request, Action<IPacket> callback, long stageId, bool isAuthenticate = false)
    {
        if (!IsConnect())
        {
            return;
        }

        var msgSeq = GetNextMsgSeq();
        var timeoutCts = new CancellationTokenSource();
        var pending = new PendingRequest
        {
            MsgSeq = msgSeq,
            Request = request,
            StageId = stageId,
            Callback = callback,
            IsAuthenticate = isAuthenticate,
            CreatedAt = DateTime.UtcNow,
            TimeoutCts = timeoutCts
        };

        _pendingRequests[msgSeq] = pending;

        var (buffer, length) = EncodePacket(request, msgSeq, stageId);

        // Fix #14: SendAsync와 타임아웃을 fire-and-forget으로 실행하되, unhandled exceptions를 로깅
        _ = SendRequestAsync(msgSeq, buffer, length, timeoutCts.Token).ContinueWith(t =>
        {
            if (t.IsFaulted && t.Exception != null)
            {
                // 예상치 못한 에러를 로깅 (send failure와 timeout은 이미 SendRequestAsync 내부에서 처리됨)
                Console.WriteLine($"[ClientNetwork] Unhandled error in SendRequestAsync - msgSeq={msgSeq}, error={t.Exception.GetBaseException().Message}");
            }
        }, TaskScheduler.Default);
    }

    private async Task SendRequestAsync(ushort msgSeq, byte[] buffer, int length, CancellationToken timeoutToken)
    {
        try
        {
            await _connection!.SendAsync(buffer.AsMemory(0, length));
        }
        catch (Exception ex)
        {
            // 전송 실패 시 pending request 제거하고 에러 콜백
            if (_pendingRequests.TryRemove(msgSeq, out var req))
            {
                // Log synchronous send error (fire-and-forget pattern)
                Console.WriteLine($"[ClientNetwork] Request send failed - msgSeq={msgSeq}, msgId={req.Request.MsgId}, stageId={req.StageId}, error={ex.Message}");

                req.Dispose();
                _asyncManager.AddJob(() =>
                {
                    _callback.ErrorCallback(req.StageId, (ushort)ConnectorErrorCode.Disconnected, req.Request);
                });
            }
            return;
        }
        finally
        {
            // Fix #13: Clear buffer to avoid leaking sensitive data
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }

        // 타임아웃 설정 - 응답 도착 시 취소됨
        try
        {
            await Task.Delay(_config.RequestTimeoutMs, timeoutToken);
        }
        catch (OperationCanceledException)
        {
            // 응답이 도착해서 타임아웃이 취소됨 - 정상
            return;
        }

        // 타임아웃 발생
        if (_pendingRequests.TryRemove(msgSeq, out var timedOutReq))
        {
            timedOutReq.Dispose();
            _asyncManager.AddJob(() =>
            {
                _callback.ErrorCallback(timedOutReq.StageId, (ushort)ConnectorErrorCode.RequestTimeout, timedOutReq.Request);
            });
        }
    }

    public async Task<IPacket> RequestAsync(IPacket request, long stageId, bool isAuthenticate = false)
    {
        if (!IsConnect())
        {
            throw new ConnectorException(stageId, (ushort)ConnectorErrorCode.Disconnected, request, 0);
        }

        var tcs = new TaskCompletionSource<IPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
        var msgSeq = GetNextMsgSeq();

        var pending = new PendingRequest
        {
            MsgSeq = msgSeq,
            Request = request,
            StageId = stageId,
            Tcs = tcs,
            IsAuthenticate = isAuthenticate,
            CreatedAt = DateTime.UtcNow
        };

        _pendingRequests[msgSeq] = pending;

        var (buffer, length) = EncodePacket(request, msgSeq, stageId);
        try
        {
            await _connection!.SendAsync(buffer.AsMemory(0, length));
        }
        finally
        {
            // Fix #13: Clear buffer to avoid leaking sensitive data
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }

        // 타임아웃 설정
        using var cts = new CancellationTokenSource(_config.RequestTimeoutMs);
        cts.Token.Register(() =>
        {
            if (_pendingRequests.TryRemove(msgSeq, out var pending))
            {
                pending.Dispose();
                tcs.TrySetException(new ConnectorException(stageId, (ushort)ConnectorErrorCode.RequestTimeout, request, msgSeq));
            }
        });

        return await tcs.Task;
    }

    #endregion

    #region Packet Encoding

    private (byte[] buffer, int length) EncodePacket(IPacket packet, ushort msgSeq, long stageId)
    {
        var msgIdByteCount = Encoding.UTF8.GetByteCount(packet.MsgId);
        if (msgIdByteCount > 255)
        {
            throw new ArgumentException($"Message ID too long: {packet.MsgId}");
        }

        var payloadSpan = packet.Payload.DataSpan;

        // Protocol: MsgIdLen(1) + MsgId(N) + MsgSeq(2) + Payload
        var contentSize = 1 + msgIdByteCount + 2 + payloadSpan.Length;
        var totalSize = 4 + contentSize;
        var buffer = ArrayPool<byte>.Shared.Rent(totalSize);

        int offset = 0;

        // Length prefix (4 bytes, little-endian)
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(offset), contentSize);
        offset += 4;

        // MsgIdLen (1 byte)
        buffer[offset++] = (byte)msgIdByteCount;

        // MsgId (N bytes) - direct encoding to buffer
        Encoding.UTF8.GetBytes(packet.MsgId, buffer.AsSpan(offset, msgIdByteCount));
        offset += msgIdByteCount;

        // MsgSeq (2 bytes, little-endian)
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), msgSeq);
        offset += 2;

        // Payload - direct copy from span
        payloadSpan.CopyTo(buffer.AsSpan(offset));

        return (buffer, totalSize);
    }

    #endregion

    #region Packet Handling

    private void OnPacketReceived(object? sender, IPacket packet)
    {
        // 데이터 수신 시 타이머 리셋
        _lastReceivedTime.Restart();

        // Cast to ParsedPacket to get metadata
        if (packet is not ParsedPacket parsed)
        {
            return;
        }

        // HeartBeat 메시지는 즉시 처리하고 dispose
        if (parsed.MsgId == PacketConst.HeartBeat)
        {
            packet.Dispose();
            return;
        }

        // Response 메시지인 경우 (MsgSeq > 0): RequestAsync vs RequestCallback 분기
        if (parsed.MsgSeq > 0)
        {
            // RequestAsync(Tcs) 응답인지 확인
            // - await 패턴은 스레드 전환을 자동으로 처리 (ConfigureAwait)
            // - 네트워크 스레드에서 TrySetResult() 호출해도 안전
            // - SyncContext.Post() 오버헤드 제거로 성능 향상
            if (_pendingRequests.TryGetValue(parsed.MsgSeq, out var pending) && pending.Tcs != null)
            {
                ProcessPacket(parsed);
                return;
            }
            // RequestCallback 응답은 아래 SyncContext/큐 처리로 진행
        }

        // RequestCallback 또는 Push 메시지
        // - Callback/ReceiveCallback은 메인 스레드에서 호출되어야 함 (Unity 호환성)
        // - SyncContext가 있으면 해당 스레드로 Post (Unity: 다음 프레임)
        // - 없으면 큐에 추가 후 MainThreadAction()에서 처리
        if (_syncContext != null)
        {
            _syncContext.Post(_ => ProcessPacket(parsed), null);
        }
        else
        {
            _packetQueue.Enqueue(parsed);
        }
    }

    private void ProcessPacket(ParsedPacket parsed)
    {
        // Response 처리 (MsgSeq > 0)
        if (parsed.MsgSeq > 0)
        {
            // Fix #6: Race condition - if TryRemove fails, timeout already handled it.
            // Dispose packet and return to prevent routing to push handler.
            if (!_pendingRequests.TryRemove(parsed.MsgSeq, out var pending))
            {
                parsed.Dispose();
                return;
            }

            // 타임아웃 타이머 취소
            pending.Dispose();

            if (pending.IsAuthenticate && parsed.ErrorCode == 0)
            {
                _isAuthenticated = true;
                if (TryExtractStageIdFromAuthPayload(parsed.Payload.DataSpan, out var authStageId) && authStageId > 0)
                {
                    Interlocked.Exchange(ref _stageId, authStageId);
                }
            }

            if (parsed.ErrorCode != 0)
            {
                parsed.Dispose(); // Error case - dispose packet
                if (pending.Tcs != null)
                {
                    pending.Tcs.TrySetException(new ConnectorException(StageId, parsed.ErrorCode, pending.Request, parsed.MsgSeq));
                }
                else if (pending.Callback != null)
                {
                    _callback.ErrorCallback(StageId, parsed.ErrorCode, pending.Request);
                }
            }
            else
            {
                if (pending.Tcs != null)
                {
                    // Async pattern - caller owns the packet and is responsible for disposal
                    pending.Tcs.TrySetResult(parsed);
                }
                else if (pending.Callback != null)
                {
                    // Callback pattern - dispose after callback
                    try
                    {
                        pending.Callback(parsed);
                    }
                    finally
                    {
                        parsed.Dispose();
                    }
                }
            }

            return;
        }

        // Push 메시지 처리 (MsgSeq == 0)
        try
        {
            _callback.ReceiveCallback(StageId, parsed);
        }
        finally
        {
            parsed.Dispose();
        }
    }

    private void OnDisconnected(object? sender, Exception? exception)
    {
        _isAuthenticated = false;
        Interlocked.Exchange(ref _stageId, 0);
        ClearPendingRequests();

        if (exception != null)
        {
            Console.Error.WriteLine(
                $"[Connector] Disconnected with error: {exception.GetType().Name} {exception.Message}");
        }

        _asyncManager.AddJob(() => _callback.DisconnectCallback());
    }

    #endregion

    #region Helpers

    private ushort GetNextMsgSeq()
    {
        int msgSeq;
        do
        {
            msgSeq = Interlocked.Increment(ref _msgSeqCounter);
        }
        while ((msgSeq & 0xFFFF) == 0);

        return (ushort)(msgSeq & 0xFFFF);
    }

    private void ClearPendingRequests()
    {
        var pending = _pendingRequests.ToArray();
        _pendingRequests.Clear();

        foreach (var kvp in pending)
        {
            kvp.Value.Dispose();
            kvp.Value.Tcs?.TrySetCanceled();
        }
    }

    #endregion

    private static bool TryExtractStageIdFromAuthPayload(ReadOnlySpan<byte> payload, out long stageId)
    {
        stageId = 0;
        int offset = 0;

        while (offset < payload.Length)
        {
            if (!TryReadVarint(payload, ref offset, out var tag))
            {
                return false;
            }

            var wireType = (int)(tag & 0x07);
            var fieldNumber = (int)(tag >> 3);

            if (fieldNumber == 2 && wireType == 0)
            {
                if (!TryReadVarint(payload, ref offset, out var value))
                {
                    return false;
                }

                stageId = (long)value;
                return true;
            }

            if (!SkipField(payload, ref offset, wireType))
            {
                return false;
            }
        }

        return false;
    }

    private static bool TryReadVarint(ReadOnlySpan<byte> data, ref int offset, out ulong value)
    {
        value = 0;
        int shift = 0;
        while (offset < data.Length && shift < 64)
        {
            var b = data[offset++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return true;
            }
            shift += 7;
        }

        return false;
    }

    private static bool SkipField(ReadOnlySpan<byte> data, ref int offset, int wireType)
    {
        return wireType switch
        {
            0 => TryReadVarint(data, ref offset, out _),
            1 => SkipBytes(data, ref offset, 8),
            2 => SkipLengthDelimited(data, ref offset),
            5 => SkipBytes(data, ref offset, 4),
            _ => false
        };
    }

    private static bool SkipLengthDelimited(ReadOnlySpan<byte> data, ref int offset)
    {
        if (!TryReadVarint(data, ref offset, out var len))
        {
            return false;
        }

        return SkipBytes(data, ref offset, (int)len);
    }

    private static bool SkipBytes(ReadOnlySpan<byte> data, ref int offset, int count)
    {
        if (offset + count > data.Length)
        {
            return false;
        }

        offset += count;
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        _isAuthenticated = false;
        Interlocked.Exchange(ref _stageId, 0);
        ClearPendingRequests();

        // Fix #11: Detach event handlers and cleanup connection
        await CleanupConnectionAsync();
    }

    private sealed class PendingRequest : IDisposable
    {
        public ushort MsgSeq { get; init; }
        public IPacket Request { get; init; } = null!;
        public long StageId { get; init; }
        public Action<IPacket>? Callback { get; init; }
        public TaskCompletionSource<IPacket>? Tcs { get; init; }
        public bool IsAuthenticate { get; init; }
        public DateTime CreatedAt { get; init; }
        public CancellationTokenSource? TimeoutCts { get; init; }

        public void Dispose()
        {
            TimeoutCts?.Cancel();
            TimeoutCts?.Dispose();
        }
    }
}

/// <summary>
/// Connector 콜백 인터페이스
/// </summary>
internal interface IConnectorCallback
{
    void ConnectCallback(bool result);
    void ReceiveCallback(long stageId, IPacket packet);
    void ErrorCallback(long stageId, ushort errorCode, IPacket request);
    void DisconnectCallback();
}
