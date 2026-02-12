using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Api;
using PlayHouse.Abstractions.System;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Shared;
using PlayHouse.Runtime.ServerMesh;
using PlayHouse.Runtime.ServerMesh.Communicator;
using PlayHouse.Runtime.ServerMesh.Discovery;
using PlayHouse.Runtime.ServerMesh.Message;

namespace PlayHouse.Core.Api.Bootstrap;

/// <summary>
/// API Server 인스턴스.
/// Play Server와 ZMQ로 통신하며 Stateless 요청 처리를 담당합니다.
/// </summary>
public sealed class ApiServer : IApiServerControl, IAsyncDisposable, ICommunicateListener
{
    private readonly ApiServerOption _options;
    private readonly ILogger<ApiServer> _logger;

    private readonly PlayCommunicator _communicator;
    private readonly ApiDispatcher _dispatcher;
    private readonly RequestCache _requestCache;
    private readonly ServerAddressResolver _addressResolver;
    private readonly SystemDispatcher _systemDispatcher;

    private bool _isRunning;
    private bool _disposed;

    /// <inheritdoc/>
    public int DiagnosticLevel
    {
        get => _communicator.DiagnosticLevel;
        set => _communicator.DiagnosticLevel = value;
    }

    /// <summary>
    /// API Link 인터페이스.
    /// DI에 등록하여 Play Server에 요청을 보낼 때 사용합니다.
    /// </summary>
    public IApiLink ApiLink { get; }

    internal ApiServer(
        ApiServerOption options,
        ISystemController systemController,
        IServiceProvider serviceProvider,
        ILoggerFactory loggerFactory)
    {
        _options = options;
        _logger = loggerFactory.CreateLogger<ApiServer>();

        var serverConfig = new ServerConfig(
            options.ServerType,
            options.ServiceId,
            options.ServerId,
            options.BindEndpoint,
            options.RequestTimeoutMs,
            options.SendHighWatermark,
            options.ReceiveHighWatermark);

        // 모든 필드를 생성자에서 초기화
        _requestCache = new RequestCache(loggerFactory.CreateLogger<RequestCache>());

        _communicator = new PlayCommunicator(serverConfig);
        _communicator.Bind(this);

        // ServerInfoCenter 생성 (ApiDispatcher와 ApiLink에서 사용)
        IServerInfoCenter serverInfoCenter = new XServerInfoCenter();
        var myServerInfo = new XServerInfo(
            _options.ServerType,
            _options.ServiceId,
            _options.ServerId,
            _options.BindEndpoint);

        // ApiDispatcher 생성 (외부 ServiceProvider 사용)
        _dispatcher = new ApiDispatcher(
            _options.ServerType,
            _options.ServiceId,
            _options.ServerId,
            _requestCache,
            _communicator,
            serverInfoCenter,
            serviceProvider,
            loggerFactory);

        // ApiLink 생성
        ApiLink = new ApiLink(
            _communicator,
            _requestCache,
            serverInfoCenter,
            _options.ServerType,
            _options.ServiceId,
            _options.ServerId);

        // ServerAddressResolver 생성
        _addressResolver = new ServerAddressResolver(
            myServerInfo,
            systemController,
            serverInfoCenter,
            _communicator,
            TimeSpan.FromSeconds(3));

        // SystemDispatcher 생성 및 핸들러 등록
        _systemDispatcher = new SystemDispatcher(loggerFactory.CreateLogger<SystemDispatcher>());
        systemController.Handles(_systemDispatcher);
    }

    /// <summary>
    /// 서버를 시작합니다.
    /// </summary>
    public async Task StartAsync()
    {
        if (_isRunning)
            throw new InvalidOperationException("Server is already running");

        _communicator.Start();

        // 자기 자신에게 연결 (자기 자신에게 메시지를 보내기 위해 필요)
        _communicator.Connect(_options.ServerId, _options.BindEndpoint);

        // ServerAddressResolver 시작
        _addressResolver.Start();

        _isRunning = true;

        await Task.Delay(50); // 서버 초기화 대기
    }

    /// <summary>
    /// 서버를 중지합니다.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning) return;

        _isRunning = false;

        // 1. 새 요청 수신 중지
        _communicator.Stop();

        // 2. 진행 중인 요청 완료 대기 (타임아웃 적용)
        try
        {
            await _dispatcher.DrainAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 타임아웃 - 강제 종료
            _logger.LogWarning("Graceful shutdown timed out, forcing stop");
        }

        // 3. 리소스 정리
        _addressResolver.Stop();
        _addressResolver.Dispose();
        _dispatcher.Dispose();
        _requestCache.CancelAll();
    }

    /// <inheritdoc/>
    public void OnReceive(RoutePacket packet)
    {
        // 응답 패킷인 경우 RequestCache에서 처리
        // IsReply 플래그로 응답/요청을 구분 (MsgSeq만으로는 구분 불가)
        if (packet.Header.IsReply && packet.MsgSeq > 0)
        {
            // Zero-copy ownership transfer: move payload to response packet, then dispose RoutePacket.
            var response = CPacket.Of(packet.MsgId, packet.Payload.Move());
            if (_requestCache.TryComplete(packet.MsgSeq, response))
            {
                packet.Dispose();
                return;
            }

            // No pending request for this reply. Drop it to avoid dispatch loops.
            response.Dispose();
            _logger.LogWarning(
                "Dropping unmatched reply packet: MsgId={MsgId}, MsgSeq={MsgSeq}, From={From}",
                packet.MsgId,
                packet.MsgSeq,
                packet.From);
            packet.Dispose();
            return;
        }

        // 시스템 메시지 라우팅
        if (packet.Header.IsSystem)
        {
            _ = Task.Run(async () =>
            {
                try { await _systemDispatcher.DispatchAsync(packet, ApiLink); }
                finally { packet.Dispose(); }
            });
            return;
        }

        // API Handler로 라우팅
        _dispatcher.Post(packet);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await StopAsync();
        _communicator.Dispose();
    }
}
