using System.Buffers;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Abstractions.System;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Play.Base;
using PlayHouse.Core.Shared;
using PlayHouse.Infrastructure.Memory;
using PlayHouse.Runtime.ClientTransport;
using PlayHouse.Runtime.ClientTransport.Tcp;
using PlayHouse.Runtime.ClientTransport.WebSocket;
using PlayHouse.Runtime.ServerMesh;
using PlayHouse.Runtime.ServerMesh.Communicator;
using PlayHouse.Runtime.ServerMesh.Discovery;
using PlayHouse.Runtime.ServerMesh.Message;

namespace PlayHouse.Core.Play.Bootstrap;

/// <summary>
/// Play Server 인스턴스.
/// Stage와 Actor를 관리하고 클라이언트와 실시간 통신을 담당합니다.
/// TCP, WebSocket, SSL/TLS를 지원하며 동시에 여러 Transport를 사용할 수 있습니다.
/// </summary>
public sealed class PlayServer : IPlayServerControl, IAsyncDisposable, ICommunicateListener, IClientReplyHandler
{
    private readonly PlayServerOption _options;
    private readonly PlayProducer _producer;
    private readonly ISystemController _systemController;
    private readonly ServerConfig _serverConfig;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<PlayServer> _logger;

    private PlayCommunicator? _communicator;
    private PlayDispatcher? _dispatcher;
    private RequestCache? _requestCache;
    private ITransportServer? _transportServer;
    private ServerAddressResolver? _addressResolver;
    private SystemDispatcher? _systemDispatcher;
    private XSystemLink? _systemLink;
    private CancellationTokenSource? _cts;

    private bool _isRunning;
    private bool _disposed;

    /// <summary>
    /// 실제 바인딩된 TCP 포트.
    /// TcpPort가 0인 경우 (자동 할당) 서버 시작 후 실제 포트를 반환합니다.
    /// </summary>
    public int ActualTcpPort => GetActualTcpPort();

    internal PlayServer(
        PlayServerOption options,
        PlayProducer producer,
        ISystemController systemController,
        ILoggerFactory loggerFactory)
    {
        _options = options;
        _producer = producer;
        _systemController = systemController;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<PlayServer>();

        _serverConfig = new ServerConfig(
            options.ServerType,
            options.ServiceId,
            options.ServerId,
            options.BindEndpoint,
            options.RequestTimeoutMs);
    }

    /// <summary>
    /// 서버를 시작합니다.
    /// </summary>
    public async Task StartAsync()
    {
        if (_isRunning)
            throw new InvalidOperationException("Server is already running");

        // [개선] 메모리 풀 설정 적용
        MessagePool.ApplyConfig(_options.MessagePool);

        _cts = new CancellationTokenSource();
        _requestCache = new RequestCache(_loggerFactory.CreateLogger<RequestCache>());

        _communicator = new PlayCommunicator(_serverConfig);
        _communicator.Bind(this);
        _communicator.Start();

        // 자기 자신에게 연결 (같은 서버 내 Stage 간 통신에 필요)
        _communicator.Connect(_options.ServerId, _options.BindEndpoint);

        // ServerInfoCenter 생성 (PlayDispatcher에서 사용)
        IServerInfoCenter serverInfoCenter = new XServerInfoCenter();

        // PlayDispatcher 생성 (ThreadPool 기반 + ComputePool/IoPool 사용)
        _dispatcher = new PlayDispatcher(
            _producer,
            _communicator,
            _requestCache,
            serverInfoCenter,
            _options.ServerType,
            _options.ServiceId,
            _options.ServerId,
            this, // client reply handler
            _loggerFactory);

        // Transport 서버 빌드 및 시작
        _transportServer = BuildTransportServer();
        await _transportServer.StartAsync(_cts.Token);

        // ServerAddressResolver 시작
        var myServerInfo = new XServerInfo(
            _options.ServerType,
            _options.ServiceId,
            _options.ServerId,
            _options.BindEndpoint,
            ServerState.Running);

        _addressResolver = new ServerAddressResolver(
            myServerInfo,
            _systemController,
            serverInfoCenter,
            _communicator,
            TimeSpan.FromSeconds(3));

        _addressResolver.Start();

        // SystemDispatcher 생성 및 핸들러 등록
        _systemDispatcher = new SystemDispatcher(_loggerFactory.CreateLogger<SystemDispatcher>());
        _systemController.Handles(_systemDispatcher);

        // System message handler용 Link 생성
        _systemLink = new XSystemLink(
            _communicator,
            _requestCache,
            serverInfoCenter,
            _options.ServerType,
            _options.ServiceId,
            _options.ServerId,
            _options.RequestTimeoutMs);

        _isRunning = true;
        _logger.LogInformation("PlayServer started: ServerId={ServerId}, TCP={TcpEnabled}, WebSocket={WsEnabled}",
            _options.ServerId, _options.IsTcpEnabled, _options.IsWebSocketEnabled);

        await Task.Delay(50); // 서버 초기화 대기
    }

    /// <summary>
    /// Transport 서버를 옵션에 따라 빌드합니다.
    /// </summary>
    private ITransportServer BuildTransportServer()
    {
        var builder = new TransportServerBuilder(
            OnClientMessage,
            OnClientDisconnect,
            _logger);

        builder.WithOptions(opts =>
        {
            opts.ReceiveBufferSize = _options.TransportOptions.ReceiveBufferSize;
            opts.SendBufferSize = _options.TransportOptions.SendBufferSize;
            opts.MaxPacketSize = _options.TransportOptions.MaxPacketSize;
            opts.HeartbeatTimeout = _options.TransportOptions.HeartbeatTimeout;
        });

        // TCP (plain) 추가
        if (_options.IsTcpEnabled)
        {
            var tcpPort = _options.TcpPort!.Value;
            builder.AddTcp(tcpPort, _options.TcpBindAddress);
            _logger.LogInformation("TCP enabled on port {Port}", tcpPort == 0 ? "auto" : tcpPort);
        }

        // TCP+TLS 추가
        if (_options.IsTcpTlsEnabled)
        {
            var tcpTlsPort = _options.TcpTlsPort!.Value;
            builder.AddTcpWithTls(tcpTlsPort, _options.TcpTlsCertificate!, _options.TcpBindAddress);
            _logger.LogInformation("TCP+TLS enabled on port {Port}", tcpTlsPort == 0 ? "auto" : tcpTlsPort);
        }

        // WebSocket 추가
        if (_options.IsWebSocketEnabled)
        {
            builder.AddWebSocket(_options.WebSocketPath!);
            _logger.LogInformation("WebSocket enabled on path {Path}", _options.WebSocketPath);

            if (_options.IsWebSocketTlsEnabled)
            {
                builder.AddWebSocketWithTls(_options.WebSocketPath!, _options.WebSocketTlsCertificate!);
                _logger.LogInformation("WebSocket+TLS enabled on path {Path}", _options.WebSocketPath);
            }
        }

        return builder.Build();
    }

    /// <summary>
    /// 서버를 중지합니다.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning) return;

        _isRunning = false;
        _cts?.Cancel();

        _addressResolver?.Stop();
        _addressResolver?.Dispose();

        if (_transportServer != null)
        {
            await _transportServer.StopAsync();
        }

        _communicator?.Stop();
        _dispatcher?.Dispose();
        _requestCache?.CancelAll();

        _cts?.Dispose();
        _cts = null;

        _logger.LogInformation("PlayServer stopped: ServerId={ServerId}", _options.ServerId);
    }

    /// <inheritdoc/>
    public void OnReceive(RoutePacket packet)
    {
        // 응답 패킷인 경우 RequestCache에서 처리
        // IsReply 플래그로 응답/요청을 구분 (MsgSeq만으로는 구분 불가)
        if (packet.Header.IsReply && packet.MsgSeq > 0)
        {
            // Zero-copy: Transfer payload ownership from RoutePacket to CPacket
            var response = CPacket.Of(packet.MsgId, packet.Payload);
            if (_requestCache?.TryComplete(packet.MsgSeq, response) == true)
            {
                // CPacket now owns the payload - don't dispose RoutePacket
                // RoutePacket will be GC'd, CPacket.Dispose() will free the payload
                return;
            }
            // TryComplete failed - RoutePacket still owns payload, will go to dispatcher
        }

        // 시스템 메시지 라우팅
        if (packet.Header.IsSystem && _systemDispatcher != null && _systemLink != null)
        {
            _ = Task.Run(async () =>
            {
                try { await _systemDispatcher.DispatchAsync(packet, _systemLink); }
                finally { packet.Dispose(); }
            });
            return;
        }

        // Stage로 라우팅
        _dispatcher?.OnPost(new RouteMessage(packet));
    }

    /// <summary>
    /// Transport에서 클라이언트 메시지 수신 시 호출됩니다.
    /// Fire-and-forget pattern for high throughput.
    /// </summary>
    private void OnClientMessage(
        ITransportSession session,
        string msgId,
        ushort msgSeq,
        long stageId,
        IPayload payload)
    {
        // 미인증 클라이언트 체크
        if (!session.IsAuthenticated)
        {
            // 인증 메시지가 아니면 연결 끊기
            if (msgId != _options.AuthenticateMessageId)
            {
                _logger.LogWarning("Unauthenticated session {SessionId} sent non-auth message: {MsgId}",
                    session.SessionId, msgId);
                payload.Dispose();
                _ = session.DisconnectAsync(); // Fire-and-forget
                return;
            }
        }

        // 기본 핸들러: 인증 및 메시지 처리
        HandleDefaultMessage(session, msgId, msgSeq, stageId, payload);
    }

    /// <summary>
    /// Handles default message processing for client messages.
    /// </summary>
    /// <remarks>
    /// Fire-and-forget pattern for high throughput.
    /// Authentication is handled asynchronously in background.
    /// </remarks>
    private void HandleDefaultMessage(
        ITransportSession session,
        string msgId,
        ushort msgSeq,
        long stageId,
        IPayload payload)
    {
        // 인증 요청 처리 (async - fire-and-forget)
        if (msgId == _options.AuthenticateMessageId)
        {
            _ = HandleAuthenticationAsync(session, msgSeq, stageId, payload); // Fire-and-forget
            return;
        }

        // HeartBeat 요청 처리 (sync path)
        if (msgId == "@Heart@Beat@")
        {
            // Use new zero-copy API (synchronous - queued internally)
            session.SendResponse(msgId, msgSeq, stageId, 0, ReadOnlySpan<byte>.Empty);
            payload.Dispose();
            return;
        }

        // 인증된 세션의 일반 메시지 처리 (sync path - no allocation)
        if (session.IsAuthenticated && !string.IsNullOrEmpty(session.AccountId))
        {
            // Direct routing without ClientRouteMessage allocation (perf: avoid heap alloc per message)
            _dispatcher?.RouteClientMessage(session, stageId, session.AccountId, msgId, msgSeq, session.SessionId, payload);
        }
        else
        {
            _logger.LogWarning("Unauthenticated session {SessionId} tried to send message: {MsgId}",
                session.SessionId, msgId);
            payload.Dispose();
        }
    }

    private async Task HandleAuthenticationAsync(
        ITransportSession session,
        ushort msgSeq,
        long stageId,
        IPayload payload)
    {
        try
        {
            _ = stageId;
            if (_dispatcher == null || _systemLink == null)
            {
                await SendAuthReplyAsync(session, msgSeq, 0, (ushort)ErrorCode.InternalError);
                payload.Dispose();
                return;
            }

            if (string.IsNullOrEmpty(_options.DefaultStageType))
            {
                _logger.LogError("DefaultStageType is required when authenticating direct client sessions.");
                await SendAuthReplyAsync(session, msgSeq, 0, (ushort)ErrorCode.InvalidStageType);
                payload.Dispose();
                return;
            }

            var actorFactoryStageType = _options.DefaultStageType;

            // 별도 Task에서 Actor 콜백 호출
            var (success, errorCode, targetStageId, actor, authReplyPacket) = await Task.Run(async () =>
            {
                try
                {
                    // 인증 단계에서는 Stage 미바인딩 상태로 Actor를 생성하고,
                    // OnAuthenticate에서 전달된 StageId로 실제 Stage를 바인딩한다.
                    var actorLink = new XActorLink(_options.ServerId, session.SessionId, _options.ServerId, _systemLink, session);

                    // IActor 생성 with DI scope
                    BaseActor actor;
                    IServiceScope? actorScope;
                    try
                    {
                        (IActor iActor, actorScope) = _producer.GetActorWithScope(actorFactoryStageType, actorLink);
                        actor = new BaseActor(iActor, actorLink, actorScope);
                    }
                    catch (KeyNotFoundException)
                    {
                        _logger.LogError("Actor factory not found for stage type: {StageType}", actorFactoryStageType);
                        return (false, (ushort)ErrorCode.InvalidStageType, 0L, (BaseActor?)null, (IPacket?)null);
                    }

                    // Actor 콜백 순차 호출
                    await actor.Actor.OnCreate();

                    // Zero-copy: Use payload directly without ToArray() copy
                    var authPacket = CPacket.Of(_options.AuthenticateMessageId, payload);
                    var (authResult, authReply) = await actor.Actor.OnAuthenticate(authPacket);

                    if (!authResult)
                    {
                        _logger.LogWarning("Authentication rejected for session {SessionId}", session.SessionId);
                        await actor.Actor.OnDestroy();
                        actor.Dispose();  // Dispose Actor's DI scope
                        authReply?.Dispose();
                        return (false, (ushort)ErrorCode.AuthenticationFailed, 0L, (BaseActor?)null, (IPacket?)null);
                    }

                    // AccountId 검증
                    if (string.IsNullOrEmpty(actorLink.AccountId))
                    {
                        _logger.LogError("AccountId not set after authentication for session {SessionId}", session.SessionId);
                        await actor.Actor.OnDestroy();
                        actor.Dispose();  // Dispose Actor's DI scope
                        authReply?.Dispose();
                        return (false, (ushort)ErrorCode.InvalidAccountId, 0L, (BaseActor?)null, (IPacket?)null);
                    }

                    if (actorLink.StageId <= 0)
                    {
                        _logger.LogWarning("StageId not set after authentication for session {SessionId}", session.SessionId);
                        await actor.Actor.OnDestroy();
                        actor.Dispose();
                        authReply?.Dispose();
                        return (false, (ushort)ErrorCode.StageNotFound, 0L, (BaseActor?)null, (IPacket?)null);
                    }

                    var resolvedStageId = actorLink.StageId;
                    var stageType = actorFactoryStageType;
                    var baseStage = _dispatcher.GetOrCreateStage(resolvedStageId, stageType);

                    if (baseStage == null)
                    {
                        _logger.LogError("Failed to get or create stage {StageId} after auth", resolvedStageId);
                        await actor.Actor.OnDestroy();
                        actor.Dispose();
                        authReply?.Dispose();
                        return (false, (ushort)ErrorCode.StageCreationFailed, resolvedStageId, (BaseActor?)null, (IPacket?)null);
                    }

                    if (!baseStage.IsCreated)
                    {
                        var createPacket = CPacket.Empty("CreateStage");
                        var (createSuccess, _) = await baseStage.CreateStage(stageType, createPacket);
                        if (!createSuccess)
                        {
                            _logger.LogError("Failed to initialize stage {StageId}", resolvedStageId);
                            await actor.Actor.OnDestroy();
                            actor.Dispose();
                            authReply?.Dispose();
                            return (false, (ushort)ErrorCode.StageCreationFailed, resolvedStageId, (BaseActor?)null, (IPacket?)null);
                        }
                    }

                    actorLink.BindStage(baseStage);

                    // 세션에 인증 정보 설정
                    session.AccountId = actorLink.AccountId;
                    session.IsAuthenticated = true;
                    session.StageId = resolvedStageId;
                    await actor.Actor.OnPostAuthenticate();

                    return (true, (ushort)ErrorCode.Success, resolvedStageId, actor, authReply);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during actor authentication callbacks");
                    return (false, (ushort)ErrorCode.InternalError, 0L, (BaseActor?)null, (IPacket?)null);
                }
            });

            if (!success || actor == null)
            {
                await SendAuthReplyAsync(session, msgSeq, targetStageId, errorCode);
                payload.Dispose();
                authReplyPacket?.Dispose();
                return;
            }

            // Stage Queue에 JoinActorMessage 전달 (즉시 return, Stage에서 응답 send)
            var authReplyMsgId = _options.AuthenticateMessageId.Replace("Request", "Reply");
            _dispatcher?.OnPost(new JoinActorMessage(targetStageId, actor, session, msgSeq, authReplyMsgId, payload, authReplyPacket));
            // payload와 authReplyPacket은 JoinActorMessage에서 Dispose됨
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication failed for session {SessionId}", session.SessionId);
            await SendAuthReplyAsync(session, msgSeq, 0, (ushort)ErrorCode.InternalError);
            payload.Dispose();
        }
    }

    private Task SendAuthReplyAsync(
        ITransportSession session,
        ushort msgSeq,
        long stageId,
        ushort errorCode,
        string? accountId = null)
    {
        var reply = new Proto.AuthenticateReply
        {
            Authenticated = errorCode == (ushort)ErrorCode.Success,
            StageId = (int)stageId,
            AccountId = long.TryParse(accountId, out var id) ? id : 0,
            ErrorMessage = errorCode == (ushort)ErrorCode.Success ? "" : $"Error code: {errorCode}"
        };

        // Zero-copy: use ArrayPool instead of ToByteArray()
        var size = reply.CalculateSize();
        var buffer = ArrayPool<byte>.Shared.Rent(size);
        try
        {
            reply.WriteTo(buffer.AsSpan(0, size));

            // Synchronous send - queued internally
            session.SendResponse(
                _options.AuthenticateMessageId.Replace("Request", "Reply"),
                msgSeq,
                stageId,
                errorCode,
                buffer.AsSpan(0, size));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return Task.CompletedTask;
    }

    private long GenerateStageId()
    {
        // Simple stage ID generation (timestamp-based)
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }


    /// <summary>
    /// Transport에서 클라이언트 연결 해제 시 호출됩니다.
    /// </summary>
    private void OnClientDisconnect(ITransportSession session, Exception? ex)
    {
        if (ex != null)
        {
            _logger.LogWarning(
                ex,
                "Session {SessionId} disconnected with error (Connected={IsConnected}, Auth={IsAuthenticated}, StageId={StageId}, AccountId={AccountId})",
                session.SessionId,
                session.IsConnected,
                session.IsAuthenticated,
                session.StageId,
                session.AccountId);
        }
        else
        {
            _logger.LogInformation(
                "Session {SessionId} disconnected (Connected={IsConnected}, Auth={IsAuthenticated}, StageId={StageId}, AccountId={AccountId})",
                session.SessionId,
                session.IsConnected,
                session.IsAuthenticated,
                session.StageId,
                session.AccountId);
        }

        // Clear routing context
        session.ProcessorContext = null;

        // 인증된 세션인 경우 DisconnectMessage 전달
        if (session.IsAuthenticated && !string.IsNullOrEmpty(session.AccountId) && session.StageId != 0)
        {
            _dispatcher?.OnPost(new DisconnectMessage(session.StageId, session.AccountId));
        }
    }

    /// <summary>
    /// 특정 세션에 Push 메시지를 전송합니다.
    /// </summary>
    public ValueTask SendPushAsync(long sessionId, string msgId, long stageId, ReadOnlyMemory<byte> payload)
    {
        var session = _transportServer?.GetSession(sessionId);
        if (session?.IsConnected == true)
        {
            // Synchronous send - queued internally
            session.SendResponse(msgId, 0, stageId, 0, payload.Span);
        }
        return ValueTask.CompletedTask;
    }


    /// <summary>
    /// 특정 세션을 연결 해제합니다.
    /// </summary>
    public async ValueTask DisconnectSessionAsync(long sessionId)
    {
        if (_transportServer != null)
        {
            await _transportServer.DisconnectSessionAsync(sessionId);
        }
    }

    /// <summary>
    /// 특정 세션을 조회합니다.
    /// </summary>
    public ITransportSession? GetSession(long sessionId)
    {
        return _transportServer?.GetSession(sessionId);
    }

    /// <summary>
    /// 외부에서 Stage를 미리 생성할 수 있도록 API 제공.
    /// Stage가 이미 존재하는 경우에도 성공을 반환합니다.
    /// </summary>
    /// <param name="stageId">생성할 Stage의 ID</param>
    /// <param name="stageType">생성할 Stage의 타입</param>
    /// <returns>Stage가 생성되었거나 이미 존재하면 true, 실패하면 false</returns>
    public bool CreateStageIfNotExists(long stageId, string stageType)
    {
        if (_dispatcher == null) return false;
        var stage = _dispatcher.GetOrCreateStage(stageId, stageType);
        return stage != null;
    }

    /// <summary>
    /// Stage에 메시지를 직접 전송합니다 (Actor 컨텍스트 없이).
    /// Stage의 OnDispatch(IPacket packet) 메서드에서 처리됩니다.
    /// </summary>
    /// <param name="stageId">대상 Stage ID</param>
    /// <param name="msgId">메시지 ID</param>
    /// <param name="payload">페이로드 (null이면 빈 페이로드)</param>
    public void SendToStage(long stageId, string msgId, byte[]? payload = null)
    {
        if (_dispatcher == null) return;

        var header = new Runtime.Proto.RouteHeader
        {
            StageId = stageId,
            MsgId = msgId,
            ServiceId = _options.ServiceId
        };

        var routePacket = payload != null
            ? RoutePacket.Of(header, payload)
            : RoutePacket.Empty(header);

        _dispatcher.OnPost(new RouteMessage(routePacket));
    }

    /// <summary>
    /// 연결된 클라이언트 수를 반환합니다.
    /// </summary>
    public int ConnectedClientCount => _transportServer?.SessionCount ?? 0;

    /// <summary>
    /// 실제 바인딩된 TCP 포트를 가져옵니다.
    /// TCP가 비활성화된 경우 0을 반환합니다.
    /// </summary>
    private int GetActualTcpPort()
    {
        if (_transportServer is CompositeTransportServer composite)
        {
            var tcpServer = composite.TcpServers.FirstOrDefault();
            return tcpServer?.ActualPort ?? _options.TcpPort ?? 0;
        }

        if (_transportServer is TcpTransportServer tcp)
        {
            return tcp.ActualPort;
        }

        return _options.TcpPort ?? 0;
    }

    /// <summary>
    /// WebSocket 서버 목록을 반환합니다 (ASP.NET Core 미들웨어 등록용).
    /// </summary>
    public IEnumerable<WebSocketTransportServer> GetWebSocketServers()
    {
        if (_transportServer is CompositeTransportServer composite)
        {
            return composite.WebSocketServers;
        }

        if (_transportServer is WebSocketTransportServer wsServer)
        {
            return new[] { wsServer };
        }

        return Enumerable.Empty<WebSocketTransportServer>();
    }

    /// <inheritdoc/>
    public ValueTask SendClientReplyAsync(long sessionId, string msgId, ushort msgSeq, long stageId, ushort errorCode, Abstractions.IPayload payload)
    {
        var session = _transportServer?.GetSession(sessionId);
        if (session?.IsConnected == true)
        {
            // Synchronous send - queued internally (Channel + SendLoop)
            session.SendResponse(msgId, msgSeq, stageId, errorCode, payload.DataSpan);
        }
        else
        {
            _logger.LogWarning("Cannot send client reply: session {SessionId} not found or disconnected", sessionId);
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await StopAsync();

        if (_transportServer != null)
        {
            await _transportServer.DisposeAsync();
        }

        _communicator?.Dispose();
    }
}
