using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.E2E.Shared.Proto;

namespace PlayHouse.E2E.Verifiers;

/// <summary>
/// Stage-to-Stage 통신 검증 (같은 서버 내 Stage 간 통신만)
///
/// 참고: StageToStageTests.cs (line 278-362)
/// - 두 개의 Connector 인스턴스 사용 (_connectorA, _connectorB)
/// - 둘 다 같은 ServerContext.PlayServer에 연결
/// - TriggerSendToStageRequest, TriggerRequestToStageRequest 메시지 사용
/// - 크로스 서버 테스트 제외 (PlayServer가 하나뿐이므로)
/// </summary>
public class StageToStageVerifier : VerifierBase
{
    public override string CategoryName => "StageToStage";

    private PlayHouse.Connector.Connector? _connectorA;
    private PlayHouse.Connector.Connector? _connectorB;
    private readonly List<(long stageId, string stageType, string msgId, byte[] payloadData)> _receivedMessagesA = new();
    private readonly List<(long stageId, string stageType, string msgId, byte[] payloadData)> _receivedMessagesB = new();

    public StageToStageVerifier(ServerContext serverContext) : base(serverContext)
    {
    }

    public override int GetTestCount() => 5;

    protected override async Task SetupAsync()
    {
        // ✅ 두 커넥터 초기화
        _connectorA = new PlayHouse.Connector.Connector();
        _connectorB = new PlayHouse.Connector.Connector();

        // ✅ OnReceive 콜백 설정
        _connectorA.OnReceive += (stageId, stageType, packet) =>
        {
            var msgId = packet.MsgId;
            var payloadData = packet.Payload.DataSpan.ToArray();
            _receivedMessagesA.Add((stageId, stageType, msgId, payloadData));
        };

        _connectorB.OnReceive += (stageId, stageType, packet) =>
        {
            var msgId = packet.MsgId;
            var payloadData = packet.Payload.DataSpan.ToArray();
            _receivedMessagesB.Add((stageId, stageType, msgId, payloadData));
        };

        await Task.CompletedTask;
    }

    protected override async Task TeardownAsync()
    {
        // ✅ Connector 정리만
        if (_connectorA != null)
        {
            _connectorA.Disconnect();
            await _connectorA.DisposeAsync();
        }

        if (_connectorB != null)
        {
            _connectorB.Disconnect();
            await _connectorB.DisposeAsync();
        }

        // ❌ 서버 종료 금지!
    }

    protected override async Task RunTestsAsync()
    {
        await RunTest("SendToStage_SameServer", Test_SendToStage_SameServer);
        await RunTest("RequestToStage_SameServer_Async", Test_RequestToStage_SameServer_Async);
        await RunTest("RequestToStage_SameServer_Callback", Test_RequestToStage_SameServer_Callback);
        await RunTest("SendToStage_MessageDelivered", Test_SendToStage_MessageDelivered);
        await RunTest("RequestToStage_ResponseReceived", Test_RequestToStage_ResponseReceived);
    }

    /// <summary>
    /// SendToStage (같은 서버) - Stage A → Stage B 단방향 메시지 전송
    /// </summary>
    private async Task Test_SendToStage_SameServer()
    {
        // Given - 같은 서버에 두 개의 Stage 연결
        await ConnectAndAuthenticateAsync(_connectorA!);
        await ConnectAndAuthenticateAsync(_connectorB!);
        var stageIdB = _connectorB!.StageId;

        // When - Stage A에서 같은 서버의 Stage B로 SendToStage
        var request = new TriggerSendToStageRequest
        {
            TargetNid = ServerContext.PlayServerId, // 같은 서버
            TargetStageId = stageIdB,
            Message = "Hello from Stage A (same server)"
        };
        using var packet = new Packet(request);
        var response = await _connectorA!.RequestAsync(packet);

        await Task.Delay(500); // 비동기 처리 대기

        // Then - E2E 검증: 응답 검증
        Assert.Equals("TriggerSendToStageReply", response.MsgId, "Should receive TriggerSendToStageReply");
        var reply = TriggerSendToStageReply.Parser.ParseFrom(response.Payload.DataSpan);
        Assert.IsTrue(reply.Success, "SendToStage should succeed");

        // Cleanup
        _connectorA!.Disconnect();
        _connectorB!.Disconnect();
        await Task.Delay(100);
    }

    /// <summary>
    /// RequestToStage (같은 서버, async) - Stage A → Stage B 요청/응답
    /// </summary>
    private async Task Test_RequestToStage_SameServer_Async()
    {
        // Given - 같은 서버에 두 개의 Stage 연결
        await ConnectAndAuthenticateAsync(_connectorA!);
        await ConnectAndAuthenticateAsync(_connectorB!);
        var stageIdB = _connectorB!.StageId;

        // When - Stage A에서 같은 서버의 Stage B로 RequestToStage
        var request = new TriggerRequestToStageRequest
        {
            TargetNid = ServerContext.PlayServerId, // 같은 서버
            TargetStageId = stageIdB,
            Query = "Query from Stage A (same server)"
        };
        using var packet = new Packet(request);
        var response = await _connectorA!.RequestAsync(packet);

        // Then - E2E 검증: 응답 검증
        Assert.Equals("TriggerRequestToStageReply", response.MsgId, "Should receive TriggerRequestToStageReply");
        var reply = TriggerRequestToStageReply.Parser.ParseFrom(response.Payload.DataSpan);
        Assert.IsTrue(reply.Response.Contains("Query from Stage A (same server)"), "Response should contain the query");

        // Cleanup
        _connectorA!.Disconnect();
        _connectorB!.Disconnect();
        await Task.Delay(100);
    }

    /// <summary>
    /// RequestToStage (같은 서버, callback) - Callback 버전 요청/응답
    /// </summary>
    private async Task Test_RequestToStage_SameServer_Callback()
    {
        // Given - 같은 서버에 두 개의 Stage 연결
        await ConnectAndAuthenticateAsync(_connectorA!);
        await ConnectAndAuthenticateAsync(_connectorB!);
        var stageIdB = _connectorB!.StageId;

        _receivedMessagesA.Clear();

        // When - Stage A에서 Stage B로 RequestToStage Callback 버전 트리거
        var request = new TriggerRequestToStageCallbackRequest
        {
            TargetNid = ServerContext.PlayServerId, // 같은 서버
            TargetStageId = stageIdB,
            Query = "Callback Query from Stage A"
        };
        using var packet = new Packet(request);
        var response = await _connectorA!.RequestAsync(packet);

        // Then - E2E 검증 1: 즉시 수락 응답
        Assert.IsTrue(response.MsgId.Contains("Accepted"), "Should receive Accepted response");

        // Callback이 실행되고 Push 메시지가 도착할 시간 대기
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (_receivedMessagesA.Count == 0 && DateTime.UtcNow < timeout)
        {
            _connectorA!.MainThreadAction();
            _connectorB!.MainThreadAction();
            await Task.Delay(50);
        }

        // Then - E2E 검증 2: Push 메시지 수신 (callback에서 SendToClient로 전송)
        Assert.IsTrue(_receivedMessagesA.Count > 0, "Should receive push messages");

        var pushMessage = _receivedMessagesA.FirstOrDefault(m => m.msgId.Contains("TriggerRequestToStageCallbackReply"));
        Assert.IsTrue(!pushMessage.Equals(default), "Should receive TriggerRequestToStageCallbackReply push message");

        var pushReply = TriggerRequestToStageCallbackReply.Parser.ParseFrom(pushMessage.payloadData);
        Assert.IsTrue(pushReply.Response.Contains("Callback Query from Stage A"),
            "Response should contain the callback query");

        // Cleanup
        _connectorA!.Disconnect();
        _connectorB!.Disconnect();
        await Task.Delay(100);
    }

    /// <summary>
    /// SendToStage - 메시지 전달 확인
    /// </summary>
    private async Task Test_SendToStage_MessageDelivered()
    {
        // Given
        await ConnectAndAuthenticateAsync(_connectorA!);
        await ConnectAndAuthenticateAsync(_connectorB!);
        var stageIdB = _connectorB!.StageId;

        // When
        var request = new TriggerSendToStageRequest
        {
            TargetNid = ServerContext.PlayServerId,
            TargetStageId = stageIdB,
            Message = "Message delivery test"
        };
        using var packet = new Packet(request);
        var response = await _connectorA!.RequestAsync(packet);

        await Task.Delay(500);

        // Then
        Assert.Equals("TriggerSendToStageReply", response.MsgId, "Should receive reply");
        var reply = TriggerSendToStageReply.Parser.ParseFrom(response.Payload.DataSpan);
        Assert.IsTrue(reply.Success, "Message delivery should succeed");

        // Cleanup
        _connectorA!.Disconnect();
        _connectorB!.Disconnect();
        await Task.Delay(100);
    }

    /// <summary>
    /// RequestToStage - 응답 수신 확인
    /// </summary>
    private async Task Test_RequestToStage_ResponseReceived()
    {
        // Given
        await ConnectAndAuthenticateAsync(_connectorA!);
        await ConnectAndAuthenticateAsync(_connectorB!);
        var stageIdB = _connectorB!.StageId;

        // When
        var request = new TriggerRequestToStageRequest
        {
            TargetNid = ServerContext.PlayServerId,
            TargetStageId = stageIdB,
            Query = "Response test query"
        };
        using var packet = new Packet(request);
        var response = await _connectorA!.RequestAsync(packet);

        // Then
        Assert.Equals("TriggerRequestToStageReply", response.MsgId, "Should receive reply");
        var reply = TriggerRequestToStageReply.Parser.ParseFrom(response.Payload.DataSpan);
        Assert.IsTrue(reply.Response.Contains("Response test query"), "Response should contain query");

        // Cleanup
        _connectorA!.Disconnect();
        _connectorB!.Disconnect();
        await Task.Delay(100);
    }

    #region Helper Methods

    private async Task ConnectAndAuthenticateAsync(PlayHouse.Connector.Connector connector)
    {
        connector.Init(new ConnectorConfig { RequestTimeoutMs = 30000 });
        var connected = await connector.ConnectAsync("127.0.0.1", ServerContext.TcpPort);
        Assert.IsTrue(connected, "Should connect to server");
        await Task.Delay(100);

        using var authPacket = Packet.Empty("AuthenticateRequest");
        await connector.AuthenticateAsync(authPacket);
        Assert.IsTrue(connector.StageId > 0, "Authenticated connector should have a valid stage id");
        await Task.Delay(100);
    }

    #endregion
}
