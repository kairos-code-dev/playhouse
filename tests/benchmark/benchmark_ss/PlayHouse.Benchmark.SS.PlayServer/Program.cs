using System.CommandLine;
using System.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Abstractions.System;
using PlayHouse.Benchmark.SS.PlayServer;
using PlayHouse.Core.Play.Bootstrap;
using PlayHouse.Extensions;
using PlayHouse.Infrastructure.Memory;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

// GC 최적화: Batch 모드 (고처리량 우선, Gen2 GC 빈도 감소)
if (GCSettings.IsServerGC)
{
    GCSettings.LatencyMode = GCLatencyMode.Batch;
    Console.WriteLine($"[GC] Server GC enabled, LatencyMode set to Batch");
}
else
{
    Console.WriteLine($"[GC] Warning: Workstation GC detected, Server GC recommended");
}

// CLI 인자 파싱
var tcpPortOption = new Option<int>(
    name: "--tcp-port",
    description: "PlayServer TCP port for client connections",
    getDefaultValue: () => 16110);

var zmqPortOption = new Option<int>(
    name: "--zmq-port",
    description: "ZMQ port for server-to-server communication",
    getDefaultValue: () => 16100);

var httpPortOption = new Option<int>(
    name: "--http-port",
    description: "HTTP API port for metrics",
    getDefaultValue: () => 5080);

var serverIdOption = new Option<string>(
    name: "--server-id",
    description: "Server ID (NID) for this PlayServer",
    getDefaultValue: () => "play-1");

var peersOption = new Option<string>(
    name: "--peers",
    description: "Peer servers in format 'nid1=endpoint1,nid2=endpoint2' (e.g., 'play-2=tcp://127.0.0.1:16200,api-1=tcp://127.0.0.1:16201')",
    getDefaultValue: () => "");

var logDirOption = new Option<string>(
    name: "--log-dir",
    description: "Directory for log files",
    getDefaultValue: () => "logs");

var minPoolSizeOption = new Option<int>(
    name: "--min-pool-size",
    description: "Minimum worker tasks",
    getDefaultValue: () => 100);

var maxPoolSizeOption = new Option<int>(
    name: "--max-pool-size",
    description: "Maximum worker tasks",
    getDefaultValue: () => 1000);

var rootCommand = new RootCommand("PlayHouse Benchmark Server (Server-to-Server)")
{
    tcpPortOption,
    zmqPortOption,
    httpPortOption,
    serverIdOption,
    peersOption,
    logDirOption,
    minPoolSizeOption,
    maxPoolSizeOption
};

var tcpPort = 0;
var zmqPort = 0;
var httpPort = 0;
var serverId = "play-1";
var peers = "";
var logDir = "logs";
var minPoolSize = 100;
var maxPoolSize = 1000;

rootCommand.SetHandler((context) =>
{
    tcpPort = context.ParseResult.GetValueForOption(tcpPortOption);
    zmqPort = context.ParseResult.GetValueForOption(zmqPortOption);
    httpPort = context.ParseResult.GetValueForOption(httpPortOption);
    serverId = context.ParseResult.GetValueForOption(serverIdOption)!;
    peers = context.ParseResult.GetValueForOption(peersOption)!;
    logDir = context.ParseResult.GetValueForOption(logDirOption)!;
    minPoolSize = context.ParseResult.GetValueForOption(minPoolSizeOption);
    maxPoolSize = context.ParseResult.GetValueForOption(maxPoolSizeOption);
});

await rootCommand.InvokeAsync(args);

if (tcpPort == 0)
{
    tcpPort = 16110;
    zmqPort = 16100;
    httpPort = 5080;
}

// 로그 디렉토리 생성
Directory.CreateDirectory(logDir);

// Serilog 설정
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        Path.Combine(logDir, "benchmark-ss-playserver-.log"),
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
        retainedFileCountLimit: 30)
    .CreateLogger();

try
{
    Log.Information("================================================================================");
    Log.Information("PlayHouse Benchmark Server (Server-to-Server)");
    Log.Information("================================================================================");
    Log.Information("Server ID: {ServerId}", serverId);
    Log.Information("ZMQ Port (server-to-server): {ZmqPort}", zmqPort);
    Log.Information("TCP Port (client connections): {TcpPort}", tcpPort);
    Log.Information("HTTP API Port: {HttpPort}", httpPort);
    Log.Information("Peers: {Peers}", string.IsNullOrEmpty(peers) ? "(none)" : peers);
    Log.Information("Log Directory: {LogDir}", Path.GetFullPath(logDir));
    Log.Information("HTTP API Endpoints:");
    Log.Information("  GET  http://localhost:{HttpPort}/benchmark/stats - Get metrics", httpPort);
    Log.Information("  POST http://localhost:{HttpPort}/benchmark/reset - Reset metrics", httpPort);
    Log.Information("  POST http://localhost:{HttpPort}/benchmark/shutdown - Shutdown server", httpPort);
    Log.Information("Note: Stages are auto-created when clients connect (DefaultStageType: BenchmarkStage)");
    Log.Information("================================================================================");

    // PlayServer 구성
    var services = new ServiceCollection();

    // Serilog LoggerFactory 생성
    var loggerFactory = new SerilogLoggerFactory(Log.Logger);

    services.AddSingleton<ILoggerFactory>(loggerFactory);
    services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
    services.AddLogging(builder =>
    {
        builder.AddSerilog(Log.Logger);
    });

    // Infrastructure 클래스를 위한 Logger 등록
    services.AddSingleton<ILogger<BenchmarkActor>>(
        sp => loggerFactory.CreateLogger<BenchmarkActor>());
    services.AddSingleton<ILogger<BenchmarkStage>>(
        sp => loggerFactory.CreateLogger<BenchmarkStage>());

    services.AddPlayServer(options =>
    {
        options.ServerType = ServerType.Play;
        options.ServerId = serverId;
        options.BindEndpoint = $"tcp://127.0.0.1:{zmqPort}";
        options.TcpPort = tcpPort;
        options.AuthenticateMessageId = "Authenticate";
        options.DefaultStageType = "BenchmarkStage"; // Auto-create stages
        options.MinTaskPoolSize = minPoolSize;
        options.MaxTaskPoolSize = maxPoolSize;
        options.SendHighWatermark = 1_000_000;
        options.ReceiveHighWatermark = 1_000_000;
    })
    .UseStage<BenchmarkStage, BenchmarkActor>("BenchmarkStage", StageMode.Single)
    .UseSystemController(StaticSystemController.Parse(peers));

    var serviceProvider = services.BuildServiceProvider();
    var playServer = serviceProvider.GetRequiredService<PlayServer>();

    // 종료 토큰 생성
    var cts = new CancellationTokenSource();

    // HTTP API 서버 구성
    Environment.SetEnvironmentVariable("ASPNETCORE_URLS", $"http://0.0.0.0:{httpPort}");
    var builder = WebApplication.CreateBuilder(args);

    // Serilog를 ASP.NET Core에 통합
    builder.Host.UseSerilog();
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();

    // CancellationTokenSource를 DI에 등록
    builder.Services.AddSingleton(cts);

    var app = builder.Build();
    app.MapControllers();

    // MessagePool Prewarm (런타임 할당 방지)
    MessagePool.WarmUp();
    Log.Information("Managed MessagePool warmed up.");

    // PlayServer 시작
    await playServer.StartAsync();
    Log.Information("PlayServer started (ID: {ServerId}, ZMQ: {ZmqPort}, TCP: {TcpPort})", serverId, zmqPort, tcpPort);

    // HTTP API 서버 시작
    await app.StartAsync();
    Log.Information("HTTP API server started on port {HttpPort}", httpPort);
    Log.Information("Press Ctrl+C to stop...");

    // 종료 대기
    Console.CancelKeyPress += (s, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    try
    {
        await Task.Delay(-1, cts.Token);
    }
    catch (TaskCanceledException)
    {
        Log.Information("Shutting down...");
    }

    // 정리
    await app.StopAsync();
    await playServer.StopAsync();

    Log.Information("Server stopped.");
}
catch (Exception ex)
{
    Log.Fatal(ex, "Server terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}
