using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;
using PlayHouse.E2E.Shared.Proto;

namespace PlayHouse.E2E.Verifiers;


/// <summary>
/// IActorSender 메서드 검증
/// </summary>
public class ActorSenderVerifier : VerifierBase
{
    public override string CategoryName => "ActorSender";

    public ActorSenderVerifier(ServerContext serverContext) : base(serverContext)
    {
    }

    public override int GetTestCount() => 4;

    protected override async Task SetupAsync()
    {
        // 기존 연결 해제
        if (Connector.IsConnected())
        {
            Connector.Disconnect();
            await Task.Delay(100);
        }
    }

    protected override Task TeardownAsync()
    {
        // 연결 정리
        if (Connector.IsConnected())
        {
            Connector.Disconnect();
        }

        return Task.CompletedTask;
    }

    protected override async Task RunTestsAsync()
    {
        await RunTest("AccountId_NotEmpty", Test_AccountId_NotEmpty);
        await RunTest("LeaveStage_Success", Test_LeaveStage_Success);
        await RunTest("Reply_EchoMessage", Test_Reply_EchoMessage);
        await RunTest("Reply_ErrorCode", Test_Reply_ErrorCode);
    }

    /// <summary>
    /// AccountId != null && != ""
    /// 인증 후 서버가 자동으로 AccountId 할당
    /// </summary>
    private async Task Test_AccountId_NotEmpty()
    {
        // Given - 서버에 연결 및 인증
        await ConnectToServerAsync();

        // When - AccountId 조회
        using var request = Packet.Empty("GetAccountIdRequest");
        var response = await Connector.RequestAsync(request);

        // Then - E2E 검증: 응답 검증
        Assert.Equals("GetAccountIdReply", response.MsgId, "Should receive GetAccountIdReply");
        var reply = GetAccountIdReply.Parser.ParseFrom(response.Payload.DataSpan);
        Assert.NotNull(reply.AccountId, "AccountId should not be null");
        Assert.IsTrue(reply.AccountId.Length > 0, "AccountId should not be empty");

        // Cleanup
        Connector.Disconnect();
        await Task.Delay(100);
    }

    /// <summary>
    /// LeaveStageRequest → LeaveStageReply.Success == true
    /// </summary>
    private async Task Test_LeaveStage_Success()
    {
        // Given - 서버에 연결 및 인증
        await ConnectToServerAsync();

        // When - LeaveStageRequest
        var leaveRequest = new LeaveStageRequest { Reason = "Test" };
        using var packet = new Packet(leaveRequest);
        var response = await Connector.RequestAsync(packet);

        // Then - E2E 검증: 응답 검증
        Assert.Equals("LeaveStageReply", response.MsgId, "Should receive LeaveStageReply");
        var reply = LeaveStageReply.Parser.ParseFrom(response.Payload.DataSpan);
        Assert.IsTrue(reply.Success, "LeaveStage should succeed");

        // Cleanup
        Connector.Disconnect();
        await Task.Delay(100);
    }

    /// <summary>
    /// EchoRequest → EchoReply (Reply 메서드)
    /// </summary>
    private async Task Test_Reply_EchoMessage()
    {
        // Given - 서버에 연결 및 인증
        await ConnectToServerAsync();

        // When - EchoRequest
        var echoRequest = new EchoRequest { Content = "Reply Test", Sequence = 99 };
        using var packet = new Packet(echoRequest);
        var response = await Connector.RequestAsync(packet);

        // Then - E2E 검증: 응답 검증
        Assert.Equals("EchoReply", response.MsgId, "Should receive EchoReply");
        var reply = EchoReply.Parser.ParseFrom(response.Payload.DataSpan);
        Assert.Equals("Reply Test", reply.Content, "Echo content should match");
        Assert.Equals(99, reply.Sequence, "Sequence should match");

        // Cleanup
        Connector.Disconnect();
        await Task.Delay(100);
    }

    /// <summary>
    /// FailRequest → ErrorPacket (ConnectorException)
    /// </summary>
    private async Task Test_Reply_ErrorCode()
    {
        // Given - 서버에 연결 및 인증
        await ConnectToServerAsync();

        // When - FailRequest (서버에서 에러 응답)
        using var failPacket = Packet.Empty("FailRequest");
        ConnectorException? caughtException = null;

        try
        {
            var response = await Connector.RequestAsync(failPacket);
            // 예외가 발생해야 하는데 발생하지 않으면 실패
            Assert.IsTrue(false, "Should throw ConnectorException");
        }
        catch (ConnectorException ex)
        {
            caughtException = ex;
        }

        // Then - E2E 검증: 에러 응답 검증
        Assert.NotNull(caughtException, "Should throw ConnectorException");
        Assert.Equals(500, caughtException!.ErrorCode, "ErrorCode should be 500");

        // Cleanup
        Connector.Disconnect();
        await Task.Delay(100);
    }

    #region Helper Methods

    private async Task ConnectOnlyAsync()
    {
        var userId = GenerateUniqueUserId("actor_sender");
        var stageId = GenerateUniqueStageId();
        var connected = await Connector.ConnectAsync("127.0.0.1", ServerContext.TcpPort);
        Assert.IsTrue(connected, "Should connect to server");
        await Task.Delay(100);
    }

    private async Task ConnectToServerAsync()
    {
        await ConnectOnlyAsync();

        if (!Connector.IsAuthenticated())
        {
            using var authPacket = Packet.Empty("AuthenticateRequest");
            await Connector.AuthenticateAsync(authPacket);
            await Task.Delay(100);
        }
    }

    #endregion
}
