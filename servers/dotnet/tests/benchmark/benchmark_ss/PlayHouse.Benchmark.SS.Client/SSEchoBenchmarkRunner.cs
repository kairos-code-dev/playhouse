using System.Diagnostics;
using Google.Protobuf;
using PlayHouse.Benchmark.SS.Shared.Proto;
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using Serilog;
using ClientConnector = PlayHouse.Connector.Connector;
using ClientPacket = PlayHouse.Connector.Protocol.Packet;

namespace PlayHouse.Benchmark.SS.Client;

public class SSEchoBenchmarkRunner(
    string serverHost,
    int serverPort,
    int ccu,
    int messageSize,
    SSCommMode commMode,
    int durationSeconds = 10,
    int inflight = 100,
    int warmupSeconds = 3)
{
    private readonly ByteString _payload = CreatePayload(messageSize);

    private static ByteString CreatePayload(int size)
    {
        var data = new byte[size];
        new Random(42).NextBytes(data);
        return ByteString.CopyFrom(data);
    }

    public async Task RunAsync()
    {
        Log.Information(">>> S2S Pure Benchmark: {Mode} Mode, {CCU} Connections, BatchSize={Batch} <<<", commMode, ccu, inflight);

        var metricsClient = new ServerMetricsClient(serverHost, 5080); // Default PlayServer HTTP port
        Log.Information("Resetting server metrics...");
        await metricsClient.ResetMetricsAsync();
        await Task.Delay(1000);

        // Phase 1: 모든 연결 + 인증 완료
        Log.Information("Phase 1: Connecting and authenticating {Count} clients...", ccu);
        var connectors = new ClientConnector[ccu];
        var connectTasks = new Task[ccu];

        for (int i = 0; i < ccu; i++)
        {
            var id = i;
            connectTasks[i] = Task.Run(async () => {
                var connector = await ConnectAndAuthenticateAsync(id);
                if (connector != null)
                {
                    connectors[id] = connector;
                }
            });
        }

        await Task.WhenAll(connectTasks);

        var connectedCount = connectors.Count(c => c != null);
        Log.Information("Phase 1 completed: {Connected}/{Total} connected", connectedCount, ccu);

        if (connectedCount == 0)
        {
            Log.Error("No ccu established. Aborting benchmark.");
            return;
        }

        // Warmup Phase: JIT 컴파일, 커넥션 워밍업
        if (warmupSeconds > 0)
        {
            Log.Information("Warmup Phase: Running warmup for {Duration}s...", warmupSeconds);
            var warmupTasks = new List<Task<long>>(connectedCount);

            for (int i = 0; i < ccu; i++)
            {
                if (connectors[i] != null)
                {
                    var connector = connectors[i];
                    var id = i;
                    warmupTasks.Add(Task.Run(() => RunBenchmark(connector, id, warmupSeconds, disconnectAfter: false)));
                }
            }

            await Task.WhenAll(warmupTasks);
            Log.Information("Warmup Phase completed. Resetting server metrics...");
            await metricsClient.ResetMetricsAsync();
            await Task.Delay(1000);
        }

        // Phase 2: 모든 연결이 준비된 후 동시에 벤치마크 시작
        Log.Information("Phase 2: Starting benchmark for all ccu...");
        var benchmarkTasks = new List<Task<long>>(connectedCount);
        var startTime = Stopwatch.GetTimestamp();

        for (int i = 0; i < ccu; i++)
        {
            if (connectors[i] != null)
            {
                var connector = connectors[i];
                var id = i;
                benchmarkTasks.Add(Task.Run(() => RunBenchmark(connector, id, durationSeconds)));
            }
        }

        var results = await Task.WhenAll(benchmarkTasks);
        var endTime = Stopwatch.GetTimestamp();
        var totalElapsedSec = (double)(endTime - startTime) / Stopwatch.Frequency;

        long totalS2SMessages = results.Sum();
        double clientObservedTps = totalS2SMessages / (totalElapsedSec > 0 ? totalElapsedSec : 1);

        Log.Information("Phase 2 completed: Benchmark finished.");
        Log.Information("Waiting for server metrics to stabilize...");
        await Task.Delay(2000);
        var serverMetrics = await metricsClient.GetMetricsAsync();

        Log.Information("================================================================================");
        Log.Information("S2S BENCHMARK RESULT");
        Log.Information("================================================================================");
        Log.Information("S2S Throughput:            {TPS:N0} msg/s", clientObservedTps);

        if (serverMetrics != null)
        {
            Log.Information("Server Avg CPU:            {CPU:F2}%", serverMetrics.CpuUsagePercent);
            Log.Information("Server Memory Alloc:       {Mem:F2} MB", serverMetrics.MemoryAllocatedMb);
            Log.Information("Server GC (0/1/2):         {G0}/{G1}/{G2}", serverMetrics.GcGen0Count, serverMetrics.GcGen1Count, serverMetrics.GcGen2Count);
            Log.Information("Server Latency (Mean):     {Mean:F2}ms", serverMetrics.LatencyMeanMs);
            Log.Information("Server Latency (P50):      {P50:F2}ms", serverMetrics.LatencyP50Ms);
            Log.Information("Server Latency (P95):      {P95:F2}ms", serverMetrics.LatencyP95Ms);
            Log.Information("Server Latency (P99):      {P99:F2}ms", serverMetrics.LatencyP99Ms);
        }

        Log.Information("Total S2S Messages:        {Count:N0}", totalS2SMessages);
        Log.Information("Total Test Time:           {Time:F2}s", totalElapsedSec);
        Log.Information("================================================================================");
    }

    private async Task<ClientConnector?> ConnectAndAuthenticateAsync(int id)
    {
        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig { RequestTimeoutMs = 30000 });

        var stageId = 1000 + id;
        if (!await connector.ConnectAsync(serverHost, serverPort))
        {
            return null;
        }

        try
        {
            await connector.AuthenticateAsync(ClientPacket.Empty("Authenticate"));
            return connector;
        }
        catch
        {
            connector.Disconnect();
            return null;
        }
    }

    private async Task<long> RunBenchmark(ClientConnector connector, int id, int duration, bool disconnectAfter = true)
    {
        var timer = new Timer(_ => connector.MainThreadAction(), null, 0, 10);
        long totalS2S = 0;
        var endTime = DateTime.UtcNow.AddSeconds(duration);

        try {
            while (DateTime.UtcNow < endTime)
            {
                var request = new TriggerSSEchoRequest {
                    BatchSize = inflight,
                    CommMode = commMode,
                    Payload = _payload
                };

                using var response = await connector.RequestAsync(new ClientPacket(request));
                if (response.MsgId.Contains("Reply"))
                {
                    var reply = TriggerSSEchoReply.Parser.ParseFrom(response.Payload.DataSpan);
                    totalS2S += reply.Count;
                }
            }
        } finally {
            timer.Dispose();
            if (disconnectAfter)
            {
                connector.Disconnect();
            }
        }

        return totalS2S;
    }
}