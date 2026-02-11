using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.E2E.Shared.Proto;

namespace PlayHouse.E2E.Verifiers;

/// <summary>
/// Connector 콜백 성능 검증
/// RequestCallback 모드에서 MainThreadAction()을 통해 콜백이 처리되는지 확인
/// 8KB 대용량 메시지를 포함한 여러 시나리오를 E2E로 검증
/// </summary>
public class ConnectorCallbackPerformanceVerifier : VerifierBase
{
    public override string CategoryName => "ConnectorCallbackPerformance";

    public ConnectorCallbackPerformanceVerifier(ServerContext serverContext) : base(serverContext)
    {
    }

    public override int GetTestCount() => 2;

    protected override async Task SetupAsync()
    {
        // 이 Verifier는 별도 Connector 인스턴스를 생성하므로 base.SetupAsync() 호출 안 함
        await Task.CompletedTask;
    }

    protected override async Task TeardownAsync()
    {
        // 별도 Connector는 각 테스트에서 정리
        await Task.CompletedTask;
    }

    protected override async Task RunTestsAsync()
    {
        await RunTest("RequestCallback_8KBMessage_RequiresMainThreadAction", Test_RequestCallback_8KBMessage);
        await RunTest("RequestCallback_MainThreadQueue_RequiresMainThreadAction", Test_RequestCallback_MainThreadQueue);
    }

    /// <summary>
    /// Test 1: RequestCallback 모드 - 8KB 메시지 정상 처리, MainThreadAction 필요
    /// </summary>
    /// <remarks>
    /// RequestCallback은 SynchronizationContext가 없으면 큐를 사용합니다.
    /// MainThreadAction() 호출이 필수이며, 8KB 이상 메시지도 정상 처리되어야 합니다.
    /// </remarks>
    private async Task Test_RequestCallback_8KBMessage()
    {
        // Given: 별도 Connector 생성 (RequestCallback 모드)
        var connector = new PlayHouse.Connector.Connector();
        connector.Init(new ConnectorConfig
        {
            RequestTimeoutMs = 30000
        });

        try
        {
            var stageId = GenerateUniqueStageId();
            var connected = await connector.ConnectAsync("127.0.0.1", ServerContext.TcpPort);
            Assert.IsTrue(connected, "Should connect to server");
            await Task.Delay(100);

            // 인증
            using (var authPacket = new Packet(new AuthenticateRequest
            {
                UserId = "perf_user",
                Token = "token"
            }))
            {
                var authReply = await connector.AuthenticateAsync(authPacket);
                Assert.StringContains(authReply.MsgId, "AuthenticateReply", "Authentication should succeed");
            }

            var receivedCount = 0;
            var timer = new Timer(_ =>
            {
                try
                {
                    connector.MainThreadAction();
                }
                catch
                {
                    // Connector가 정리된 경우 무시
                }
            }, null, 0, 10);

            try
            {
                // When: 50개 요청 전송 (8KB 메시지)
                var largeContent = new string('A', 8192);  // 8KB
                var packets = new List<IPacket>();

                for (int i = 0; i < 50; i++)
                {
                    var request = new EchoRequest
                    {
                        Content = $"Sequence {i} - {largeContent}",
                        Sequence = i
                    };

                    var packet = new Packet(request);
                    packets.Add(packet);

                    connector.Request(packet, response =>
                    {
                        Interlocked.Increment(ref receivedCount);
                        response.Dispose();
                    });
                }

                // Then: 모든 콜백 실행 확인 (최대 5초 대기)
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                while (receivedCount < 50 && stopwatch.ElapsedMilliseconds < 5000)
                {
                    await Task.Delay(50);
                }

                Assert.Equals(50, receivedCount, "All 50 callbacks should be invoked");

                // Cleanup packets
                foreach (var packet in packets)
                {
                    packet.Dispose();
                }
            }
            finally
            {
                timer.Dispose();
            }
        }
        finally
        {
            connector.Disconnect();
            await connector.DisposeAsync();
        }
    }

    /// <summary>
    /// Test 2: RequestCallback 모드 - SynchronizationContext 없이 큐 모드 검증
    /// </summary>
    /// <remarks>
    /// SynchronizationContext가 없으면 큐를 사용합니다.
    /// MainThreadAction() 호출이 필수입니다.
    /// </remarks>
    private async Task Test_RequestCallback_MainThreadQueue()
    {
        // Given: 별도 Connector 생성
        var connector = new PlayHouse.Connector.Connector();
        connector.Init(new ConnectorConfig
        {
            RequestTimeoutMs = 5000
        });

        try
        {
            var stageId = GenerateUniqueStageId();
            var connected = await connector.ConnectAsync("127.0.0.1", ServerContext.TcpPort);
            Assert.IsTrue(connected, "Should connect to server");
            await Task.Delay(100);

            // 인증
            using (var authPacket = new Packet(new AuthenticateRequest
            {
                UserId = "queue_user",
                Token = "token"
            }))
            {
                var authReply = await connector.AuthenticateAsync(authPacket);
                Assert.StringContains(authReply.MsgId, "AuthenticateReply", "Authentication should succeed");
            }

            var receivedResponses = new List<string>();
            var receiveLock = new object();

            // When: 10개 요청 전송 (1KB 메시지)
            var largeContent = new string('A', 1024);  // 1KB
            var packets = new List<IPacket>();

            for (int i = 0; i < 10; i++)
            {
                var request = new EchoRequest
                {
                    Content = $"Sequence {i} - {largeContent}",
                    Sequence = i
                };

                var packet = new Packet(request);
                packets.Add(packet);

                connector.Request(packet, response =>
                {
                    var reply = EchoReply.Parser.ParseFrom(response.Payload.DataSpan);
                    lock (receiveLock)
                    {
                        receivedResponses.Add(reply.Content);
                    }
                    response.Dispose();
                });
            }

            // Then: MainThreadAction() 호출하여 콜백 처리
            for (int i = 0; i < 50; i++)
            {
                connector.MainThreadAction();
                await Task.Delay(20);

                lock (receiveLock)
                {
                    if (receivedResponses.Count >= 10)
                        break;
                }
            }

            lock (receiveLock)
            {
                Assert.Equals(10, receivedResponses.Count,
                    "All 10 callbacks should be processed via MainThreadAction");
            }

            // Cleanup packets
            foreach (var packet in packets)
            {
                packet.Dispose();
            }
        }
        finally
        {
            connector.Disconnect();
            await connector.DisposeAsync();
        }
    }
}
