#nullable enable

using System.Collections.Concurrent;
using System.Text;
using Google.Protobuf;
using PlayHouse.Runtime.ServerMesh.Message;
using Zlink;
using ManagedMessagePool = PlayHouse.Infrastructure.Memory.MessagePool;

namespace PlayHouse.Runtime.ServerMesh.PlaySocket;

/// <summary>
/// Zlink Router socket wrapper for server-to-server communication using MessagePool.
/// </summary>
internal sealed class ZlinkPlaySocket : IPlaySocket
{
    private const int ZlinkEagainLinux = 11;
    private const int ZlinkEagainMac = 35;
    private const int ZlinkEwouldblockWindows = 10035;
    private const int ProbeRouterEnabled = 1;
    private const int RouterHandoverEnabled = 1;
    private const int RouterMandatoryEnabled = 1;
    private const int ImmediateDisabled = 0;

    private readonly Socket _socket;
    private readonly MonitorSocket? _monitor;
    private readonly CancellationTokenSource? _monitorCts;
    private readonly Thread? _monitorThread;
    private readonly byte[] _serverIdBytes;
    private readonly byte[] _recvServerIdBuffer = new byte[1024];
    private readonly byte[] _recvHeaderBuffer = new byte[65536];
    private readonly ConcurrentDictionary<string, byte[]> _routerIdBytesCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<int, string> _receivedServerIdCache = new();
    private readonly ConcurrentDictionary<string, byte> _readyRouterIds;
    private readonly int _receiveTimeoutMs;
    private static long _monitorLogCount;
    private static readonly int _monitorLogLimit =
        int.TryParse(Environment.GetEnvironmentVariable("PLAYHOUSE_ZLINK_MONITOR_LOG_LIMIT"), out var monitorLogLimit) && monitorLogLimit > 0
            ? monitorLogLimit
            : 200;
    private static readonly bool _enableSocketMonitor =
        string.Equals(Environment.GetEnvironmentVariable("PLAYHOUSE_ZLINK_MONITOR"), "1", StringComparison.Ordinal);
    private bool _disposed;
    private string? _boundEndpoint;

    private static readonly Microsoft.Extensions.ObjectPool.ObjectPool<Proto.RouteHeader> _headerPool =
        new Microsoft.Extensions.ObjectPool.DefaultObjectPool<Proto.RouteHeader>(new RouteHeaderPoolPolicy());

    private sealed class RouteHeaderPoolPolicy : Microsoft.Extensions.ObjectPool.IPooledObjectPolicy<Proto.RouteHeader>
    {
        public Proto.RouteHeader Create() => new Proto.RouteHeader();

        public bool Return(Proto.RouteHeader obj)
        {
            obj.MsgSeq = 0;
            obj.ServiceId = 0;
            obj.MsgId = string.Empty;
            obj.ErrorCode = 0;
            obj.From = string.Empty;
            obj.StageId = string.Empty;
            obj.AccountId = 0;
            obj.Sid = 0;
            obj.IsReply = false;
            obj.IsSystem = false;
            obj.PayloadSize = 0;
            return true;
        }
    }

    [ThreadStatic]
    private static byte[]? _threadLocalHeaderBuffer;

    public string ServerId { get; }

    public string EndPoint => _boundEndpoint ?? string.Empty;

    public ZlinkPlaySocket(
        string serverId,
        Context context,
        PlaySocketConfig? config = null,
        ConcurrentDictionary<string, byte>? readyRouterIds = null)
    {
        var socketConfig = config ?? PlaySocketConfig.Default;
        ServerId = serverId;
        _serverIdBytes = Encoding.UTF8.GetBytes(serverId);
        _readyRouterIds = readyRouterIds
            ?? new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        _receiveTimeoutMs = socketConfig.ReceiveTimeout;
        _socket = new Socket(context, SocketType.Router);
        ConfigureSocket(_socket, socketConfig);

        if (_enableSocketMonitor)
        {
            try
            {
                _monitor = _socket.MonitorOpen(
                    SocketEvent.All);
                _monitorCts = new CancellationTokenSource();
                _monitorThread = new Thread(() => MonitorLoop(_monitor, _monitorCts.Token))
                {
                    IsBackground = true,
                    Name = $"zlink-monitor:{ServerId}"
                };
                _monitorThread.Start();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ZlinkPlaySocket] Monitor setup failed: {ex.Message}");
            }
        }
    }

    private void ConfigureSocket(Socket socket, PlaySocketConfig config)
    {
        socket.SetOption(SocketOption.RoutingId, _serverIdBytes);
        socket.SetOption(SocketOption.ProbeRouter, ProbeRouterEnabled);
        socket.SetOption(SocketOption.RouterHandover, RouterHandoverEnabled);
        socket.SetOption(SocketOption.RouterMandatory, RouterMandatoryEnabled);
        socket.SetOption(SocketOption.Immediate, ImmediateDisabled);
        socket.SetOption(SocketOption.SndHwm, config.SendHighWatermark);
        socket.SetOption(SocketOption.RcvHwm, config.ReceiveHighWatermark);
        socket.SetOption(SocketOption.RcvTimeo, config.ReceiveTimeout);
        socket.SetOption(SocketOption.Linger, config.Linger);

        if (config.TcpKeepalive)
        {
            socket.SetOption(SocketOption.TcpKeepalive, 1);
            socket.SetOption(SocketOption.TcpKeepaliveIdle, config.TcpKeepaliveIdle);
            socket.SetOption(SocketOption.TcpKeepaliveIntvl, config.TcpKeepaliveInterval);
        }
    }

    public void Bind(string endpoint)
    {
        ThrowIfDisposed();
        _socket.Bind(endpoint);
        _boundEndpoint = endpoint;
    }

    public void Connect(string endpoint, string? routerId = null)
    {
        ThrowIfDisposed();
        if (!string.IsNullOrWhiteSpace(routerId))
        {
            _socket.SetOption(SocketOption.ConnectRoutingId, routerId);
        }

        _socket.Connect(endpoint);
    }

    public void Disconnect(string endpoint)
    {
        ThrowIfDisposed();
        _socket.Disconnect(endpoint);
    }

    public void Send(string routerId, RoutePacket packet)
    {
        ThrowIfDisposed();

        using (packet)
        {
            try
            {
                packet.Header.PayloadSize = (uint)packet.Payload.Length;
                var routingIdBytes = _routerIdBytesCache.GetOrAdd(routerId, id => Encoding.UTF8.GetBytes(id));
                _socket.Send(routingIdBytes, SendFlags.SendMore);

                int headerSize = packet.Header.CalculateSize();
                byte[]? headerBuffer;
                bool isPooled = false;

                if (headerSize <= 128)
                {
                    headerBuffer = _threadLocalHeaderBuffer ??= new byte[128];
                }
                else
                {
                    headerBuffer = ManagedMessagePool.Rent(headerSize);
                    isPooled = true;
                }

                try
                {
                    packet.Header.WriteTo(headerBuffer.AsSpan(0, headerSize));
                    _socket.Send(headerBuffer.AsSpan(0, headerSize), SendFlags.SendMore);
                }
                finally
                {
                    if (isPooled)
                    {
                        ManagedMessagePool.Return(headerBuffer!);
                    }
                }

                _socket.Send(packet.Payload.DataSpan);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ZlinkPlaySocket] Failed to send to {routerId}: {ex.Message}");
            }
        }
    }

    public RoutePacket? Receive()
    {
        ThrowIfDisposed();
        while (true)
        {
            if (!TryReceiveFrame(_recvServerIdBuffer, out int serverIdLen))
            {
                return null;
            }

            if (serverIdLen <= 0)
            {
                continue;
            }

            var senderRouterId = GetOrCacheServerId(_recvServerIdBuffer, serverIdLen);

            if (!HasMoreFrames())
            {
                continue;
            }

            if (!TryReceiveFrame(_recvHeaderBuffer, out int headerLen))
            {
                return null;
            }

            if (headerLen <= 0)
            {
                // Probe message: [routerId][empty-frame]
                MarkRouterIdReady(senderRouterId);
                continue;
            }

            var header = _headerPool.Get();
            try
            {
                header.MergeFrom(_recvHeaderBuffer.AsSpan(0, headerLen));
                if (!string.IsNullOrEmpty(header.From))
                {
                    MarkRouterIdReady(header.From);
                }
            }
            catch
            {
                _headerPool.Return(header);
                throw;
            }

            if (header.PayloadSize > int.MaxValue)
            {
                _headerPool.Return(header);
                throw new InvalidOperationException($"Payload size is too large: {header.PayloadSize}");
            }

            int payloadSize = (int)header.PayloadSize;
            var payloadBuffer = ManagedMessagePool.Rent(payloadSize);

            try
            {
                if (!HasMoreFrames())
                {
                    ManagedMessagePool.Return(payloadBuffer);
                    _headerPool.Return(header);
                    continue;
                }

                if (!TryReceiveFrame(payloadBuffer.AsSpan(0, payloadSize), out int payloadLen))
                {
                    ManagedMessagePool.Return(payloadBuffer);
                    _headerPool.Return(header);
                    return null;
                }

                if (payloadLen >= 0 && payloadLen < payloadSize)
                {
                    payloadSize = payloadLen;
                }
            }
            catch
            {
                ManagedMessagePool.Return(payloadBuffer);
                _headerPool.Return(header);
                throw;
            }

            if (HasMoreFrames())
            {
                DrainRemainingFrames();
            }

            return RoutePacket.FromMessagePool(header, payloadBuffer, payloadSize, senderRouterId, h => _headerPool.Return(h));
        }
    }

    public bool IsRouterIdReady(string routerId)
    {
        if (string.IsNullOrWhiteSpace(routerId))
        {
            return true;
        }

        return _readyRouterIds.ContainsKey(routerId);
    }

    public void MarkRouterIdNotReady(string routerId)
    {
        if (string.IsNullOrWhiteSpace(routerId))
        {
            return;
        }

        _readyRouterIds.TryRemove(routerId, out _);
    }

    public void ReceiveDirect(int level)
    {
        ThrowIfDisposed();

        if (!TryReceiveFrame(_recvServerIdBuffer, out int serverIdLen) || serverIdLen <= 0)
        {
            return;
        }

        if (level == 0)
        {
            int lastFrameLen = 0;
            while (HasMoreFrames())
            {
                if (!TryReceiveFrame(_recvHeaderBuffer, out lastFrameLen))
                {
                    return;
                }
            }

            _socket.Send(_recvServerIdBuffer.AsSpan(0, serverIdLen), SendFlags.SendMore);
            _socket.Send(_recvHeaderBuffer.AsSpan(0, Math.Min(lastFrameLen, 1024)));
            return;
        }

        if (!HasMoreFrames() || !TryReceiveFrame(_recvHeaderBuffer, out int headerLen) || headerLen <= 0)
        {
            return;
        }

        if (level >= 1)
        {
            var header = _headerPool.Get();
            try
            {
                header.MergeFrom(_recvHeaderBuffer.AsSpan(0, headerLen));
            }
            finally
            {
                _headerPool.Return(header);
            }
        }

        if (!HasMoreFrames() || !TryReceiveFrame(_recvHeaderBuffer, out int payloadLen) || payloadLen < 0)
        {
            return;
        }

        _socket.Send(_recvServerIdBuffer.AsSpan(0, serverIdLen), SendFlags.SendMore);
        _socket.Send(_recvHeaderBuffer.AsSpan(0, headerLen), SendFlags.SendMore);
        _socket.Send(_recvHeaderBuffer.AsSpan(0, payloadLen));
    }

    private bool TryReceiveFrame(Span<byte> buffer, out int bytesReceived)
    {
        if (_receiveTimeoutMs < 0)
        {
            while (true)
            {
                try
                {
                    bytesReceived = _socket.Receive(buffer, ReceiveFlags.DontWait);
                    return true;
                }
                catch (ZlinkException ex) when (IsWouldBlock(ex.Errno))
                {
                    Thread.Sleep(1);
                }
            }
        }

        var deadline = Environment.TickCount64 + _receiveTimeoutMs;
        while (true)
        {
            try
            {
                bytesReceived = _socket.Receive(buffer, ReceiveFlags.DontWait);
                return true;
            }
            catch (ZlinkException ex) when (IsWouldBlock(ex.Errno))
            {
                if (Environment.TickCount64 >= deadline)
                {
                    bytesReceived = 0;
                    return false;
                }

                Thread.Sleep(1);
            }
        }
    }

    private bool HasMoreFrames()
    {
        return _socket.GetOption(SocketOption.RcvMore) != 0;
    }

    private void DrainRemainingFrames()
    {
        while (HasMoreFrames())
        {
            if (!TryReceiveFrame(_recvHeaderBuffer, out _))
            {
                break;
            }
        }
    }

    private static bool IsWouldBlock(int errno)
    {
        return errno == ZlinkEagainLinux || errno == ZlinkEagainMac || errno == ZlinkEwouldblockWindows;
    }

    private void MonitorLoop(MonitorSocket monitor, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var evt = monitor.Receive(ReceiveFlags.DontWait);
                var monitorLogCount = Interlocked.Increment(ref _monitorLogCount);
                if (monitorLogCount <= _monitorLogLimit)
                {
                    Console.Error.WriteLine(
                        $"[ZlinkPlaySocket] Monitor event={(SocketEvent)evt.Event}, serverId={ServerId}, rid={FormatRoutingId(evt.RoutingId)}, local={evt.LocalAddress}, remote={evt.RemoteAddress}, value={evt.Value}");
                }
            }
            catch (ZlinkException ex) when (IsWouldBlock(ex.Errno))
            {
                Thread.Sleep(5);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ZlinkPlaySocket] Monitor loop error: {ex.Message}");
                Thread.Sleep(50);
            }
        }
    }

    private static string FormatRoutingId(byte[] routingId)
    {
        if (routingId.Length == 0)
        {
            return "<none>";
        }

        var printable = true;
        for (int i = 0; i < routingId.Length; i++)
        {
            if (routingId[i] < 0x20 || routingId[i] > 0x7E)
            {
                printable = false;
                break;
            }
        }

        return printable
            ? Encoding.UTF8.GetString(routingId)
            : Convert.ToHexString(routingId);
    }

    private void MarkRouterIdReady(string routerId)
    {
        if (string.IsNullOrWhiteSpace(routerId))
        {
            return;
        }

        _readyRouterIds.TryAdd(routerId, 0);
    }

    private string GetOrCacheServerId(byte[] buffer, int length)
    {
        int hash = unchecked((int)2166136261);
        for (int i = 0; i < length; i++)
        {
            hash = unchecked((hash ^ buffer[i]) * 16777619);
        }

        if (_receivedServerIdCache.TryGetValue(hash, out var cached))
        {
            return cached;
        }

        var newId = Encoding.UTF8.GetString(buffer, 0, length);
        _receivedServerIdCache.TryAdd(hash, newId);
        return newId;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ZlinkPlaySocket));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _monitorCts?.Cancel();
        _monitorThread?.Join(200);
        _monitor?.Dispose();
        _monitorCts?.Dispose();
        _socket.Dispose();
    }
}
