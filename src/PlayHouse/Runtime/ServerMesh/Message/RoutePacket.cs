#nullable enable

using Google.Protobuf;
using Microsoft.Extensions.ObjectPool;
using PlayHouse.Abstractions;
using PlayHouse.Infrastructure.Memory;
using PlayHouse.Runtime.Proto;

namespace PlayHouse.Runtime.ServerMesh.Message;

/// <summary>
/// Server-to-server route packet for ZMQ communication.
/// </summary>
public sealed class RoutePacket : IDisposable
{
    private static readonly ObjectPool<RoutePacket> _pool = 
        new DefaultObjectPool<RoutePacket>(new DefaultPooledObjectPolicy<RoutePacket>());

    /// <summary>
    /// Gets the route header containing routing metadata.
    /// </summary>
    public RouteHeader Header { get; private set; } = null!;

    /// <summary>
    /// Gets the payload data.
    /// </summary>
    public IPayload Payload { get; private set; } = null!;

    private bool _ownsPayload;
    private Action<RouteHeader>? _returnHeaderAction;
    private bool _disposed;

    // Internal constructor for pooling
    public RoutePacket() { }

    private void Update(RouteHeader header, IPayload payload, bool ownsPayload = true, Action<RouteHeader>? returnHeaderAction = null)
    {
        Header = header;
        Payload = payload;
        _ownsPayload = ownsPayload;
        _returnHeaderAction = returnHeaderAction;
        _disposed = false;
    }

    #region Factory Methods

    public static RoutePacket Create(RouteHeader header, IPayload payload, bool ownsPayload = true, Action<RouteHeader>? returnHeaderAction = null)
    {
        var packet = _pool.Get();
        packet.Update(header, payload, ownsPayload, returnHeaderAction);
        return packet;
    }

    /// <summary>
    /// Creates a route packet from received frame data.
    /// </summary>
    public static RoutePacket FromFrames(byte[] headerBytes, byte[] payloadBytes, string? senderNid = null)
    {
        var header = RouteHeader.Parser.ParseFrom(headerBytes);
        if (string.IsNullOrWhiteSpace(header.From) && !string.IsNullOrWhiteSpace(senderNid))
        {
            header.From = senderNid;
        }
        
        var payload = new MemoryPayload(payloadBytes);
        return Create(header, payload);
    }

   /// <summary>
    /// Creates a route packet from a parsed RouteHeader and MessagePool buffer payload.
    /// </summary>
    public static RoutePacket FromMessagePool(
        RouteHeader header,
        byte[] payloadBuffer,
        int payloadSize,
        string? senderNid = null,
        Action<RouteHeader>? returnHeaderAction = null)
    {
        if (string.IsNullOrWhiteSpace(header.From) && !string.IsNullOrWhiteSpace(senderNid))
        {
            header.From = senderNid;
        }

        var payload = MessagePoolPayload.Create(payloadBuffer, payloadSize);
        return Create(header, payload, ownsPayload: true, returnHeaderAction: returnHeaderAction);
    }

   /// <summary>
    /// Creates a route packet with custom header and IPayload (shared reference).
    /// Used for sending - does NOT own the payload.
    /// </summary>
    public static RoutePacket Of(RouteHeader header, IPayload payload)
    {
        // For sending: RoutePacket does NOT own the payload
        return Create(header, payload, ownsPayload: false);
    }

    /// <summary>
    /// Creates a route packet with custom header and byte array payload.
    /// </summary>
    public static RoutePacket Of(RouteHeader header, byte[] payload)
    {
        return Create(header, new MemoryPayload(payload));
    }

    /// <summary>
    /// Creates an empty route packet (for error responses).
    /// </summary>
    public static RoutePacket Empty(RouteHeader header)
    {
        return Create(header, EmptyPayload.Instance);
    }

    #endregion

    #region Reply Factory Methods

    /// <summary>
    /// Creates an error reply packet for this request.
    /// </summary>
    public RoutePacket CreateErrorReply(ushort errorCode)
    {
        var replyHeader = new RouteHeader
        {
            MsgSeq = Header.MsgSeq,
            ServiceId = Header.ServiceId,
            MsgId = Header.MsgId,
            From = Header.From,
            ErrorCode = errorCode,
            IsReply = true,
            StageId = Header.StageId,
            AccountId = Header.AccountId,
            Sid = Header.Sid
        };
        return Create(replyHeader, EmptyPayload.Instance);
    }

    #endregion

    #region Serialization

    /// <summary>
    /// Serializes the header to bytes for sending.
    /// </summary>
    public byte[] SerializeHeader()
    {
        return Header.ToByteArray();
    }

    /// <summary>
    /// Gets the payload data as a ReadOnlySpan for zero-copy access.
    /// </summary>
    public ReadOnlySpan<byte> GetPayloadSpan() => Payload.DataSpan;

    #endregion

    #region Convenience Properties

    public string MsgId => Header.MsgId;
    public ushort MsgSeq => (ushort)Header.MsgSeq;
    public ushort ErrorCode => (ushort)Header.ErrorCode;
    public string From => Header.From;
    public string StageId => Header.StageId;
    public long AccountId => Header.AccountId;
    public long Sid => Header.Sid;
    public bool IsError => Header.ErrorCode != 0;

    #endregion

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_ownsPayload)
        {
            Payload.Dispose();
        }

        if (_returnHeaderAction != null && Header != null)
        {
            _returnHeaderAction(Header);
        }

        Payload = null!;
        Header = null!;
        _returnHeaderAction = null;
        _pool.Return(this);
    }
}
