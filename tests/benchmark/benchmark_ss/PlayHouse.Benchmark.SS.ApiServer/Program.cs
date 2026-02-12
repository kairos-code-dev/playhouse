using System.CommandLine;
using System.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.System;
using PlayHouse.Benchmark.SS.ApiServer;
using PlayHouse.Core.Api.Bootstrap;
using PlayHouse.Extensions;
using PlayHouse.Infrastructure.Memory;
using Serilog;
using Serilog.Events;

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
var zmqPortOption = new Option<int>(
    name: "--zmq-port",
    description: "ZMQ port for server-to-server communication",
    getDefaultValue: () => 16201);

var httpPortOption = new Option<int>(
    name: "--http-port",
    description: "HTTP API port for metrics",
    getDefaultValue: () => 5081);

var serverIdOption = new Option<string>(
    name: "--server-id",
    description: "ApiServer ID",
    getDefaultValue: () => "api-1");

var peersOption = new Option<string>(
    name: "--peers",
    description: "Comma-separated peer list (e.g., play-1=tcp://127.0.0.1:16100,play-2=tcp://127.0.0.1:16101)",
    getDefaultValue: () => "play-1=tcp://127.0.0.1:16100");

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

var diagLevelOption = new Option<int>(
    name: "--diagnostic-level",
    description: "S2S Diagnostic Level (-1: Normal, 0: Raw Echo, 1: Header Echo)",
    getDefaultValue: () => -1);

var rootCommand = new RootCommand("PlayHouse Server-to-Server Benchmark API Server")
{
    zmqPortOption,
    httpPortOption,
    serverIdOption,
    peersOption,
    logDirOption,
    minPoolSizeOption,
    maxPoolSizeOption,
    diagLevelOption
};

var zmqPort = 0;
var httpPort = 0;
var serverId = "api-1";
var peers = "play-1=tcp://127.0.0.1:16100";
var logDir = "logs";
var minPoolSize = 100;
var maxPoolSize = 1000;
var diagLevel = -1;

rootCommand.SetHandler((zmq, http, sid, peersStr, logDirectory, minPool, maxPool, dl) =>
{
    zmqPort = zmq;
    httpPort = http;
    serverId = sid;
    peers = peersStr;
    logDir = logDirectory;
    minPoolSize = minPool;
    maxPoolSize = maxPool;
    diagLevel = dl;
}, zmqPortOption, httpPortOption, serverIdOption, peersOption, logDirOption, minPoolSizeOption, maxPoolSizeOption, diagLevelOption);

await rootCommand.InvokeAsync(args);

if (zmqPort == 0)
{
    zmqPort = 16201;
    httpPort = 5081;
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
        Path.Combine(logDir, "benchmark-api-server-.log"),
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
        retainedFileCountLimit: 30)
    .CreateLogger();

try
{
    Log.Information("================================================================================");
    Log.Information("PlayHouse Server-to-Server Benchmark API Server");
    Log.Information("================================================================================");
    Log.Information("Server ID: {ServerId}", serverId);
    Log.Information("ZMQ Port (server-to-server): {ZmqPort}", zmqPort);
    Log.Information("HTTP API Port: {HttpPort}", httpPort);
    Log.Information("Peers: {Peers}", peers);
    Log.Information("Log Directory: {LogDir}", Path.GetFullPath(logDir));
    Log.Information("HTTP API Endpoints:");
    Log.Information("  POST http://localhost:{HttpPort}/benchmark/shutdown - Shutdown server", httpPort);
    Log.Information("================================================================================");

    // 종료 토큰 생성
    var cts = new CancellationTokenSource();

    // ApiServer 구성
    var services = new ServiceCollection();

    // Serilog LoggerFactory 생성
    var loggerFactory = LoggerFactory.Create(builder =>
    {
        builder.AddSerilog(Log.Logger);
    });

    services.AddSingleton<ILoggerFactory>(loggerFactory);
    services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
    services.AddLogging(builder =>
    {
        builder.AddSerilog(Log.Logger);
    });

    // Infrastructure 클래스를 위한 Logger 등록
    services.AddSingleton<ILogger<BenchmarkApiController>>(
        sp => loggerFactory.CreateLogger<BenchmarkApiController>());

    services.AddApiServer(options =>
    {
        options.ServerType = ServerType.Api;
        options.ServerId = serverId;
        options.BindEndpoint = $"tcp://127.0.0.1:{zmqPort}";
        options.MinTaskPoolSize = minPoolSize;
        options.MaxTaskPoolSize = maxPoolSize;
    })
    .UseSystemController(StaticSystemController.Parse(peers))
    .UseController<BenchmarkApiController>();

    var serviceProvider = services.BuildServiceProvider();
    var apiServer = serviceProvider.GetRequiredService<ApiServer>();

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

    // Stage 생성 HTTP 엔드포인트 추가
    app.MapPost("/benchmark/create-stage", async (CreateStageHttpRequest request) =>
    {
        try
        {
            var createPacket = PlayHouse.Core.Shared.CPacket.Empty("CreateStage");
            var result = await apiServer.ApiLink!.CreateStage(
                request.PlayNid,
                request.StageType,
                request.StageId,
                createPacket);

            return TypedResults.Ok(new CreateStageHttpResponse
            {
                Success = result.Result,
                ErrorCode = result.Result ? 0 : -1,
                StageId = request.StageId,
                PlayNid = request.PlayNid,
                ErrorMessage = result.Result ? null : "Failed to create stage"
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create stage {StageId} on {PlayNid}", request.StageId, request.PlayNid);
            return TypedResults.Ok(new CreateStageHttpResponse
            {
                Success = false,
                ErrorCode = -1,
                StageId = request.StageId,
                PlayNid = request.PlayNid,
                ErrorMessage = ex.Message
            });
        }
    });

    // MessagePool Prewarm (런타임 할당 방지)
    MessagePool.WarmUp();
    Log.Information("Managed MessagePool warmed up.");

    // ApiServer 시작
    await apiServer.StartAsync();
    apiServer.DiagnosticLevel = diagLevel;
    Log.Information("API Server started (ZMQ: {ZmqPort}) with DiagnosticLevel: {DiagLevel}", zmqPort, diagLevel);
    Log.Information("SystemController will manage connections to peers: {Peers}", peers);

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
    await apiServer.StopAsync();

    Log.Information("API Server stopped.");
}
catch (Exception ex)
{
    Log.Fatal(ex, "API Server terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>
/// HTTP를 통한 Stage 생성 요청
/// </summary>
internal record CreateStageHttpRequest
{
    public required string PlayNid { get; set; }
    public required string StageType { get; set; }
    public required string StageId { get; set; }
}

/// <summary>
/// HTTP를 통한 Stage 생성 응답
/// </summary>
internal record CreateStageHttpResponse
{
    public bool Success { get; set; }
    public int ErrorCode { get; set; }
    public string StageId { get; set; } = "";
    public string PlayNid { get; set; } = "";
    public string? ErrorMessage { get; set; }
}
