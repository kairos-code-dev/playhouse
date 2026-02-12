#nullable enable

using System.Buffers;
using System.Collections.Concurrent;
using System.Text;
using Net.Zmq;
using Google.Protobuf;
using PlayHouse.Abstractions;
using PlayHouse.Runtime.ServerMesh.Message;
using ManagedMessagePool = PlayHouse.Infrastructure.Memory.MessagePool;

namespace PlayHouse.Runtime.ServerMesh.PlaySocket;

/// <summary>
/// Net.Zmq Router socket wrapper for server-to-server communication using MessagePool.
/// </summary>
internal sealed class ZmqPlaySocket : IPlaySocket
{
    private readonly Context _context;
    private readonly Socket _socket;
    private readonly byte[] _serverIdBytes;
    private readonly byte[] _recvServerIdBuffer = new byte[1024];
    private readonly byte[] _recvHeaderBuffer = new byte[65536];
    private readonly ConcurrentDictionary<string, byte[]> _serverIdCache = new();
    private readonly ConcurrentDictionary<int, string> _receivedServerIdCache = new();
    private bool _disposed;
    private string? _boundEndpoint;

    // Header pool to avoid allocations
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

    public ZmqPlaySocket(string serverId, Context context, PlaySocketConfig? config = null)
    {
        ServerId = serverId;
        _serverIdBytes = Encoding.UTF8.GetBytes(serverId);
        _context = context;
        _socket = new Socket(_context, SocketType.Router);
        ConfigureSocket(_socket, config ?? PlaySocketConfig.Default);
    }

    private void ConfigureSocket(Socket socket, PlaySocketConfig config)
    {
        socket.SetOption(SocketOption.Routing_Id, _serverIdBytes);
        socket.SetOption(SocketOption.Router_Handover, 1);
        socket.SetOption(SocketOption.Router_Mandatory, 1);
        socket.SetOption(SocketOption.Immediate, 0);
        socket.SetOption(SocketOption.Sndhwm, config.SendHighWatermark);
        socket.SetOption(SocketOption.Rcvhwm, config.ReceiveHighWatermark);
        socket.SetOption(SocketOption.Rcvtimeo, config.ReceiveTimeout);
        socket.SetOption(SocketOption.Linger, config.Linger);

        if (config.TcpKeepalive)
        {
            socket.SetOption(SocketOption.Tcp_Keepalive, 1);
            socket.SetOption(SocketOption.Tcp_Keepalive_Idle, config.TcpKeepaliveIdle);
            socket.SetOption(SocketOption.Tcp_Keepalive_Intvl, config.TcpKeepaliveInterval);
        }
    }

    public void Bind(string endpoint) { ThrowIfDisposed(); _socket.Bind(endpoint); _boundEndpoint = endpoint; }
    public void Connect(string endpoint) { ThrowIfDisposed(); _socket.Connect(endpoint); }
    public void Disconnect(string endpoint) { ThrowIfDisposed(); _socket.Disconnect(endpoint); }

    public void Send(string serverId, RoutePacket packet)
    {
        ThrowIfDisposed();

        using (packet)
        {
            try
            {
                packet.Header.PayloadSize = (uint)packet.Payload.Length;
                var serverIdBytes = _serverIdCache.GetOrAdd(serverId, id => Encoding.UTF8.GetBytes(id));
                _socket.Send(serverIdBytes, SendFlags.SendMore);

                int headerSize = packet.Header.CalculateSize();
                byte[]? headerBuffer = null;
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
                    if (isPooled && headerBuffer != null)
                    {
                        ManagedMessagePool.Return(headerBuffer);
                    }
                }

                _socket.Send(packet.Payload.DataSpan);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ZmqPlaySocket] Failed to send to {serverId}: {ex.Message}");
            }
        }
    }

    public RoutePacket? Receive()
    {
        ThrowIfDisposed();

        int serverIdLen = _socket.Recv(_recvServerIdBuffer);
        if (serverIdLen <= 0) return null;
        var senderServerId = GetOrCacheServerId(_recvServerIdBuffer, serverIdLen);

        int headerLen = _socket.Recv(_recvHeaderBuffer);
        if (headerLen <= 0) return null;
        
        var header = _headerPool.Get();
        try
        {
            header.MergeFrom(_recvHeaderBuffer.AsSpan(0, headerLen));
        }
        catch
        {
            _headerPool.Return(header);
            throw;
        }

        var payloadSize = (int)header.PayloadSize;
        var payloadBuffer = ManagedMessagePool.Rent(payloadSize);
        _socket.Recv(payloadBuffer.AsSpan(0, payloadSize));

        return RoutePacket.FromMessagePool(header, payloadBuffer, payloadSize, senderServerId, h => _headerPool.Return(h));
    }

    public void ReceiveDirect(int level)
    {
        ThrowIfDisposed();

        int serverIdLen = _socket.Recv(_recvServerIdBuffer);
        if (serverIdLen <= 0) return;

        if (level == 0)
        {
            while (_socket.HasMore) _socket.Recv(_recvHeaderBuffer);
            _socket.Send(_recvServerIdBuffer.AsSpan(0, serverIdLen), SendFlags.SendMore);
            _socket.Send(_recvHeaderBuffer.AsSpan(0, 1024));
            return;
        }

        int headerLen = _socket.Recv(_recvHeaderBuffer);
        if (headerLen <= 0) return;

        if (level >= 1)
        {
            var header = _headerPool.Get();
            header.MergeFrom(_recvHeaderBuffer.AsSpan(0, headerLen));
            _headerPool.Return(header);
        }

        int payloadLen = _socket.Recv(_recvHeaderBuffer);
        _socket.Send(_recvServerIdBuffer.AsSpan(0, serverIdLen), SendFlags.SendMore);
        _socket.Send(_recvHeaderBuffer.AsSpan(0, headerLen), SendFlags.SendMore);
        _socket.Send(_recvHeaderBuffer.AsSpan(0, payloadLen));
    }

    private string GetOrCacheServerId(byte[] buffer, int length)
    {
        int hash = unchecked((int)2166136261);
        for (int i = 0; i < length; i++) hash = unchecked((hash ^ buffer[i]) * 16777619);
        if (_receivedServerIdCache.TryGetValue(hash, out var cached)) return cached;
        var newId = Encoding.UTF8.GetString(buffer, 0, length);
        _receivedServerIdCache.TryAdd(hash, newId);
        return newId;
    }

    private void ThrowIfDisposed() { if (_disposed) throw new ObjectDisposedException(nameof(ZmqPlaySocket)); }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _socket.Dispose();
    }
}