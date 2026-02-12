#nullable enable

using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Play.Base;
using PlayHouse.Runtime.ClientTransport;
using PlayHouse.Runtime.ServerMesh.Discovery;

namespace PlayHouse.Core.Play;

/// <summary>
/// Internal implementation of IActorLink.
/// </summary>
/// <remarks>
/// XActorLink delegates most operations to its parent BaseStage's StageLink.
/// It maintains Actor-specific session information for client communication.
/// </remarks>
internal sealed class XActorLink : IActorLink
{
    private BaseStage? _baseStage;
    private readonly ILink _fallbackLink;
    private ITransportSession? _transportSession;

    private string _sessionServerId;
    private long _sid;
    private string _apiServerId;

    private XStageLink StageLink => _baseStage?.StageLink
        ?? throw new InvalidOperationException("Stage is not bound for this actor link.");
    private ILink ActiveLink => _baseStage?.StageLink ?? _fallbackLink;

    /// <inheritdoc/>
    public string AccountId { get; set; } = string.Empty;

    /// <inheritdoc/>
    public long StageId { get; set; }

    internal string AuthStageType { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public ServerType ServerType => ActiveLink.ServerType;

    /// <inheritdoc/>
    public ushort ServiceId => ActiveLink.ServiceId;

    /// <summary>
    /// Initializes a new instance of the <see cref="XActorLink"/> class.
    /// </summary>
    /// <param name="sessionNid">Session server NID.</param>
    /// <param name="sid">Session ID.</param>
    /// <param name="apiNid">API server NID.</param>
    /// <param name="baseStage">Parent BaseStage.</param>
    /// <param name="transportSession">Optional transport session for direct client communication.</param>
    public XActorLink(
        string sessionNid,
        long sid,
        string apiNid,
        BaseStage baseStage,
        ITransportSession? transportSession = null)
    {
        _sessionServerId = sessionNid;
        _sid = sid;
        _apiServerId = apiNid;
        _baseStage = baseStage;
        _fallbackLink = baseStage.StageLink;
        _transportSession = transportSession;
    }

    /// <summary>
    /// Initializes a new actor link without a bound Stage.
    /// BindStage must be called before Stage-dependent operations.
    /// </summary>
    public XActorLink(
        string sessionNid,
        long sid,
        string apiNid,
        ILink fallbackLink,
        ITransportSession? transportSession = null)
    {
        _sessionServerId = sessionNid;
        _sid = sid;
        _apiServerId = apiNid;
        _fallbackLink = fallbackLink;
        _transportSession = transportSession;
    }

    /// <summary>
    /// Gets the session server NID.
    /// </summary>
    internal string SessionNid => _sessionServerId;

    /// <summary>
    /// Gets the session ID.
    /// </summary>
    internal long Sid => _sid;

    /// <summary>
    /// Gets the API server NID.
    /// </summary>
    internal string ApiNid => _apiServerId;

    #region IActorLink Implementation

    /// <inheritdoc/>
    public async Task LeaveStageAsync()
    {
        if (_baseStage == null)
            throw new InvalidOperationException("Cannot leave stage before stage binding.");

        await _baseStage.LeaveStage(AccountId, _sessionServerId, _sid);
    }

    /// <inheritdoc/>
    public void SendToClient(IPacket packet)
    {
        // For directly connected clients, use transport session directly
        if (_transportSession != null)
        {
            if (_transportSession.IsConnected)
            {
                _transportSession.SendResponse(
                    packet.MsgId,
                    0,  // msgSeq = 0 for push messages
                    StageId,
                    0,  // errorCode = 0
                    packet.Payload.DataSpan);
                return;
            }
            // Session exists but disconnected - skip sending
            Console.WriteLine($"[XActorLink] SendToClient skipped: session {_transportSession.SessionId} disconnected for stage {_baseStage?.StageId ?? 0}");
            return;
        }

        // For server-to-server (API → PlayServer), use the routing mechanism
        if (_baseStage != null)
        {
            StageLink.SendToClient(_sessionServerId, _sid, packet);
        }
    }

    /// <inheritdoc/>
    public void SetAuthContext(string accountId, long stageId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
            throw new ArgumentException("AccountId must not be empty.", nameof(accountId));
        if (stageId <= 0)
            throw new ArgumentOutOfRangeException(nameof(stageId), "StageId must be greater than 0.");

        AccountId = accountId;
        StageId = stageId;
        AuthStageType = string.Empty;
    }

    /// <inheritdoc/>
    public void SetAuthSingleContext(string accountId, string stageType)
    {
        if (string.IsNullOrWhiteSpace(accountId))
            throw new ArgumentException("AccountId must not be empty.", nameof(accountId));
        if (string.IsNullOrWhiteSpace(stageType))
            throw new ArgumentException("StageType must not be empty.", nameof(stageType));

        AccountId = accountId;
        StageId = 0;
        AuthStageType = stageType;
    }

    #endregion

    #region ILink Delegation

    /// <inheritdoc/>
    public void SendToApi(string apiNid, IPacket packet)
    {
        ActiveLink.SendToApi(apiNid, packet);
    }

    /// <inheritdoc/>
    public void RequestToApi(string apiNid, IPacket packet, ReplyCallback replyCallback)
    {
        ActiveLink.RequestToApi(apiNid, packet, replyCallback);
    }

    /// <inheritdoc/>
    public async Task<IPacket> RequestToApi(string apiNid, IPacket packet)
    {
        return await ActiveLink.RequestToApi(apiNid, packet);
    }

    /// <inheritdoc/>
    public void SendToApiService(ushort serviceId, IPacket packet)
    {
        ActiveLink.SendToApiService(serviceId, packet);
    }

    /// <inheritdoc/>
    public void SendToApiService(ushort serviceId, IPacket packet, ServerSelectionPolicy policy)
    {
        ActiveLink.SendToApiService(serviceId, packet, policy);
    }

    /// <inheritdoc/>
    public void RequestToApiService(ushort serviceId, IPacket packet, ReplyCallback replyCallback)
    {
        ActiveLink.RequestToApiService(serviceId, packet, replyCallback);
    }

    /// <inheritdoc/>
    public void RequestToApiService(ushort serviceId, IPacket packet, ReplyCallback replyCallback, ServerSelectionPolicy policy)
    {
        ActiveLink.RequestToApiService(serviceId, packet, replyCallback, policy);
    }

    /// <inheritdoc/>
    public Task<IPacket> RequestToApiService(ushort serviceId, IPacket packet)
    {
        return ActiveLink.RequestToApiService(serviceId, packet);
    }

    /// <inheritdoc/>
    public Task<IPacket> RequestToApiService(ushort serviceId, IPacket packet, ServerSelectionPolicy policy)
    {
        return ActiveLink.RequestToApiService(serviceId, packet, policy);
    }

    /// <inheritdoc/>
    public void SendToStage(string playNid, long stageId, IPacket packet)
    {
        ActiveLink.SendToStage(playNid, stageId, packet);
    }

    /// <inheritdoc/>
    public void RequestToStage(string playNid, long stageId, IPacket packet, ReplyCallback replyCallback)
    {
        ActiveLink.RequestToStage(playNid, stageId, packet, replyCallback);
    }

    /// <inheritdoc/>
    public async Task<IPacket> RequestToStage(string playNid, long stageId, IPacket packet)
    {
        return await ActiveLink.RequestToStage(playNid, stageId, packet);
    }

    /// <inheritdoc/>
    public void SendToSystem(string serverId, IPacket packet)
        => ActiveLink.SendToSystem(serverId, packet);

    /// <inheritdoc/>
    public void RequestToSystem(string serverId, IPacket packet, ReplyCallback replyCallback)
        => ActiveLink.RequestToSystem(serverId, packet, replyCallback);

    /// <inheritdoc/>
    public Task<IPacket> RequestToSystem(string serverId, IPacket packet)
        => ActiveLink.RequestToSystem(serverId, packet);

    /// <inheritdoc/>
    public void Reply(ushort errorCode)
    {
        // For client requests, we don't use SendToClient for error codes
        // Just use the standard Reply mechanism
        ActiveLink.Reply(errorCode);
    }

    /// <inheritdoc/>
    public void Reply(IPacket reply)
    {
        if (_baseStage != null)
        {
            _baseStage.Reply(reply);
            return;
        }

        ActiveLink.Reply(reply);
    }

    #endregion

    #region Session Update

    /// <summary>
    /// Updates session information (used for reconnection).
    /// </summary>
    /// <param name="sessionNid">New session server NID.</param>
    /// <param name="sid">New session ID.</param>
    /// <param name="apiNid">New API server NID.</param>
    /// <param name="transportSession">Optional new transport session.</param>
    internal void Update(string sessionNid, long sid, string apiNid, ITransportSession? transportSession = null)
    {
        _sessionServerId = sessionNid;
        _sid = sid;
        _apiServerId = apiNid;
        _transportSession = transportSession;
    }

    /// <summary>
    /// Binds this actor link to a concrete stage after authentication.
    /// </summary>
    internal void BindStage(BaseStage baseStage)
    {
        _baseStage = baseStage;
        StageId = baseStage.StageId;
    }

    #endregion
}
