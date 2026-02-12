using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.E2E.Shared.Proto;

namespace PlayHouse.E2E.Verifiers;

using Google.Protobuf;

/// <summary>
/// Request/OnReceive에서 Packet 자동 Dispose 검증
/// </summary>
public class PacketAutoDisposeVerifier : VerifierBase
{
    private readonly List<(long stageId, string stageType, string msgId, byte[] payloadData)> _receivedMessages = new();
    private Action<long, string, IPacket>? _receiveHandler;

    public override string CategoryName => "PacketAutoDispose";

    public PacketAutoDisposeVerifier(ServerContext serverContext) : base(serverContext)
    {
    }

    public override int GetTestCount() => 9;

    protected override async Task SetupAsync()
    {
        _receivedMessages.Clear();

        // OnReceive 핸들러 등록
        _receiveHandler = (stageId, stageType, packet) =>
        {
            var msgId = packet.MsgId;
            var payloadData = packet.Payload.DataSpan.ToArray();
            _receivedMessages.Add((stageId, stageType, msgId, payloadData));
        };
        Connector.OnReceive += _receiveHandler;

        // 연결 + 인증
        if (!Connector.IsConnected())
        {
            var connected = await Connector.ConnectAsync("127.0.0.1", ServerContext.TcpPort);
            Assert.IsTrue(connected, "Should connect to server");
            await Task.Delay(100);
        }

        if (!Connector.IsAuthenticated())
        {
            using var authPacket = Packet.Empty("AuthenticateRequest");
            await Connector.AuthenticateAsync(authPacket);
            Assert.IsTrue(Connector.IsAuthenticated(), "Authentication should succeed");
        }
    }

    protected override Task TeardownAsync()
    {
        // 핸들러 해제
        if (_receiveHandler != null)
        {
            Connector.OnReceive -= _receiveHandler;
            _receiveHandler = null;
        }

        _receivedMessages.Clear();

        return Task.CompletedTask;
    }

    protected override async Task RunTestsAsync()
    {
        await RunTest("Request_CallbackPattern_AutoDispose", Test_Request_CallbackPattern_AutoDispose);
        await RunTest("Request_MultipleCallbacks_AutoDispose", Test_Request_MultipleCallbacks_AutoDispose);
        await RunTest("OnReceive_AutoDispose", Test_OnReceive_AutoDispose);
        await RunTest("RequestAsync_CallerResponsibility", Test_RequestAsync_CallerResponsibility);
        await RunTest("RequestAsync_Multiple_CallerResponsibility", Test_RequestAsync_Multiple_CallerResponsibility);
        await RunTest("Mixed_AsyncAndCallback", Test_Mixed_AsyncAndCallback);
        await RunTest("OnDispatch_RequestToApi_AutoDispose", Test_OnDispatch_RequestToApi_AutoDispose);
        await RunTest("OnDispatch_RequestToStage_AutoDispose", Test_OnDispatch_RequestToStage_AutoDispose);
        await RunTest("TimerCallback_RequestAsync_AutoDispose", Test_TimerCallback_RequestAsync_AutoDispose);
    }

    private async Task Test_Request_CallbackPattern_AutoDispose()
    {
        // Given
        var echoRequest = new EchoRequest { Content = "Callback Test", Sequence = 1 };
        using var packet = new Packet(echoRequest);

        IPacket? receivedResponse = null;
        var responseReceived = new ManualResetEventSlim(false);

        // When - Callback 패턴으로 요청
        Connector.Request(packet, response =>
        {
            receivedResponse = response;
            var echoReply = EchoReply.Parser.ParseFrom(response.Payload.DataSpan);
            Assert.Equals(echoReply.Content, "Callback Test", "Response content should match");
            responseReceived.Set();
            // 콜백이 끝나면 ClientNetwork가 자동으로 packet.Dispose() 호출
        });

        // Wait for callback
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!responseReceived.IsSet && DateTime.UtcNow < timeout)
        {
            Connector.MainThreadAction();
            await Task.Delay(50);
        }

        // Then - 응답을 정상적으로 받았고, 연결이 유지됨
        Assert.IsTrue(responseReceived.IsSet, "Callback should be invoked");
        Assert.NotNull(receivedResponse, "Should receive response packet");
        Assert.IsTrue(Connector.IsConnected(), "Connection should be maintained after auto dispose");

        // 추가 요청이 정상적으로 동작하는지 확인
        var echoRequest2 = new EchoRequest { Content = "Second Request", Sequence = 2 };
        using var packet2 = new Packet(echoRequest2);
        using var response2 = await Connector.RequestAsync(packet2);

        Assert.NotNull(response2, "Additional request should work after auto dispose");
        Assert.StringContains(response2.MsgId, "EchoReply", "Response should be EchoReply");
    }

    private async Task Test_Request_MultipleCallbacks_AutoDispose()
    {
        // Given
        var responseCount = 0;
        var allReceived = new ManualResetEventSlim(false);
        var expectedCount = 5;

        // When - 여러 Callback 요청
        for (int i = 0; i < expectedCount; i++)
        {
            var request = new EchoRequest { Content = $"Message {i}", Sequence = i };
            using var packet = new Packet(request);

            Connector.Request(packet, response =>
            {
                var reply = EchoReply.Parser.ParseFrom(response.Payload.DataSpan);
                Assert.StringContains(reply.Content, "Message", "Response content should contain 'Message'");

                if (System.Threading.Interlocked.Increment(ref responseCount) == expectedCount)
                {
                    allReceived.Set();
                }
            });
        }

        // Wait for all callbacks
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!allReceived.IsSet && DateTime.UtcNow < timeout)
        {
            Connector.MainThreadAction();
            await Task.Delay(50);
        }

        // Then
        Assert.Equals(expectedCount, responseCount, $"Should receive {expectedCount} responses");
        Assert.IsTrue(Connector.IsConnected(), "Connection should be maintained after all auto disposes");
    }

    private async Task Test_OnReceive_AutoDispose()
    {
        // Given
        _receivedMessages.Clear();

        // When - 여러 요청 전송
        for (int i = 0; i < 3; i++)
        {
            var echoRequest = new EchoRequest { Content = $"Test {i}", Sequence = i };
            using var packet = new Packet(echoRequest);
            await Connector.RequestAsync(packet);
        }

        await Task.Delay(200);

        // Consume callbacks
        for (int i = 0; i < 5; i++)
        {
            Connector.MainThreadAction();
            await Task.Delay(50);
        }

        // Then - 연결 유지 및 추가 요청 가능
        Assert.IsTrue(Connector.IsConnected(), "Connection should be maintained after message reception");

        // 추가 요청이 정상적으로 동작하는지 확인
        var finalRequest = new EchoRequest { Content = "After Messages", Sequence = 99 };
        using var finalPacket = new Packet(finalRequest);
        using var echoResponse = await Connector.RequestAsync(finalPacket);

        Assert.NotNull(echoResponse, "Additional request should work");
        Assert.StringContains(echoResponse.MsgId, "EchoReply", "Response should be EchoReply");
    }

    private async Task Test_RequestAsync_CallerResponsibility()
    {
        // Given
        var echoRequest = new EchoRequest { Content = "Async Test", Sequence = 10 };
        using var requestPacket = new Packet(echoRequest);

        // When - Async 패턴으로 요청 (호출자가 using으로 감싸서 dispose)
        using var response = await Connector.RequestAsync(requestPacket);

        // Then
        Assert.NotNull(response, "Should receive response packet");
        Assert.StringContains(response.MsgId, "EchoReply", "Response should be EchoReply");

        var echoReply = EchoReply.Parser.ParseFrom(response.Payload.DataSpan);
        Assert.Equals(echoReply.Content, "Async Test", "Echo content should match");
        Assert.Equals(10, echoReply.Sequence, "Sequence should match");

        Assert.IsTrue(Connector.IsConnected(), "Connection should be maintained");
        // using 블록이 끝나면 자동으로 response.Dispose() 호출됨
    }

    private async Task Test_RequestAsync_Multiple_CallerResponsibility()
    {
        // Given & When - 여러 Async 요청, 각각 using으로 감싸서 dispose
        for (int i = 0; i < 5; i++)
        {
            var request = new EchoRequest { Content = $"Async {i}", Sequence = i };
            using var requestPacket = new Packet(request);

            // 호출자가 using으로 감싸서 dispose
            using var response = await Connector.RequestAsync(requestPacket);

            // Then - 각 응답 검증
            Assert.NotNull(response, $"Should receive response {i}");
            Assert.StringContains(response.MsgId, "EchoReply", "Response should be EchoReply");

            var reply = EchoReply.Parser.ParseFrom(response.Payload.DataSpan);
            Assert.Equals($"Async {i}", reply.Content, $"Response {i} content should match");
            Assert.Equals(i, reply.Sequence, $"Sequence {i} should match");
        }

        // Then - 모든 요청 후에도 연결 유지
        Assert.IsTrue(Connector.IsConnected(), "Connection should be maintained after all requests");
    }

    private async Task Test_Mixed_AsyncAndCallback()
    {
        // When - Async 패턴
        var asyncRequest = new EchoRequest { Content = "Async Mixed", Sequence = 100 };
        using var asyncPacket = new Packet(asyncRequest);
        using var asyncResponse = await Connector.RequestAsync(asyncPacket);

        Assert.StringContains(asyncResponse.MsgId, "EchoReply", "Async response should be EchoReply");

        // When - Callback 패턴
        var callbackRequest = new EchoRequest { Content = "Callback Mixed", Sequence = 200 };
        using var callbackPacket = new Packet(callbackRequest);

        var callbackReceived = new ManualResetEventSlim(false);
        Connector.Request(callbackPacket, response =>
        {
            Assert.StringContains(response.MsgId, "EchoReply", "Callback response should be EchoReply");
            callbackReceived.Set();
        });

        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!callbackReceived.IsSet && DateTime.UtcNow < timeout)
        {
            Connector.MainThreadAction();
            await Task.Delay(50);
        }

        // Then
        Assert.IsTrue(callbackReceived.IsSet, "Callback should be invoked");
        Assert.IsTrue(Connector.IsConnected(), "Connection should be maintained after mixed pattern usage");
    }

    /// <summary>
    /// OnDispatch 내 RequestToApi 호출 시 응답 패킷 자동 Dispose 검증
    /// </summary>
    private async Task Test_OnDispatch_RequestToApi_AutoDispose()
    {
        // Given
        var request = new TriggerAutoDisposeApiRequest { Query = "api_test_query" };
        using var packet = new Packet(request);

        // When - OnDispatch 내에서 RequestToApi 호출 트리거
        using var response = await Connector.RequestAsync(packet);

        // Then - E2E 검증: 응답이 정상적으로 전달됨 = 자동 dispose가 정상 동작함
        Assert.StringContains(response.MsgId, "TriggerAutoDisposeApiReply",
            "Should receive response from OnDispatch RequestToApi");

        var reply = TriggerAutoDisposeApiReply.Parser.ParseFrom(response.Payload.DataSpan);
        Assert.StringContains(reply.ApiResponse, "api_test_query",
            "API response should contain query content");

        // 추가 검증: 연결이 유지되고 후속 요청이 가능함
        Assert.IsTrue(Connector.IsConnected(),
            "Connection should be maintained after OnDispatch RequestToApi");
    }

    /// <summary>
    /// OnDispatch 내 RequestToStage 호출 시 응답 패킷 자동 Dispose 검증
    /// </summary>
    private async Task Test_OnDispatch_RequestToStage_AutoDispose()
    {
        // Given - 새 Connector로 두 번째 Stage 생성
        var connector2 = new PlayHouse.Connector.Connector();
        connector2.Init(new PlayHouse.Connector.ConnectorConfig { RequestTimeoutMs = 30000 });
        await connector2.ConnectAsync("127.0.0.1", ServerContext.TcpPort);
        var connector2UserId = GenerateUniqueUserId("packet_autodispose_stage2");
        var authRequest2 = new AuthenticateRequest
        {
            UserId = connector2UserId,
            Token = "single-stage"
        };
        using var authPacket2 = new Packet(authRequest2);
        await connector2.AuthenticateAsync(authPacket2);
        var stageId2 = $"single:SingleStage:{connector2UserId}";

        try
        {
            var request = new TriggerAutoDisposeStageRequest
            {
                TargetNid = "play-1",
                TargetStageId = stageId2,
                Query = "stage_test_query"
            };
            using var packet = new Packet(request);

            // When - OnDispatch 내에서 RequestToStage 호출 트리거
            using var response = await Connector.RequestAsync(packet);

            // Then - E2E 검증: Stage 간 통신 결과가 정상적으로 전달됨
            Assert.StringContains(response.MsgId, "TriggerAutoDisposeStageReply",
                "Should receive response from OnDispatch RequestToStage");

            var reply = TriggerAutoDisposeStageReply.Parser.ParseFrom(response.Payload.DataSpan);
            Assert.StringContains(reply.Response, "stage_test_query",
                "Stage response should contain query content");

            Assert.IsTrue(Connector.IsConnected(),
                "Connection should be maintained after OnDispatch RequestToStage");
        }
        finally
        {
            await connector2.DisposeAsync();
        }
    }

    /// <summary>
    /// Timer 콜백 내 RequestAsync 호출 시 응답 패킷 자동 Dispose 검증
    /// </summary>
    private async Task Test_TimerCallback_RequestAsync_AutoDispose()
    {
        // Given
        _receivedMessages.Clear();
        var request = new StartTimerWithRequestRequest { DelayMs = 200 };
        using var packet = new Packet(request);

        // When - Timer 시작 (Timer 콜백 내에서 RequestToApi 호출)
        using var response = await Connector.RequestAsync(packet);

        // Then - 1차 검증: Timer 시작 확인
        Assert.StringContains(response.MsgId, "StartTimerWithRequestReply",
            "Should receive timer start confirmation");

        var startReply = StartTimerWithRequestReply.Parser.ParseFrom(response.Payload.DataSpan);
        Assert.IsTrue(startReply.TimerId > 0, "Timer ID should be valid");

        // Timer 실행 대기 (DelayMs + 버퍼)
        await Task.Delay(600);

        // MainThreadAction 호출로 Push 메시지 수신
        for (int i = 0; i < 10; i++)
        {
            Connector.MainThreadAction();
            await Task.Delay(50);
        }

        // Then - 2차 검증: Timer 콜백 내 RequestAsync 결과를 Push 메시지로 수신
        var timerResults = _receivedMessages
            .Where(m => m.msgId.EndsWith("TimerRequestResultNotify"))
            .ToList();

        Assert.IsTrue(timerResults.Count >= 1,
            "Should receive at least one timer request result notification");

        var resultNotify = TimerRequestResultNotify.Parser.ParseFrom(timerResults[0].payloadData);
        Assert.IsTrue(resultNotify.Success,
            "Timer request should succeed");
        Assert.StringContains(resultNotify.Result, "timer_test",
            "Timer API response should contain expected content");

        // 추가 검증: 연결이 유지됨
        Assert.IsTrue(Connector.IsConnected(),
            "Connection should be maintained after Timer RequestAsync");
    }
}
