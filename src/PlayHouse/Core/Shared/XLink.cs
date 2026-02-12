#nullable enable

using PlayHouse.Abstractions;
using PlayHouse.Core.Messaging;
using PlayHouse.Runtime.Shared;
using PlayHouse.Runtime.ServerMesh.Communicator;
using PlayHouse.Runtime.ServerMesh.Discovery;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.Proto;

namespace PlayHouse.Core.Shared;

/// <summary>
/// Base implementation of ILink providing common functionality.
/// </summary>
/// <remarks>
/// XLink handles the low-level details of message routing and request-reply
/// coordination. Derived classes (XActorLink, XStageLink, ApiLink) add
/// specialized functionality.
/// </remarks>
public abstract class XLink : ILink
{
    private readonly IClientCommunicator _communicator;
    private readonly RequestCache _requestCache;
    private readonly IServerInfoCenter _serverInfoCenter;
    private readonly int _requestTimeoutMs;

    // 전역 MsgSeq 카운터 - 모든 XLink 인스턴스가 공유하여 RequestCache 키 충돌 방지
    private static int _globalMsgSeqCounter;

    /// <summary>
    /// Gets the current RouteHeader for reply routing.
    /// </summary>
    internal RouteHeader? CurrentHeader { get; private set; }

    /// <inheritdoc/>
    public ServerType ServerType { get; }

    /// <inheritdoc/>
    public ushort ServiceId { get; }

    /// <summary>
    /// Gets the ServerId of this sender.
    /// </summary>
    protected string ServerId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="XLink"/> class.
    /// </summary>
    /// <param name="communicator">Communicator for sending messages.</param>
    /// <param name="requestCache">Cache for tracking pending requests.</param>
    /// <param name="serverInfoCenter">Server information center for service discovery.</param>
    /// <param name="serverType">Server type of this sender.</param>
    /// <param name="serviceId">Service ID of this sender.</param>
    /// <param name="serverId">ServerId of this sender.</param>
    /// <param name="requestTimeoutMs">Request timeout in milliseconds.</param>
    protected XLink(
        IClientCommunicator communicator,
        RequestCache requestCache,
        IServerInfoCenter serverInfoCenter,
        ServerType serverType,
        ushort serviceId,
        string serverId,
        int requestTimeoutMs = 30000)
    {
        _communicator = communicator;
        _requestCache = requestCache;
        _serverInfoCenter = serverInfoCenter;
        ServerType = serverType;
        ServiceId = serviceId;
        ServerId = serverId;
        _requestTimeoutMs = requestTimeoutMs;
    }

    /// <summary>
    /// Sets the current header for reply routing.
    /// </summary>
    /// <param name="header">The incoming request header.</param>
    public void SetCurrentHeader(RouteHeader header)
    {
        CurrentHeader = header;
    }

    /// <summary>
    /// Clears the current header.
    /// </summary>
    public void ClearCurrentHeader()
    {
        CurrentHeader = null;
    }

    /// <summary>
    /// Gets the link's Stage ID for Stage-to-Stage communication.
    /// Override this in XStageLink to return the actual StageId.
    /// </summary>
    /// <returns>Stage ID for the link (0 if not a Stage).</returns>
    protected virtual long GetSenderStageId()
    {
        return 0;
    }

    /// <summary>
    /// Gets the delegate for posting callbacks to Stage event loop.
    /// Override this in XStageLink to return the post callback delegate.
    /// </summary>
    /// <returns>Callback delegate (null if not a Stage).</returns>
    protected virtual Action<ReplyCallback, ushort, IPacket?>? GetPostToStageCallback()
    {
        return null;
    }

    /// <summary>
    /// Gets the next message sequence number.
    /// </summary>
    /// <returns>A new sequence number (1-65535).</returns>
    protected ushort NextMsgSeq()
    {
        var seq = Interlocked.Increment(ref _globalMsgSeqCounter);
        // Wrap around at 65535 and skip 0 (0 means no reply expected)
        if (seq > 65535)
        {
            Interlocked.CompareExchange(ref _globalMsgSeqCounter, 1, seq);
            seq = Interlocked.Increment(ref _globalMsgSeqCounter);
        }
        if (seq == 0) seq = Interlocked.Increment(ref _globalMsgSeqCounter);
        return (ushort)seq;
    }

    #region API Communication

    /// <inheritdoc/>
    public void SendToApi(string apiServerId, IPacket packet)
    {
        var header = CreateApiHeader(packet.MsgId, msgSeq: 0);
        SendInternal(apiServerId, header, packet.Payload);
    }

    /// <inheritdoc/>
    public void RequestToApi(string apiServerId, IPacket packet, ReplyCallback replyCallback)
    {
        var msgSeq = NextMsgSeq();
        var replyObject = ReplyObject.CreateCallback(msgSeq, replyCallback, GetPostToStageCallback());
        var header = CreateApiHeader(packet.MsgId, msgSeq);
        SendRequest(apiServerId, header, packet.Payload, replyObject);
    }

    /// <inheritdoc/>
    public async Task<IPacket> RequestToApi(string apiServerId, IPacket packet)
    {
        var msgSeq = NextMsgSeq();
        var (replyObject, task) = ReplyObject.CreateAsync(msgSeq);
        var header = CreateApiHeader(packet.MsgId, msgSeq);
        SendRequest(apiServerId, header, packet.Payload, replyObject);

        return await task;
    }

    #endregion

    #region API Service Communication

    /// <inheritdoc/>
    public void SendToApiService(ushort serviceId, IPacket packet)
    {
        SendToApiService(serviceId, packet, ServerSelectionPolicy.RoundRobin);
    }

    /// <inheritdoc/>
    public void SendToApiService(ushort serviceId, IPacket packet, ServerSelectionPolicy policy)
    {
        var server = ResolveServiceServer(ServerType.Api, serviceId, policy);
        SendToApi(server.ServerId, packet);
    }

    /// <inheritdoc/>
    public void RequestToApiService(ushort serviceId, IPacket packet, ReplyCallback replyCallback)
    {
        RequestToApiService(serviceId, packet, replyCallback, ServerSelectionPolicy.RoundRobin);
    }

    /// <inheritdoc/>
    public void RequestToApiService(ushort serviceId, IPacket packet, ReplyCallback replyCallback, ServerSelectionPolicy policy)
    {
        var server = ResolveServiceServer(ServerType.Api, serviceId, policy);
        RequestToApi(server.ServerId, packet, replyCallback);
    }

    /// <inheritdoc/>
    public Task<IPacket> RequestToApiService(ushort serviceId, IPacket packet)
    {
        return RequestToApiService(serviceId, packet, ServerSelectionPolicy.RoundRobin);
    }

    /// <inheritdoc/>
    public async Task<IPacket> RequestToApiService(ushort serviceId, IPacket packet, ServerSelectionPolicy policy)
    {
        var server = ResolveServiceServer(ServerType.Api, serviceId, policy);
        return await RequestToApi(server.ServerId, packet);
    }

    #endregion

    #region Stage Communication

    /// <inheritdoc/>
    public void SendToStage(string playServerId, long stageId, IPacket packet)
    {
        var header = CreateStageHeader(packet.MsgId, msgSeq: 0, stageId);
        SendInternal(playServerId, header, packet.Payload);
    }

    /// <inheritdoc/>
    public void RequestToStage(string playServerId, long stageId, IPacket packet, ReplyCallback replyCallback)
    {
        var msgSeq = NextMsgSeq();
        var replyObject = ReplyObject.CreateCallback(msgSeq, replyCallback, GetPostToStageCallback());
        var header = CreateStageHeader(packet.MsgId, msgSeq, stageId);
        SendRequest(playServerId, header, packet.Payload, replyObject);
    }

    /// <inheritdoc/>
    public async Task<IPacket> RequestToStage(string playServerId, long stageId, IPacket packet)
    {
        var msgSeq = NextMsgSeq();
        var (replyObject, task) = ReplyObject.CreateAsync(msgSeq);
        var header = CreateStageHeader(packet.MsgId, msgSeq, stageId);
        SendRequest(playServerId, header, packet.Payload, replyObject);

        return await task;
    }

    #endregion

    #region System Communication

    /// <inheritdoc/>
    public void SendToSystem(string serverId, IPacket packet)
    {
        var header = CreateSystemHeader(packet.MsgId, msgSeq: 0);
        SendInternal(serverId, header, packet.Payload);
    }

    /// <inheritdoc/>
    public void RequestToSystem(string serverId, IPacket packet, ReplyCallback replyCallback)
    {
        var msgSeq = NextMsgSeq();
        var replyObject = ReplyObject.CreateCallback(msgSeq, replyCallback, GetPostToStageCallback());
        var header = CreateSystemHeader(packet.MsgId, msgSeq);
        SendRequest(serverId, header, packet.Payload, replyObject);
    }

    /// <inheritdoc/>
    public async Task<IPacket> RequestToSystem(string serverId, IPacket packet)
    {
        var msgSeq = NextMsgSeq();
        var (replyObject, task) = ReplyObject.CreateAsync(msgSeq);
        var header = CreateSystemHeader(packet.MsgId, msgSeq);
        SendRequest(serverId, header, packet.Payload, replyObject);

        return await task;
    }

    #endregion

    #region Reply

    /// <inheritdoc/>
    public void Reply(ushort errorCode)
    {
        if (CurrentHeader == null)
        {
            throw new InvalidOperationException("Cannot reply without a current request context");
        }

        if (CurrentHeader.MsgSeq == 0)
        {
            return; // Not a request, no reply needed
        }

        var replyHeader = CreateReplyHeader(CurrentHeader.MsgId, (ushort)CurrentHeader.MsgSeq, errorCode);
        SendInternal(CurrentHeader.From, replyHeader, EmptyPayload.Instance);
    }

    /// <inheritdoc/>
    public void Reply(IPacket reply)
    {
        if (CurrentHeader == null)
        {
            throw new InvalidOperationException("Cannot reply without a current request context");
        }

        if (CurrentHeader.MsgSeq == 0)
        {
            return; // Not a request, no reply needed
        }

        // For Stage-to-Stage communication: AccountId in request contains the sender's StageId
        // Reply should be routed back to that Stage
        var replyStageId = CurrentHeader.AccountId;
        var replyAccountId = CurrentHeader.AccountId;

        var replyHeader = CreateReplyHeader(reply.MsgId, (ushort)CurrentHeader.MsgSeq, 0, replyStageId, replyAccountId);
        SendInternal(CurrentHeader.From, replyHeader, reply.Payload);
    }

    #endregion

    #region Internal Methods

    private RouteHeader CreateHeader(string msgId, ushort msgSeq, long stageId = 0, long accountId = 0, long sid = 0)
    {
        return new RouteHeader
        {
            MsgSeq = msgSeq,
            ServiceId = ServiceId,
            MsgId = msgId,
            From = ServerId,
            StageId = stageId,
            AccountId = accountId,
            Sid = sid
        };
    }

    private RouteHeader CreateApiHeader(string msgId, ushort msgSeq)
    {
        var stageId = GetSenderStageId();
        var header = CurrentHeader;
        var accountId = header?.AccountId ?? 0;
        var sid = header?.Sid ?? 0;
        return CreateHeader(msgId, msgSeq, stageId, accountId, sid);
    }

    private RouteHeader CreateStageHeader(string msgId, ushort msgSeq, long stageId)
    {
        // Determine AccountId for reply routing:
        // - If CurrentHeader exists with AccountId (Actor context), preserve it
        // - Otherwise, use sender's StageId (Stage-to-Stage direct communication)
        var accountId = CurrentHeader?.AccountId ?? 0;
        if (accountId == 0)
        {
            accountId = GetSenderStageId();
        }

        // Stage-to-Stage communication: Sid should be 0 (not a client session)
        return CreateHeader(msgId, msgSeq, stageId, accountId, sid: 0);
    }

    private RouteHeader CreateSystemHeader(string msgId, ushort msgSeq)
    {
        var header = CreateApiHeader(msgId, msgSeq);
        header.IsSystem = true;
        return header;
    }

    private RouteHeader CreateReplyHeader(string msgId, ushort msgSeq, ushort errorCode, long stageId = 0, long accountId = 0)
    {
        return new RouteHeader
        {
            MsgSeq = msgSeq,
            ServiceId = ServiceId,
            MsgId = msgId,
            From = ServerId,
            ErrorCode = errorCode,
            IsReply = true,
            StageId = stageId,
            AccountId = accountId
        };
    }

    private void SendRequest(string targetServerId, RouteHeader header, IPayload payload, ReplyObject replyObject)
    {
        RegisterReply(replyObject);
        SendInternal(targetServerId, header, payload);
    }

    private XServerInfo ResolveServiceServer(ServerType serverType, ushort serviceId, ServerSelectionPolicy policy)
    {
        return _serverInfoCenter.GetServerByService(serverType, serviceId, policy)
            ?? throw new InvalidOperationException($"No available server for {serverType}:{serviceId}");
    }

    private void SendInternal(string targetServerId, RouteHeader header, IPayload payload)
    {
        // Transfer payload ownership to the send pipeline to avoid use-after-dispose
        // when callers dispose their IPacket immediately after sending.
        var ownedPayload = payload.Move();
        // ZmqPlaySocket.Send() will dispose the packet after sending.
        var packet = RoutePacket.Create(header, ownedPayload, ownsPayload: true);
        _communicator.Send(targetServerId, packet);
    }

    private void RegisterReply(ReplyObject replyObject)
    {
        var tcs = new TaskCompletionSource<IPacket>();
        _requestCache.Register(replyObject.MsgSeq, tcs, TimeSpan.FromMilliseconds(_requestTimeoutMs));

        // Bridge ReplyObject to RequestCache
        tcs.Task.ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully)
            {
                replyObject.Complete(t.Result);
            }
            else
            {
                replyObject.CompleteWithError((ushort)ErrorCode.RequestTimeout);
            }
        }, TaskContinuationOptions.ExecuteSynchronously);
    }

    #endregion
}
