using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using Serilog;
using ClientConnector = PlayHouse.Connector.Connector;
using ClientPacket = PlayHouse.Connector.Protocol.Packet;

namespace PlayHouse.Benchmark.Client;

/// <summary>
/// 연결 + 인증만 테스트 (패킷 전송 없음)
/// 10000개 연결이 성공하는지 확인용
/// </summary>
public class ConnectionOnlyTest(
    string serverHost,
    int serverPort,
    int connections)
{
    private int _successCount;
    private int _failedConnectCount;
    private int _failedAuthCount;

    public async Task RunAsync()
    {
        Log.Information("=== Connection Only Test ===");
        Log.Information("  Target connections: {Connections:N0}", connections);
        Log.Information("  Server: {Host}:{Port}", serverHost, serverPort);
        Log.Information("");

        var startTime = DateTime.UtcNow;

        // 모든 연결을 동시에 시작 (BenchmarkRunner와 동일)
        var tasks = new Task[connections];
        for (int i = 0; i < connections; i++)
        {
            var connectionId = i;
            tasks[i] = Task.Run(async () => await TestConnectionAsync(connectionId));
        }

        Log.Information("[{Time:HH:mm:ss}] Starting all {Connections:N0} connections simultaneously...",
            DateTime.Now, connections);

        await Task.WhenAll(tasks);

        var elapsed = DateTime.UtcNow - startTime;

        Log.Information("");
        Log.Information("=== Connection Only Test Results ===");
        Log.Information("  Total time: {Elapsed:F1} seconds", elapsed.TotalSeconds);
        Log.Information("  Success: {Success:N0}", _successCount);
        Log.Information("  Failed (connect): {FailedConnect:N0}", _failedConnectCount);
        Log.Information("  Failed (auth): {FailedAuth:N0}", _failedAuthCount);
        Log.Information("  Success rate: {Rate:F1}%", (double)_successCount / connections * 100);
    }

    private async Task TestConnectionAsync(int connectionId)
    {
        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig());

        var stageId = 1000 + connectionId;

        try
        {
            // 연결
            var connected = await connector.ConnectAsync(serverHost, serverPort);
            if (!connected)
            {
                Interlocked.Increment(ref _failedConnectCount);
                return;
            }

            // 인증
            try
            {
                using var authPacket = ClientPacket.Empty("AuthenticateRequest");
                var authReply = await connector.AuthenticateAsync(authPacket);

                // 성공!
                Interlocked.Increment(ref _successCount);

                if (connectionId % 1000 == 0)
                {
                    Log.Debug("[Connection {ConnectionId}] Auth succeeded: {MsgId}", connectionId, authReply.MsgId);
                }
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _failedAuthCount);
                if (connectionId % 100 == 0)
                {
                    Log.Warning("[Connection {ConnectionId}] Auth failed: {Message}", connectionId, ex.Message);
                }
            }
        }
        finally
        {
            connector.Disconnect();
        }
    }
}
