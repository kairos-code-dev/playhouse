#nullable enable

using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions.Play;
using PlayHouse.Abstractions.System;

namespace PlayHouse.Core.Play.Bootstrap;

/// <summary>
/// Play Server 부트스트랩 빌더.
/// </summary>
/// <remarks>
/// 사용 예시:
/// <code>
/// // TCP만 사용하는 경우
/// var playServer = new PlayServerBootstrap()
///     .Configure(options =>
///     {
///         options.ServerType = ServerType.Play;
///         options.ServerId = 1;
///         options.BindEndpoint = "tcp://0.0.0.0:5000";
///         options.TcpPort = 6000;
///         options.AuthenticateMessageId = "AuthRequest";
///     })
///     .UseStage&lt;GameRoomStage, PlayerActor&gt;("GameRoom")
///     .Build();
///
/// // TCP + WebSocket 동시 사용
/// var playServer = new PlayServerBootstrap()
///     .Configure(options =>
///     {
///         options.TcpPort = 6000;           // TCP 활성화
///         options.WebSocketPath = "/ws";    // WebSocket 활성화
///     })
///     .Build();
///
/// // TCP + TLS
/// var playServer = new PlayServerBootstrap()
///     .ConfigureTcpWithTls(6000, certificate)
///     .Build();
///
/// await playServer.StartAsync();
/// </code>
/// </remarks>
public sealed class PlayServerBootstrap
{
    private readonly PlayServerOption _options = new();
    private readonly Dictionary<string, Type> _stageTypes = new();
    private readonly Dictionary<string, Type> _actorTypes = new();
    private readonly Dictionary<string, StageMode> _stageModes = new();
    private Type _systemControllerType = null!;
    private ILoggerFactory _loggerFactory = null!;
    private IServiceProvider _serviceProvider = null!;

    /// <summary>
    /// 서버 옵션을 설정합니다.
    /// </summary>
    /// <param name="configure">설정 액션.</param>
    /// <returns>빌더 인스턴스.</returns>
    public PlayServerBootstrap Configure(Action<PlayServerOption> configure)
    {
        configure(_options);
        return this;
    }

    /// <summary>
    /// TCP 서버를 설정합니다.
    /// </summary>
    /// <param name="port">TCP 포트.</param>
    /// <param name="bindAddress">바인드 주소 (기본값: 모든 인터페이스).</param>
    /// <returns>빌더 인스턴스.</returns>
    public PlayServerBootstrap ConfigureTcp(int port, string? bindAddress = null)
    {
        _options.TcpPort = port;
        _options.TcpBindAddress = bindAddress;
        _options.TcpPortExplicitlySet = true;
        return this;
    }

    /// <summary>
    /// TCP + TLS 서버를 설정합니다.
    /// </summary>
    /// <param name="port">TCP 포트.</param>
    /// <param name="certificate">TLS 인증서.</param>
    /// <param name="bindAddress">바인드 주소 (기본값: 모든 인터페이스).</param>
    /// <returns>빌더 인스턴스.</returns>
    public PlayServerBootstrap ConfigureTcpWithTls(int port, X509Certificate2 certificate, string? bindAddress = null)
    {
        _options.TcpTlsPort = port;
        _options.TcpBindAddress = bindAddress;
        _options.TcpTlsCertificate = certificate;
        if (!_options.TcpPortExplicitlySet)
        {
            _options.TcpPort = null;
        }
        return this;
    }

    /// <summary>
    /// TCP + TLS 서버를 파일 경로로 설정합니다.
    /// </summary>
    /// <param name="port">TCP 포트.</param>
    /// <param name="certificatePath">인증서 파일 경로 (.pfx).</param>
    /// <param name="password">인증서 비밀번호 (없으면 null).</param>
    /// <param name="bindAddress">바인드 주소 (기본값: 모든 인터페이스).</param>
    /// <returns>빌더 인스턴스.</returns>
    public PlayServerBootstrap ConfigureTcpWithTls(int port, string certificatePath, string? password = null, string? bindAddress = null)
    {
        var cert = string.IsNullOrEmpty(password)
            ? new X509Certificate2(certificatePath)
            : new X509Certificate2(certificatePath, password);
        return ConfigureTcpWithTls(port, cert, bindAddress);
    }

    /// <summary>
    /// WebSocket 서버를 설정합니다.
    /// </summary>
    /// <param name="path">WebSocket 경로 (예: "/ws").</param>
    /// <returns>빌더 인스턴스.</returns>
    public PlayServerBootstrap ConfigureWebSocket(string path = "/ws")
    {
        _options.WebSocketPath = path;
        return this;
    }

    /// <summary>
    /// WebSocket + TLS 서버를 설정합니다.
    /// </summary>
    /// <param name="path">WebSocket 경로 (예: "/ws").</param>
    /// <param name="certificate">TLS 인증서.</param>
    /// <returns>빌더 인스턴스.</returns>
    public PlayServerBootstrap ConfigureWebSocketWithTls(string path, X509Certificate2 certificate)
    {
        _options.WebSocketPath = path;
        _options.WebSocketTlsCertificate = certificate;
        return this;
    }

    /// <summary>
    /// WebSocket + TLS 서버를 파일 경로로 설정합니다.
    /// </summary>
    /// <param name="path">WebSocket 경로 (예: "/ws").</param>
    /// <param name="certificatePath">인증서 파일 경로 (.pfx).</param>
    /// <param name="password">인증서 비밀번호 (없으면 null).</param>
    /// <returns>빌더 인스턴스.</returns>
    public PlayServerBootstrap ConfigureWebSocketWithTls(string path, string certificatePath, string? password = null)
    {
        var cert = string.IsNullOrEmpty(password)
            ? new X509Certificate2(certificatePath)
            : new X509Certificate2(certificatePath, password);
        return ConfigureWebSocketWithTls(path, cert);
    }

    /// <summary>
    /// LoggerFactory를 설정합니다.
    /// </summary>
    /// <param name="loggerFactory">ILoggerFactory 인스턴스.</param>
    /// <returns>빌더 인스턴스.</returns>
    public PlayServerBootstrap UseLoggerFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        return this;
    }

    /// <summary>
    /// ServiceProvider를 설정합니다.
    /// </summary>
    /// <param name="serviceProvider">IServiceProvider 인스턴스.</param>
    /// <returns>빌더 인스턴스.</returns>
    public PlayServerBootstrap UseServiceProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        return this;
    }

    /// <summary>
    /// Stage와 Actor 타입을 등록합니다.
    /// </summary>
    /// <typeparam name="TStage">IStage 구현 타입.</typeparam>
    /// <typeparam name="TActor">IActor 구현 타입.</typeparam>
    /// <param name="stageType">Stage 타입 이름.</param>
    /// <param name="mode">Stage 동작 모드 (기본값: Multi).</param>
    /// <returns>빌더 인스턴스.</returns>
    /// <remarks>
    /// TStage는 IStageLink를 받는 생성자가 필요하고, TActor는 IActorLink를 받는 생성자가 필요합니다.
    /// <code>
    /// public MyStage(IStageLink stageLink) { StageLink = stageLink; }
    /// public MyActor(IActorLink actorLink) { ActorLink = actorLink; }
    /// </code>
    /// </remarks>
    public PlayServerBootstrap UseStage<TStage, TActor>(string stageType, StageMode mode = StageMode.Multi)
        where TStage : class, IStage
        where TActor : class, IActor
    {
        _stageTypes[stageType] = typeof(TStage);
        _actorTypes[stageType] = typeof(TActor);
        _stageModes[stageType] = mode;
        return this;
    }

    /// <summary>
    /// System Controller를 등록합니다.
    /// </summary>
    /// <typeparam name="TSystemController">ISystemController 구현 타입.</typeparam>
    /// <returns>빌더 인스턴스.</returns>
    public PlayServerBootstrap UseSystemController<TSystemController>() where TSystemController : class, Abstractions.System.ISystemController
    {
        _systemControllerType = typeof(TSystemController);
        return this;
    }

    /// <summary>
    /// Play Server 인스턴스를 생성합니다.
    /// </summary>
    /// <returns>PlayServer 인스턴스.</returns>
    public PlayServer Build()
    {
        _options.Validate();

        // 필수 필드 검증 (throw expression 사용)
        var systemControllerType = _systemControllerType
            ?? throw new InvalidOperationException("SystemController is required. Use UseSystemController<T>() to register.");
        var loggerFactory = _loggerFactory
            ?? throw new InvalidOperationException("LoggerFactory is required. Use UseLoggerFactory() to register.");
        var serviceProvider = _serviceProvider
            ?? throw new InvalidOperationException("ServiceProvider is required. Use UseServiceProvider() to register.");

        // DefaultStageType이 설정된 경우에만 Stage와 Actor 필수
        if (!string.IsNullOrEmpty(_options.DefaultStageType))
        {
            if (_stageTypes.Count == 0)
                throw new InvalidOperationException("At least one Stage type must be registered when using DefaultStageType. Use UseStage<TStage, TActor>().");

            if (_actorTypes.Count == 0)
                throw new InvalidOperationException("Actor type must be registered when using DefaultStageType. Use UseStage<TStage, TActor>().");
        }

        // SystemController 인스턴스 생성 (DI를 통해 의존성 주입)
        var systemController = ActivatorUtilities.CreateInstance(serviceProvider, systemControllerType) as ISystemController
            ?? throw new InvalidOperationException($"Failed to create SystemController instance: {systemControllerType.Name}");

        var producer = new PlayProducer(_stageTypes, _actorTypes, serviceProvider, _stageModes);
        return new PlayServer(_options, producer, systemController, loggerFactory);
    }
}
