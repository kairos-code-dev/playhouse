using PlayHouse.Connector;
using PlayHouse.E2E.Shared.Utils;

namespace PlayHouse.E2E.Verifiers;


/// <summary>
/// 서버 생명주기 검증
/// 이 Verifier만 예외로 임시 서버 생성 허용
/// </summary>
public class ServerLifecycleVerifier : VerifierBase
{
    public override string CategoryName => "ServerLifecycle";

    public ServerLifecycleVerifier(ServerContext serverContext) : base(serverContext)
    {
    }

    public override int GetTestCount() => 1;

    protected override async Task RunTestsAsync()
    {
        await RunTest("OnDisconnect_ServerShutdown", Test_OnDisconnect_ServerShutdown);
    }

    private async Task Test_OnDisconnect_ServerShutdown()
    {
        // Given
        bool disconnected = false;
        var tempConnector = new PlayHouse.Connector.Connector();
        tempConnector.Init(new ConnectorConfig
        {
            RequestTimeoutMs = 30000,
            HeartbeatTimeoutMs = 500 // 짧은 Heartbeat timeout
        });
        tempConnector.OnDisconnect += () => { disconnected = true; };

        // 임시 서버 생성 (메인 서버와 별개) - CreatePlayServerAsync가 자동으로 Start함
        var tempServer = await ServerFactory.CreatePlayServerAsync(
            serverId: "temp",
            tcpPort: 0,
            zmqPort: 0
        );
        var tempPort = tempServer.ActualTcpPort;

        var stageId = GenerateUniqueStageId();
        var connected = await tempConnector.ConnectAsync("127.0.0.1", tempPort);
        Assert.IsTrue(connected, "Should connect to temporary server");
        await Task.Delay(500);

        // When - 서버 종료
        await tempServer.StopAsync();
        await Task.Delay(1000);

        // Consume callbacks
        for (int i = 0; i < 10; i++)
        {
            tempConnector.MainThreadAction();
            await Task.Delay(100);
        }

        // Then
        Assert.IsTrue(disconnected, "OnDisconnect should be called on server shutdown");

        // Cleanup
        await tempConnector.DisposeAsync();
        await tempServer.DisposeAsync();
    }
}
