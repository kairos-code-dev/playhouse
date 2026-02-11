#nullable enable

using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions.Api;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Api.Bootstrap;
using PlayHouse.Core.Play.Bootstrap;
using PlayHouse.Extensions;
using PlayHouse.E2E.Shared.Infrastructure;

namespace PlayHouse.E2E.Shared.Utils;

/// <summary>
/// PlayServer 및 ApiServer 생성을 위한 헬퍼 유틸리티.
/// </summary>
public static class ServerFactory
{
    /// <summary>
    /// 단일 PlayServer를 생성합니다.
    /// </summary>
    /// <param name="serverId">서버 ID (기본값: "1")</param>
    /// <param name="tcpPort">TCP 포트 (0이면 자동 할당)</param>
    /// <param name="zmqPort">ZMQ 포트 (0이면 자동 할당)</param>
    /// <param name="authenticateMessageId">인증 메시지 ID (기본값: "AuthenticateRequest")</param>
    /// <param name="defaultStageType">기본 Stage 타입 (기본값: "TestStage")</param>
    /// <param name="requestTimeoutMs">요청 타임아웃 (기본값: 30000ms)</param>
    /// <param name="logLevel">로그 레벨 (기본값: Warning)</param>
    public static async Task<PlayServer> CreatePlayServerAsync(
        string serverId = "1",
        int tcpPort = 0,
        int zmqPort = 0,
        string authenticateMessageId = "AuthenticateRequest",
        string defaultStageType = "TestStage",
        int requestTimeoutMs = 30000,
        LogLevel logLevel = LogLevel.Warning)
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole().SetMinimumLevel(logLevel);
            // 서버 종료 시 정상적인 SocketException 로그 필터링
            builder.AddFilter((category, level) =>
            {
                if (category == "PlayHouse.Bootstrap.PlayServer" && level == LogLevel.Error)
                    return false; // Error in accept loop 로그 숨김
                return true;
            });
        });

        // ServiceCollection 생성 (DI 필수)
        var services = new ServiceCollection();
        services.AddSingleton(loggerFactory);
        services.AddSingleton<ILogger<PlayServer>>(
            sp => loggerFactory.CreateLogger<PlayServer>());
        services.AddSingleton<ILogger<TestStageImpl>>(
            sp => loggerFactory.CreateLogger<TestStageImpl>());
        services.AddSingleton<ILogger<TestActorImpl>>(
            sp => loggerFactory.CreateLogger<TestActorImpl>());
        services.AddSingleton<ILogger<TestSystemController>>(
            sp => loggerFactory.CreateLogger<TestSystemController>());

        // ISystemController 등록 (PlayServer가 필요로 함)
        services.AddSingleton<PlayHouse.Abstractions.System.ISystemController, TestSystemController>();

        var serviceProvider = services.BuildServiceProvider();

        var playServer = new PlayServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = serverId;
                options.BindEndpoint = $"tcp://127.0.0.1:{zmqPort}";
                options.TcpPort = tcpPort;
                options.RequestTimeoutMs = requestTimeoutMs;
                options.AuthenticateMessageId = authenticateMessageId;
                options.DefaultStageType = defaultStageType;
            })
            .UseLoggerFactory(loggerFactory)
            .UseServiceProvider(serviceProvider)
            .UseStage<TestStageImpl, TestActorImpl>(defaultStageType)
            .UseStage<TestStageImpl, TestActorImpl>("SingleStage", StageMode.Single)
            .UseSystemController<TestSystemController>()
            .Build();

        await playServer.StartAsync();
        return playServer;
    }

    /// <summary>
    /// DI를 지원하는 PlayServer를 생성합니다.
    /// AddPlayServer extension을 사용합니다.
    /// </summary>
    /// <param name="serverId">서버 ID (기본값: "1")</param>
    /// <param name="tcpPort">TCP 포트 (0이면 자동 할당)</param>
    /// <param name="zmqPort">ZMQ 포트 (0이면 자동 할당)</param>
    /// <param name="authenticateMessageId">인증 메시지 ID (기본값: "AuthenticateRequest")</param>
    /// <param name="defaultStageType">기본 Stage 타입 (기본값: "DITestStage")</param>
    /// <param name="requestTimeoutMs">요청 타임아웃 (기본값: 30000ms)</param>
    /// <param name="logLevel">로그 레벨 (기본값: Warning)</param>
    public static async Task<(PlayServer Server, IServiceProvider ServiceProvider)> CreateDIPlayServerAsync(
        string serverId = "di-1",
        int tcpPort = 0,
        int zmqPort = 0,
        string authenticateMessageId = "AuthenticateRequest",
        string defaultStageType = "DITestStage",
        int requestTimeoutMs = 30000,
        LogLevel logLevel = LogLevel.Warning)
    {
        var services = new ServiceCollection();

        // Logging 등록
        services.AddLogging(builder =>
        {
            builder.AddConsole().SetMinimumLevel(logLevel);
            // 서버 종료 시 정상적인 SocketException 로그 필터링
            builder.AddFilter((category, level) =>
            {
                if (category == "PlayHouse.Bootstrap.PlayServer" && level == LogLevel.Error)
                    return false; // Error in accept loop 로그 숨김
                return true;
            });
        });

        // 사용자 서비스 등록 (DI 검증용)
        services.AddSingleton<ITestService, TestService>();

        // Logger 명시적 등록 (DITestStage, DITestActor, TestSystemController용)
        services.AddSingleton<ILogger<DITestStage>>(
            sp => sp.GetRequiredService<ILoggerFactory>().CreateLogger<DITestStage>());
        services.AddSingleton<ILogger<DITestActor>>(
            sp => sp.GetRequiredService<ILoggerFactory>().CreateLogger<DITestActor>());
        services.AddSingleton<ILogger<TestSystemController>>(
            sp => sp.GetRequiredService<ILoggerFactory>().CreateLogger<TestSystemController>());

        // PlayServer 등록 및 구성
        services.AddPlayServer(options =>
        {
            options.ServerId = serverId;
            options.BindEndpoint = $"tcp://127.0.0.1:{zmqPort}";
            options.TcpPort = tcpPort;
            options.RequestTimeoutMs = requestTimeoutMs;
            options.AuthenticateMessageId = authenticateMessageId;
            options.DefaultStageType = defaultStageType;
        })
        .UseStage<DITestStage, DITestActor>(defaultStageType)
        .UseSystemController<TestSystemController>();

        var serviceProvider = services.BuildServiceProvider();
        var playServer = serviceProvider.GetRequiredService<PlayServer>();

        await playServer.StartAsync();
        return (playServer, serviceProvider);
    }

    /// <summary>
    /// 단일 ApiServer를 생성합니다.
    /// </summary>
    /// <param name="serverId">서버 ID (기본값: "api-1")</param>
    /// <param name="zmqPort">ZMQ 포트 (0이면 자동 할당)</param>
    /// <param name="requestTimeoutMs">요청 타임아웃 (기본값: 30000ms)</param>
    /// <param name="logLevel">로그 레벨 (기본값: Warning)</param>
    public static async Task<ApiServer> CreateApiServerAsync(
        string serverId = "api-1",
        int zmqPort = 0,
        int requestTimeoutMs = 30000,
        LogLevel logLevel = LogLevel.Warning)
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole().SetMinimumLevel(logLevel);
        });

        // ServiceCollection 생성 (DI 필수)
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(loggerFactory);
        services.AddSingleton<ILogger<TestApiController>>(
            sp => loggerFactory.CreateLogger<TestApiController>());
        services.AddSingleton<ILogger<TestSystemController>>(
            sp => loggerFactory.CreateLogger<TestSystemController>());

        // IApiController 등록 (ApiReflection이 필요로 함)
        services.AddTransient<PlayHouse.Abstractions.Api.IApiController, TestApiController>();

        // ISystemController 등록 (ApiServer가 필요로 함)
        services.AddSingleton<PlayHouse.Abstractions.System.ISystemController, TestSystemController>();

        var serviceProvider = services.BuildServiceProvider();

        var apiServer = new ApiServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = serverId;
                options.BindEndpoint = $"tcp://127.0.0.1:{zmqPort}";
                options.RequestTimeoutMs = requestTimeoutMs;
            })
            .UseLoggerFactory(loggerFactory)
            .UseServiceProvider(serviceProvider)
            .UseSystemController<TestSystemController>()
            .Build();

        await apiServer.StartAsync();
        return apiServer;
    }

    /// <summary>
    /// ApiServer와 ASP.NET Core HTTP API를 함께 생성합니다.
    /// HTTP API를 통한 E2E 검증을 위해 사용됩니다.
    /// </summary>
    /// <param name="serverId">API 서버 ID (기본값: "api-1")</param>
    /// <param name="zmqPort">ZMQ 포트 (0이면 자동 할당)</param>
    /// <param name="playServerId">대상 PlayServer ID</param>
    /// <param name="requestTimeoutMs">요청 타임아웃 (기본값: 30000ms)</param>
    /// <param name="logLevel">로그 레벨 (기본값: Warning)</param>
    /// <returns>ApiServer 인스턴스, WebApplication 인스턴스, 바인딩된 HTTP 포트</returns>
    public static async Task<(ApiServer ApiServer, WebApplication HttpApp, int HttpPort)> CreateApiServerWithHttpAsync(
        string serverId = "api-1",
        int zmqPort = 0,
        string playServerId = "play-1",
        int requestTimeoutMs = 30000,
        LogLevel logLevel = LogLevel.Warning)
    {
        // 1. ApiServer 생성
        var apiServer = await CreateApiServerAsync(serverId, zmqPort, requestTimeoutMs, logLevel);

        // 2. ASP.NET Core WebApplication 생성
        var builder = WebApplication.CreateBuilder();

        // 동적 포트 할당 (0을 사용하여 자동 할당)
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        // Logging 설정
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole().SetMinimumLevel(logLevel);

        // Configuration 설정
        builder.Configuration["PlayServerId"] = playServerId;

        // Controllers 등록 (PlayHouse.E2E.Shared 어셈블리의 컨트롤러 포함)
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(TestHttpApiController).Assembly);

        // ApiSender를 싱글톤으로 등록
        var apiSender = apiServer.ApiLink ?? throw new InvalidOperationException("ApiServer.ApiSender is null");
        builder.Services.AddSingleton<IApiLink>(apiSender);

        var app = builder.Build();

        // Controller 라우팅 설정
        app.MapControllers();

        // HTTP 서버 시작
        await app.StartAsync();

        // 실제 바인딩된 포트 추출
        var httpPort = ExtractHttpPort(app);

        return (apiServer, app, httpPort);
    }

    /// <summary>
    /// WebApplication에서 실제 바인딩된 HTTP 포트를 추출합니다.
    /// </summary>
    private static int ExtractHttpPort(WebApplication app)
    {
        // app.Urls에서 첫 번째 URL의 포트를 추출
        var url = app.Urls.FirstOrDefault()
            ?? throw new InvalidOperationException("No URLs found in WebApplication");

        // URL 형식: http://127.0.0.1:12345
        var uri = new Uri(url);
        return uri.Port;
    }

    /// <summary>
    /// 실제 바인딩된 TCP 포트를 가져옵니다.
    /// </summary>
    /// <param name="playServer">PlayServer 인스턴스</param>
    /// <returns>바인딩된 TCP 포트 번호</returns>
    public static int GetActualTcpPort(PlayServer playServer)
    {
        // PlayServer에서 실제 바인딩된 포트를 가져오는 로직
        // 포트 0으로 시작한 경우 자동 할당된 포트를 반환
        // 구현은 PlayServer의 내부 구조에 따라 조정 필요

        // 임시로 간단한 구현: reflection을 통해 가져오거나 public API 사용
        var tcpListenerField = playServer.GetType().GetField("_tcpListener",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (tcpListenerField?.GetValue(playServer) is TcpListener tcpListener)
        {
            return ((IPEndPoint)tcpListener.LocalEndpoint).Port;
        }

        throw new InvalidOperationException("Cannot get actual TCP port from PlayServer");
    }

    /// <summary>
    /// 사용 가능한 무작위 포트를 가져옵니다.
    /// </summary>
    public static int GetAvailablePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)socket.LocalEndPoint!).Port;
        return port;
    }

    /// <summary>
    /// 테스트용 자체 서명 인증서를 생성합니다.
    /// </summary>
    public static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddIpAddress(IPAddress.Loopback);
        sanBuilder.AddDnsName("localhost");
        request.CertificateExtensions.Add(sanBuilder.Build());

        var cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1));

        return new X509Certificate2(cert.Export(X509ContentType.Pfx));
    }

    /// <summary>
    /// TCP + TLS 지원 PlayServer를 생성합니다.
    /// </summary>
    public static async Task<PlayServer> CreatePlayServerWithTlsAsync(
        string serverId = "tls-1",
        int tcpPort = 0,
        int zmqPort = 0,
        X509Certificate2? certificate = null,
        string authenticateMessageId = "AuthenticateRequest",
        string defaultStageType = "TestStage",
        int requestTimeoutMs = 30000,
        LogLevel logLevel = LogLevel.Warning)
    {
        var cert = certificate ?? CreateSelfSignedCertificate();

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole().SetMinimumLevel(logLevel);
            builder.AddFilter((category, level) =>
            {
                if (category == "PlayHouse.Bootstrap.PlayServer" && level == LogLevel.Error)
                    return false;
                return true;
            });
        });

        var services = new ServiceCollection();
        services.AddSingleton(loggerFactory);
        services.AddSingleton<ILogger<PlayServer>>(sp => loggerFactory.CreateLogger<PlayServer>());
        services.AddSingleton<ILogger<TestStageImpl>>(sp => loggerFactory.CreateLogger<TestStageImpl>());
        services.AddSingleton<ILogger<TestActorImpl>>(sp => loggerFactory.CreateLogger<TestActorImpl>());
        services.AddSingleton<ILogger<TestSystemController>>(sp => loggerFactory.CreateLogger<TestSystemController>());
        services.AddSingleton<PlayHouse.Abstractions.System.ISystemController, TestSystemController>();

        var serviceProvider = services.BuildServiceProvider();

        var playServer = new PlayServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = serverId;
                options.BindEndpoint = $"tcp://127.0.0.1:{zmqPort}";
                options.TcpPort = null; // TCP 비활성화 (TLS만 사용)
                options.RequestTimeoutMs = requestTimeoutMs;
                options.AuthenticateMessageId = authenticateMessageId;
                options.DefaultStageType = defaultStageType;
            })
            .ConfigureTcpWithTls(tcpPort, cert)
            .UseLoggerFactory(loggerFactory)
            .UseServiceProvider(serviceProvider)
            .UseStage<TestStageImpl, TestActorImpl>(defaultStageType)
            .UseSystemController<TestSystemController>()
            .Build();

        await playServer.StartAsync();
        return playServer;
    }

    /// <summary>
    /// WebSocket 지원 PlayServer를 생성합니다 (ASP.NET Core 호스팅 필요).
    /// </summary>
    public static async Task<(PlayServer Server, WebApplication HttpApp, int HttpPort)> CreatePlayServerWithWebSocketAsync(
        string serverId = "ws-1",
        int zmqPort = 0,
        string wsPath = "/ws",
        string authenticateMessageId = "AuthenticateRequest",
        string defaultStageType = "TestStage",
        int requestTimeoutMs = 30000,
        LogLevel logLevel = LogLevel.Warning)
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole().SetMinimumLevel(logLevel);
        });

        var services = new ServiceCollection();
        services.AddSingleton(loggerFactory);
        services.AddSingleton<ILogger<PlayServer>>(sp => loggerFactory.CreateLogger<PlayServer>());
        services.AddSingleton<ILogger<TestStageImpl>>(sp => loggerFactory.CreateLogger<TestStageImpl>());
        services.AddSingleton<ILogger<TestActorImpl>>(sp => loggerFactory.CreateLogger<TestActorImpl>());
        services.AddSingleton<ILogger<TestSystemController>>(sp => loggerFactory.CreateLogger<TestSystemController>());
        services.AddSingleton<PlayHouse.Abstractions.System.ISystemController, TestSystemController>();

        var serviceProvider = services.BuildServiceProvider();

        var playServer = new PlayServerBootstrap()
            .Configure(options =>
            {
                options.ServerId = serverId;
                options.BindEndpoint = $"tcp://127.0.0.1:{zmqPort}";
                options.TcpPort = null; // TCP 비활성화
                options.RequestTimeoutMs = requestTimeoutMs;
                options.AuthenticateMessageId = authenticateMessageId;
                options.DefaultStageType = defaultStageType;
            })
            .ConfigureWebSocket(wsPath)
            .UseLoggerFactory(loggerFactory)
            .UseServiceProvider(serviceProvider)
            .UseStage<TestStageImpl, TestActorImpl>(defaultStageType)
            .UseSystemController<TestSystemController>()
            .Build();

        // ASP.NET Core WebApplication 생성
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole().SetMinimumLevel(logLevel);

        var app = builder.Build();

        // WebSocket 미들웨어 설정
        app.UseWebSockets();

        // PlayServer를 먼저 시작해야 _transportServer가 생성됨
        await playServer.StartAsync();

        // PlayServer의 WebSocket 서버와 연결 (StartAsync 이후에 호출해야 함)
        var wsServers = playServer.GetWebSocketServers();
        if (wsServers.Any())
        {
            var wsServer = wsServers.First();
            app.Map(wsPath, async context => await wsServer.HandleAsync(context));
        }

        await app.StartAsync();

        var httpPort = ExtractHttpPort(app);
        return (playServer, app, httpPort);
    }
}
