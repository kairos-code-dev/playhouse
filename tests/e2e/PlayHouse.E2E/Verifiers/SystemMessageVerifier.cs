using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.Core.Shared;
using PlayHouse.E2E.Shared.Infrastructure;
using PlayHouse.E2E.Shared.Proto;
using PlayHouse.Extensions.Proto;

namespace PlayHouse.E2E.Verifiers;

/// <summary>
/// 시스템 메시지 (SendToSystem/RequestToSystem) 검증
///
/// 테스트 시나리오:
/// - Stage → API Server (SendToSystem)
/// - Stage → Play Server (동일 서버)
/// - API Server → Play Server (SendToSystem)
/// - API Server → API Server (동일 서버)
/// - API Server → API Server (다른 서버)
///
/// 참고: TestSystemController.ReceivedSystemMessages로 수신 메시지 검증
/// </summary>
public class SystemMessageVerifier : VerifierBase
{
    public override string CategoryName => "SystemMessage";

    private PlayHouse.Connector.Connector? _connector;

    public SystemMessageVerifier(ServerContext serverContext) : base(serverContext)
    {
    }

    public override int GetTestCount() => 5;

    protected override async Task SetupAsync()
    {
        // 이전 테스트의 시스템 메시지 기록 초기화
        TestSystemController.ResetSystemMessages();

        // Connector 초기화
        _connector = new PlayHouse.Connector.Connector();

        // ServerAddressResolver 연결 대기
        await Task.Delay(1000);
    }

    protected override async Task TeardownAsync()
    {
        // 테스트 후 메시지 기록 초기화
        TestSystemController.ResetSystemMessages();

        // Connector 정리
        if (_connector != null)
        {
            _connector.Disconnect();
            await _connector.DisposeAsync();
        }
    }

    protected override async Task RunTestsAsync()
    {
        await RunTest("SendToSystem_StageToApi", Test_SendToSystem_StageToApi);
        await RunTest("SendToSystem_StageToPlay_SameServer", Test_SendToSystem_StageToPlay_SameServer);
        await RunTest("SendToSystem_ApiToPlay", Test_SendToSystem_ApiToPlay);
        await RunTest("SendToSystem_ApiToApi_SameServer", Test_SendToSystem_ApiToApi_SameServer);
        await RunTest("SendToSystem_ApiToApi_DifferentServer", Test_SendToSystem_ApiToApi_DifferentServer);
    }

    /// <summary>
    /// SendToSystem - Stage → API Server
    /// </summary>
    private async Task Test_SendToSystem_StageToApi()
    {
        // Given
        TestSystemController.ResetSystemMessages();
        var testMessage = $"StageToApi_{Guid.NewGuid():N}";
        var stageId = GenerateUniqueStageId(9000);

        // Connector 연결 및 Stage 참여
        await ConnectAndAuthenticateAsync(_connector!, stageId);

        // When - Stage에서 API로 시스템 메시지 전송 트리거
        var triggerRequest = new TriggerSendToSystemApiRequest
        {
            TargetApiNid = ServerContext.ApiServer1Id,
            Message = testMessage
        };

        using var packet = new Packet(triggerRequest);
        var responsePacket = await _connector!.RequestAsync(packet);

        // 메시지 전달 대기
        await Task.Delay(500);

        // Then - 응답 확인
        Assert.Equals("TriggerSendToSystemApiReply", responsePacket.MsgId, "Should receive trigger reply");
        var reply = TriggerSendToSystemApiReply.Parser.ParseFrom(responsePacket.Payload.DataSpan);
        Assert.IsTrue(reply.Success, "Trigger should succeed");

        // Then - 시스템 메시지 수신 확인
        var receivedMessages = TestSystemController.ReceivedSystemMessages;
        var matchingMessage = receivedMessages.FirstOrDefault(m => m.Content == testMessage);
        Assert.NotNull(matchingMessage, $"Should receive system message with content: {testMessage}");
        Assert.Equals("play-1", matchingMessage!.FromServerId, "Message should be from play-1");

        // Cleanup
        _connector!.Disconnect();
        await Task.Delay(100);
    }

    /// <summary>
    /// SendToSystem - Stage → Play Server (동일 서버)
    /// </summary>
    private async Task Test_SendToSystem_StageToPlay_SameServer()
    {
        // Given
        TestSystemController.ResetSystemMessages();
        var testMessage = $"StageToPlay_{Guid.NewGuid():N}";
        var stageId = GenerateUniqueStageId(9100);

        // Connector 연결 및 Stage 참여
        await ConnectAndAuthenticateAsync(_connector!, stageId);

        // When - Stage에서 동일 Play 서버로 시스템 메시지 전송 트리거
        var triggerRequest = new TriggerSendToSystemPlayRequest
        {
            TargetPlayNid = ServerContext.PlayServerId,
            Message = testMessage
        };

        using var packet = new Packet(triggerRequest);
        var responsePacket = await _connector!.RequestAsync(packet);

        // 메시지 전달 대기
        await Task.Delay(500);

        // Then - 응답 확인
        Assert.Equals("TriggerSendToSystemPlayReply", responsePacket.MsgId, "Should receive trigger reply");
        var reply = TriggerSendToSystemPlayReply.Parser.ParseFrom(responsePacket.Payload.DataSpan);
        Assert.IsTrue(reply.Success, "Trigger should succeed");

        // Then - 시스템 메시지 수신 확인
        var receivedMessages = TestSystemController.ReceivedSystemMessages;
        var matchingMessage = receivedMessages.FirstOrDefault(m => m.Content == testMessage);
        Assert.NotNull(matchingMessage, $"Should receive system message with content: {testMessage}");
        Assert.Equals("play-1", matchingMessage!.FromServerId, "Message should be from play-1");

        // Cleanup
        _connector!.Disconnect();
        await Task.Delay(100);
    }

    /// <summary>
    /// SendToSystem - API Server → Play Server
    /// </summary>
    private async Task Test_SendToSystem_ApiToPlay()
    {
        // Given
        TestSystemController.ResetSystemMessages();
        var testMessage = $"ApiToPlay_{Guid.NewGuid():N}";

        // When - API에서 Play로 시스템 메시지 전송 트리거
        var triggerRequest = new TriggerApiSendToSystemPlayRequest
        {
            TargetPlayNid = ServerContext.PlayServerId,
            Message = testMessage
        };

        var responsePacket = await ApiServer1.ApiLink!.RequestToApi(
            ServerContext.ApiServer1Id,
            ProtoCPacketExtensions.OfProto(triggerRequest));

        // 메시지 전달 대기
        await Task.Delay(500);

        // Then - 응답 확인
        Assert.Equals("TriggerApiSendToSystemPlayReply", responsePacket.MsgId, "Should receive trigger reply");
        var reply = TriggerApiSendToSystemPlayReply.Parser.ParseFrom(responsePacket.Payload.DataSpan);
        Assert.IsTrue(reply.Success, "Trigger should succeed");

        // Then - 시스템 메시지 수신 확인
        var receivedMessages = TestSystemController.ReceivedSystemMessages;
        var matchingMessage = receivedMessages.FirstOrDefault(m => m.Content == testMessage);
        Assert.NotNull(matchingMessage, $"Should receive system message with content: {testMessage}");
        Assert.Equals("api-1", matchingMessage!.FromServerId, "Message should be from api-1");
    }

    /// <summary>
    /// SendToSystem - API Server → API Server (동일 서버)
    /// </summary>
    private async Task Test_SendToSystem_ApiToApi_SameServer()
    {
        // Given
        TestSystemController.ResetSystemMessages();
        var testMessage = $"ApiToApiSame_{Guid.NewGuid():N}";

        // When - API에서 동일 API로 시스템 메시지 전송 트리거
        var triggerRequest = new TriggerApiSendToSystemApiRequest
        {
            TargetApiNid = ServerContext.ApiServer1Id,  // 자기 자신
            Message = testMessage
        };

        var responsePacket = await ApiServer1.ApiLink!.RequestToApi(
            ServerContext.ApiServer1Id,
            ProtoCPacketExtensions.OfProto(triggerRequest));

        // 메시지 전달 대기
        await Task.Delay(500);

        // Then - 응답 확인
        Assert.Equals("TriggerApiSendToSystemApiReply", responsePacket.MsgId, "Should receive trigger reply");
        var reply = TriggerApiSendToSystemApiReply.Parser.ParseFrom(responsePacket.Payload.DataSpan);
        Assert.IsTrue(reply.Success, "Trigger should succeed");

        // Then - 시스템 메시지 수신 확인
        var receivedMessages = TestSystemController.ReceivedSystemMessages;
        var matchingMessage = receivedMessages.FirstOrDefault(m => m.Content == testMessage);
        Assert.NotNull(matchingMessage, $"Should receive system message with content: {testMessage}");
        Assert.Equals("api-1", matchingMessage!.FromServerId, "Message should be from api-1");
    }

    /// <summary>
    /// SendToSystem - API Server → API Server (다른 서버)
    /// </summary>
    private async Task Test_SendToSystem_ApiToApi_DifferentServer()
    {
        // Given
        TestSystemController.ResetSystemMessages();
        var testMessage = $"ApiToApiDiff_{Guid.NewGuid():N}";

        // When - API1에서 API2로 시스템 메시지 전송 트리거
        var triggerRequest = new TriggerApiSendToSystemApiRequest
        {
            TargetApiNid = ServerContext.ApiServer2Id,  // 다른 API 서버
            Message = testMessage
        };

        var responsePacket = await ApiServer1.ApiLink!.RequestToApi(
            ServerContext.ApiServer1Id,
            ProtoCPacketExtensions.OfProto(triggerRequest));

        // 메시지 전달 대기
        await Task.Delay(500);

        // Then - 응답 확인
        Assert.Equals("TriggerApiSendToSystemApiReply", responsePacket.MsgId, "Should receive trigger reply");
        var reply = TriggerApiSendToSystemApiReply.Parser.ParseFrom(responsePacket.Payload.DataSpan);
        Assert.IsTrue(reply.Success, "Trigger should succeed");

        // Then - 시스템 메시지 수신 확인
        var receivedMessages = TestSystemController.ReceivedSystemMessages;
        var matchingMessage = receivedMessages.FirstOrDefault(m => m.Content == testMessage);
        Assert.NotNull(matchingMessage, $"Should receive system message with content: {testMessage}");
        Assert.Equals("api-1", matchingMessage!.FromServerId, "Message should be from api-1");
    }

    #region Helper Methods

    /// <summary>
    /// Connector를 연결하고 인증/Stage 참여합니다.
    /// </summary>
    private async Task ConnectAndAuthenticateAsync(PlayHouse.Connector.Connector connector, long stageId)
    {
        connector.Init(new ConnectorConfig { RequestTimeoutMs = 30000 });
        var connected = await connector.ConnectAsync("127.0.0.1", ServerContext.TcpPort);
        Assert.IsTrue(connected, $"Should connect to server (stageId: {stageId})");
        await Task.Delay(100);

        using var authPacket = Packet.Empty("AuthenticateRequest");
        await connector.AuthenticateAsync(authPacket);
        await Task.Delay(100);
    }

    #endregion
}
