#nullable enable

using PlayHouse.Abstractions;
using PlayHouse.Runtime.ClientTransport;
using PlayHouse.Runtime.ServerMesh.Message;

namespace PlayHouse.Core.Play;

/// <summary>
/// Base abstract type for all messages dispatched to PlayDispatcher.
/// </summary>
/// <remarks>
/// PlayMessage provides a unified abstraction for different message types:
/// - RouteMessage: Server-to-server route packets
/// - TimerMessage: Timer callback notifications
/// - AsyncMessage: AsyncBlock post-callback results
/// - DestroyMessage: Stage destruction requests
/// </remarks>
internal abstract class PlayMessage : IDisposable
{
    /// <summary>
    /// Gets the target stage ID.
    /// </summary>
    public string StageId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="PlayMessage"/> class.
    /// </summary>
    /// <param name="stageId">Target stage ID.</param>
    protected PlayMessage(string stageId)
    {
        StageId = stageId;
    }

    /// <inheritdoc/>
    public virtual void Dispose()
    {
        // Base implementation does nothing; override if needed
    }
}

/// <summary>
/// Message wrapping a RuntimeRoutePacket for stage processing.
/// </summary>
internal sealed class RouteMessage : PlayMessage
{
    /// <summary>
    /// Gets the wrapped route packet.
    /// </summary>
    public RoutePacket RoutePacket { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RouteMessage"/> class.
    /// </summary>
    /// <param name="routePacket">The route packet to wrap.</param>
    public RouteMessage(RoutePacket routePacket)
        : base(routePacket.StageId)
    {
        RoutePacket = routePacket;
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        RoutePacket.Dispose();
    }
}

/// <summary>
/// Message wrapping a TimerPacket for timer callback execution.
/// </summary>
internal sealed class TimerMessage : PlayMessage
{
    /// <summary>
    /// Gets the wrapped timer packet.
    /// </summary>
    public TimerPacket TimerPacket { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TimerMessage"/> class.
    /// </summary>
    /// <param name="timerPacket">The timer packet to wrap.</param>
    public TimerMessage(TimerPacket timerPacket)
        : base(timerPacket.StageId)
    {
        TimerPacket = timerPacket;
    }
}

/// <summary>
/// Message wrapping an AsyncBlockPacket for post-callback execution.
/// </summary>
internal sealed class AsyncMessage : PlayMessage
{
    /// <summary>
    /// Gets the wrapped async block packet.
    /// </summary>
    public AsyncBlockPacket AsyncBlockPacket { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncMessage"/> class.
    /// </summary>
    /// <param name="asyncBlockPacket">The async block packet to wrap.</param>
    public AsyncMessage(AsyncBlockPacket asyncBlockPacket)
        : base(asyncBlockPacket.StageId)
    {
        AsyncBlockPacket = asyncBlockPacket;
    }
}

/// <summary>
/// Message requesting stage destruction.
/// </summary>
internal sealed class DestroyMessage : PlayMessage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DestroyMessage"/> class.
    /// </summary>
    /// <param name="stageId">The stage ID to destroy.</param>
    public DestroyMessage(string stageId)
        : base(stageId)
    {
    }
}

/// <summary>
/// Message routing client messages to Actor via AccountId.
/// </summary>
internal sealed class ClientRouteMessage : PlayMessage
{
    /// <summary>
    /// Gets the account ID for actor routing.
    /// </summary>
    public string AccountId { get; }

    /// <summary>
    /// Gets the message ID.
    /// </summary>
    public string MsgId { get; }

    /// <summary>
    /// Gets the message sequence number.
    /// </summary>
    public ushort MsgSeq { get; }

    /// <summary>
    /// Gets the session ID.
    /// </summary>
    public long Sid { get; }

    /// <summary>
    /// Gets the message payload (must be disposed).
    /// </summary>
    public IPayload Payload { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientRouteMessage"/> class.
    /// </summary>
    /// <param name="stageId">Target stage ID.</param>
    /// <param name="accountId">Account ID for actor routing.</param>
    /// <param name="msgId">Message ID.</param>
    /// <param name="msgSeq">Message sequence number.</param>
    /// <param name="sid">Session ID.</param>
    /// <param name="payload">Message payload.</param>
    public ClientRouteMessage(
        string stageId,
        string accountId,
        string msgId,
        ushort msgSeq,
        long sid,
        IPayload payload)
        : base(stageId)
    {
        AccountId = accountId;
        MsgId = msgId;
        MsgSeq = msgSeq;
        Sid = sid;
        Payload = payload;
    }

    /// <summary>
    /// Disposes resources including the payload.
    /// </summary>
    public override void Dispose()
    {
        Payload?.Dispose();
    }
}

/// <summary>
/// Message notifying Stage that an authenticated Actor is ready to join.
/// </summary>
internal sealed class JoinActorMessage : PlayMessage
{
    /// <summary>
    /// Gets the authenticated BaseActor ready to join the Stage.
    /// </summary>
    public Base.BaseActor Actor { get; }

    /// <summary>
    /// Gets the transport session for sending auth reply.
    /// </summary>
    public ITransportSession Session { get; }

    /// <summary>
    /// Gets the message sequence number for auth reply.
    /// </summary>
    public ushort MsgSeq { get; }

    /// <summary>
    /// Gets the auth reply message ID.
    /// </summary>
    public string AuthReplyMsgId { get; }

    /// <summary>
    /// Gets the auth request payload (must be disposed).
    /// </summary>
    public IPayload Payload { get; }

    /// <summary>
    /// Gets the authentication reply packet from OnAuthenticate callback.
    /// </summary>
    public IPacket? AuthReplyPacket { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="JoinActorMessage"/> class.
    /// </summary>
    /// <param name="stageId">Target stage ID.</param>
    /// <param name="actor">The authenticated actor.</param>
    /// <param name="session">Transport session for sending auth reply.</param>
    /// <param name="msgSeq">Message sequence number for auth reply.</param>
    /// <param name="authReplyMsgId">Auth reply message ID.</param>
    /// <param name="payload">Auth request payload.</param>
    /// <param name="authReplyPacket">Authentication reply packet from OnAuthenticate.</param>
    public JoinActorMessage(
        string stageId,
        Base.BaseActor actor,
        ITransportSession session,
        ushort msgSeq,
        string authReplyMsgId,
        IPayload payload,
        IPacket? authReplyPacket = null)
        : base(stageId)
    {
        Actor = actor;
        Session = session;
        MsgSeq = msgSeq;
        AuthReplyMsgId = authReplyMsgId;
        Payload = payload;
        AuthReplyPacket = authReplyPacket;
    }

    /// <summary>
    /// Disposes resources including the payload and auth reply packet.
    /// </summary>
    public override void Dispose()
    {
        Payload?.Dispose();
        AuthReplyPacket?.Dispose();
    }
}

/// <summary>
/// Message notifying Stage that a client disconnected.
/// </summary>
internal sealed class DisconnectMessage : PlayMessage
{
    /// <summary>
    /// Gets the account ID of the disconnected client.
    /// </summary>
    public string AccountId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DisconnectMessage"/> class.
    /// </summary>
    /// <param name="stageId">Target stage ID.</param>
    /// <param name="accountId">Account ID of disconnected client.</param>
    public DisconnectMessage(string stageId, string accountId)
        : base(stageId)
    {
        AccountId = accountId;
    }
}
