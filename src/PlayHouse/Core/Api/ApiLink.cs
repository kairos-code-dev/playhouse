using Google.Protobuf;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Api;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Shared;
using PlayHouse.Runtime.ServerMesh.Communicator;
using PlayHouse.Runtime.ServerMesh.Discovery;
using PlayHouse.Runtime.Proto;

namespace PlayHouse.Core.Api;

/// <summary>
/// Implementation of IApiLink for API server handlers.
/// </summary>
/// <remarks>
/// ApiLink extends XLink with API-specific functionality:
/// - Stage creation and management
/// - Authentication context management
/// </remarks>
internal class ApiLink : XLink, IApiLink
{
    private string _accountId = string.Empty;
    private string _stageId = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiLink"/> class.
    /// </summary>
    /// <param name="communicator">The communicator for sending messages.</param>
    /// <param name="requestCache">The request cache for tracking pending requests.</param>
    /// <param name="serverInfoCenter">Server information center for service discovery.</param>
    /// <param name="serverType">The server type of this API server.</param>
    /// <param name="serviceId">The service ID of this API server.</param>
    /// <param name="serverId">The ServerId of this API server.</param>
    public ApiLink(
        IClientCommunicator communicator,
        RequestCache requestCache,
        IServerInfoCenter serverInfoCenter,
        ServerType serverType,
        ushort serviceId,
        string serverId)
        : base(communicator, requestCache, serverInfoCenter, serverType, serviceId, serverId)
    {
    }

    /// <inheritdoc/>
    public string AccountId
    {
        get => _accountId;
        set => _accountId = value;
    }

    /// <inheritdoc/>
    public string StageId => _stageId;

    /// <inheritdoc/>
    public string FromNid => CurrentHeader?.From ?? string.Empty;

    /// <inheritdoc/>
    public bool IsRequest => CurrentHeader?.MsgSeq > 0;

    /// <summary>
    /// Sets the session context from the incoming packet header.
    /// </summary>
    /// <param name="header">The route header containing session information.</param>
    public void SetSessionContext(RouteHeader header)
    {
        SetCurrentHeader(header);
        _stageId = header.StageId;
        _accountId = header.AccountId.ToString();
    }

    /// <summary>
    /// Clears the session context.
    /// </summary>
    public void ClearSessionContext()
    {
        ClearCurrentHeader();
        _stageId = string.Empty;
        _accountId = string.Empty;
    }

    #region Stage Creation

    /// <inheritdoc/>
    public async Task<CreateStageResult> CreateStage(
        string playNid,
        string stageType,
        string stageId,
        IPacket packet)
    {
        var req = new CreateStageReq
        {
            StageType = stageType,
            PayloadId = packet.MsgId,
            Payload = ByteString.CopyFrom(packet.Payload.DataSpan)
        };

        var routePacket = CPacket.Of(req);
        var reply = await RequestToStage(playNid, stageId, routePacket);
        var res = CreateStageRes.Parser.ParseFrom(reply.Payload.DataSpan);

        // Zero-copy: use ByteString.Memory directly
        return new CreateStageResult(
            res.Result,
            CPacket.Of(res.PayloadId, new MemoryPayload(res.Payload.Memory)));
    }

    /// <inheritdoc/>
    public async Task<GetOrCreateStageResult> GetOrCreateStage(
        string playNid,
        string stageType,
        string stageId,
        IPacket createPacket)
    {
        var req = new GetOrCreateStageReq
        {
            StageType = stageType,
            CreatePayloadId = createPacket.MsgId,
            CreatePayload = ByteString.CopyFrom(createPacket.Payload.DataSpan)
        };

        var routePacket = CPacket.Of(req);
        var reply = await RequestToStage(playNid, stageId, routePacket);
        var res = GetOrCreateStageRes.Parser.ParseFrom(reply.Payload.DataSpan);

        // Result/IsCreated combinations:
        // - Result=true, IsCreated=false: Stage already existed
        // - Result=true, IsCreated=true: New stage created successfully
        // - Result=false, IsCreated=false: Creation failed
        // Zero-copy: use ByteString.Memory directly
        return new GetOrCreateStageResult(
            res.Result,
            res.IsCreated,
            CPacket.Of(res.PayloadId, new MemoryPayload(res.Payload.Memory)));
    }

    /// <inheritdoc/>
    public void CreateStage(
        string playNid,
        string stageType,
        string stageId,
        IPacket packet,
        CreateStageCallback callback)
    {
        var req = new CreateStageReq
        {
            StageType = stageType,
            PayloadId = packet.MsgId,
            Payload = ByteString.CopyFrom(packet.Payload.DataSpan)
        };

        var routePacket = CPacket.Of(req);
        RequestToStage(playNid, stageId, routePacket, (errorCode, reply) =>
        {
            if (errorCode != 0 || reply == null)
            {
                callback(errorCode, null);
                return;
            }

            var res = CreateStageRes.Parser.ParseFrom(reply.Payload.DataSpan);
            var result = new CreateStageResult(
                res.Result,
                CPacket.Of(res.PayloadId, new MemoryPayload(res.Payload.Memory)));
            callback(0, result);
        });
    }

    /// <inheritdoc/>
    public void GetOrCreateStage(
        string playNid,
        string stageType,
        string stageId,
        IPacket createPacket,
        GetOrCreateStageCallback callback)
    {
        var req = new GetOrCreateStageReq
        {
            StageType = stageType,
            CreatePayloadId = createPacket.MsgId,
            CreatePayload = ByteString.CopyFrom(createPacket.Payload.DataSpan)
        };

        var routePacket = CPacket.Of(req);
        RequestToStage(playNid, stageId, routePacket, (errorCode, reply) =>
        {
            if (errorCode != 0 || reply == null)
            {
                callback(errorCode, null);
                return;
            }

            var res = GetOrCreateStageRes.Parser.ParseFrom(reply.Payload.DataSpan);
            var result = new GetOrCreateStageResult(
                res.Result,
                res.IsCreated,
                CPacket.Of(res.PayloadId, new MemoryPayload(res.Payload.Memory)));
            callback(0, result);
        });
    }

    #endregion
}
