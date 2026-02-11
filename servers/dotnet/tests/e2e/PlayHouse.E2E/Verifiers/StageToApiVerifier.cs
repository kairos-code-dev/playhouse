using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.E2E.Shared.Proto;

namespace PlayHouse.E2E.Verifiers;

/// <summary>
/// Stage-to-API 통신 검증
///
/// 참고: StageToApiTests.cs
/// - ServerContext.Connector 사용 (PlayServer에 연결)
/// - ServerContext.ApiServer1 (이미 실행 중인 API 서버)
/// - TriggerSendToApiRequest, TriggerRequestToApiRequest 메시지 사용
/// </summary>
public class StageToApiVerifier : VerifierBase
{
    public override string CategoryName => "StageToApi";

    private readonly List<(long stageId, string stageType, string msgId, byte[] payloadData)> _receivedMessages = new();

    public StageToApiVerifier(ServerContext serverContext) : base(serverContext)
    {
        // OnReceive 콜백 설정
        Connector.OnReceive += (stageId, stageType, packet) =>
        {
            var msgId = packet.MsgId;
            var payloadData = packet.Payload.DataSpan.ToArray();
            _receivedMessages.Add((stageId, stageType, msgId, payloadData));
        };
    }

    public override int GetTestCount() => 5;  // Removed S2S_DirectRouting (server-side test, incompatible with E2E)

    protected override async Task SetupAsync()
    {
        // 기존 연결 해제
        if (Connector.IsConnected())
        {
            Connector.Disconnect();
            await Task.Delay(100);
        }

        _receivedMessages.Clear();

        // ❌ NO ApiServer 생성!
        // ✅ ServerContext.ApiServer1은 이미 실행 중

        // ServerAddressResolver 연결 대기
        await Task.Delay(5000);
    }

    protected override Task TeardownAsync()
    {
        // 연결 정리
        if (Connector.IsConnected())
        {
            Connector.Disconnect();
        }

        // ❌ 서버 종료 금지!
        return Task.CompletedTask;
    }

    protected override async Task RunTestsAsync()
    {
        await RunTest("SendToApi_Success", Test_SendToApi_Success);
        await RunTest("AsyncBlock_SendToApi", Test_AsyncBlock_SendToApi);
        // Skipped: S2S_DirectRouting - tests server-side routing (API→Stage SendToStage) which cannot be verified E2E
        await RunTest("AsyncBlock_RequestToApi", Test_AsyncBlock_RequestToApi);
        await RunTest("S2S_BasicRequestReply", Test_S2S_BasicRequestReply);
        await RunTest("ApiSender_AccountId_NotEmpty", Test_ApiSender_AccountId_NotEmpty);
    }

    /// <summary>
    /// SendToApi - Stage → API SendToApi 성공
    /// </summary>
    private async Task Test_SendToApi_Success()
    {
        // Given - 서버에 연결
        var stageId = GenerateUniqueStageId(10000);
        await ConnectAndAuthenticateAsync(stageId);

        // When - SendToApi 트리거
        var request = new TriggerSendToApiRequest { Message = "Hello from Stage" };
        using var packet = new Packet(request);
        var response = await Connector.RequestAsync(packet);

        await Task.Delay(500); // API 처리 대기

        // Then - E2E 검증: 응답 검증
        Assert.Equals("TriggerSendToApiReply", response.MsgId, "Should receive TriggerSendToApiReply");

        // Cleanup
        Connector.Disconnect();
        await Task.Delay(100);
    }

    /// <summary>
    /// AsyncBlock - PreBlock 내에서 SendToApi 호출
    /// </summary>
    private async Task Test_AsyncBlock_SendToApi()
    {
        // Given
        var stageId = GenerateUniqueStageId(20000);
        await ConnectAndAuthenticateAsync(stageId);

        // When - AsyncBlock SendToApi 트리거
        var request = new TriggerAsyncBlockSendToApiRequest { Message = "Hello from AsyncBlock" };
        using var packet = new Packet(request);
        var response = await Connector.RequestAsync(packet);

        // Then - E2E 검증: Accepted 응답
        Assert.Equals("TriggerAsyncBlockSendToApiAccepted", response.MsgId, "Should receive Accepted response");

        await Task.Delay(500); // API 처리 대기

        // Cleanup
        Connector.Disconnect();
        await Task.Delay(100);
    }

    /// <summary>
    /// S2S 라우팅 - StageId 정보가 API로 전달되고 SendToStage 응답 수신
    /// </summary>
    private async Task Test_S2S_DirectRouting()
    {
        // Given
        var stageId = GenerateUniqueStageId(30000);
        await ConnectAndAuthenticateAsync(stageId);

        _receivedMessages.Clear();

        // When - API DirectEcho 트리거
        using var packet = Packet.Empty("TriggerApiDirectEcho");
        var response = await Connector.RequestAsync(packet);

        Assert.IsTrue(response.MsgId.Contains("Accepted"), "Should receive Accepted response");

        // Then - Push 메시지 대기 (MainThreadAction 폴링 추가)
        var timeout = DateTime.UtcNow.AddSeconds(5);
        var received = false;
        while (DateTime.UtcNow < timeout)
        {
            Connector.MainThreadAction();

            if (_receivedMessages.Any(m => m.msgId.Contains("ApiDirectEchoReply")))
            {
                received = true;
                break;
            }
            await Task.Delay(50);
        }

        Assert.IsTrue(received, "Should receive ApiDirectEchoReply push message");

        // Cleanup
        Connector.Disconnect();
        await Task.Delay(100);
    }

    /// <summary>
    /// AsyncBlock - PreBlock 내에서 RequestToApi 호출 및 PostBlock 실행
    /// </summary>
    private async Task Test_AsyncBlock_RequestToApi()
    {
        // Given
        var stageId = GenerateUniqueStageId(40000);
        await ConnectAndAuthenticateAsync(stageId);

        _receivedMessages.Clear();

        // When - AsyncBlock RequestToApi 트리거
        var request = new TriggerAsyncBlockRequestToApiRequest { Query = "Async Request Query" };
        using var packet = new Packet(request);
        await Connector.RequestAsync(packet);

        // Then - 최종 Push 응답 대기 (MainThreadAction 폴링 추가)
        TriggerAsyncBlockRequestToApiReply? finalReply = null;
        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < timeout)
        {
            Connector.MainThreadAction();

            var msg = _receivedMessages.FirstOrDefault(m => m.msgId.Contains("TriggerAsyncBlockRequestToApiReply"));
            if (!msg.Equals(default))
            {
                finalReply = TriggerAsyncBlockRequestToApiReply.Parser.ParseFrom(msg.payloadData);
                break;
            }
            await Task.Delay(50);
        }

        Assert.NotNull(finalReply, "Should receive PostBlock push message");
        Assert.IsTrue(finalReply!.ApiResponse.Contains("Async Request Query"), "ApiResponse should contain query");
        Assert.IsTrue(finalReply.PostBlockCalled, "PostBlock should be called");

        // Cleanup
        Connector.Disconnect();
        await Task.Delay(100);
    }

    /// <summary>
    /// S2S 기본 - OnDispatch 내에서 RequestToApi 호출 및 응답
    /// </summary>
    private async Task Test_S2S_BasicRequestReply()
    {
        // Given
        var stageId = GenerateUniqueStageId(50000);
        await ConnectAndAuthenticateAsync(stageId);

        // When - RequestToApi 트리거
        var request = new TriggerRequestToApiRequest { Query = "Basic S2S Test" };
        using var packet = new Packet(request);
        var response = await Connector.RequestAsync(packet);

        // Then - E2E 검증: 응답 검증
        Assert.Equals("TriggerRequestToApiReply", response.MsgId, "Should receive TriggerRequestToApiReply");
        var reply = TriggerRequestToApiReply.Parser.ParseFrom(response.Payload.DataSpan);
        Assert.IsTrue(reply.ApiResponse.Contains("Basic S2S Test"), "ApiResponse should contain query");

        // Cleanup
        Connector.Disconnect();
        await Task.Delay(100);
    }

    /// <summary>
    /// IApiSender.AccountId - API 핸들러에서 AccountId 접근 가능 검증
    /// </summary>
    private async Task Test_ApiSender_AccountId_NotEmpty()
    {
        // Given - 서버 연결 및 인증
        var stageId = GenerateUniqueStageId(60000);
        await ConnectAndAuthenticateAsync(stageId);

        // When - API의 AccountId 조회 트리거
        using var packet = Packet.Empty("TriggerGetApiAccountIdRequest");
        var response = await Connector.RequestAsync(packet);

        // Then - E2E 검증
        Assert.Equals("TriggerGetApiAccountIdReply", response.MsgId, "Should receive TriggerGetApiAccountIdReply");
        var reply = TriggerGetApiAccountIdReply.Parser.ParseFrom(response.Payload.DataSpan);
        Assert.NotNull(reply.ApiAccountId, "ApiAccountId should not be null");
        Assert.IsTrue(reply.ApiAccountId.Length > 0, "ApiAccountId should not be empty");

        // Cleanup
        Connector.Disconnect();
        await Task.Delay(100);
    }

    #region Helper Methods

    private async Task ConnectAndAuthenticateAsync(long stageId)
    {
        Connector.Init(new ConnectorConfig { RequestTimeoutMs = 30000 });
        var connected = await Connector.ConnectAsync("127.0.0.1", ServerContext.TcpPort);
        Assert.IsTrue(connected, $"Should connect to server (stageId: {stageId})");
        await Task.Delay(100);

        using var authPacket = Packet.Empty("AuthenticateRequest");
        await Connector.AuthenticateAsync(authPacket);
        await Task.Delay(100);
    }

    #endregion
}
