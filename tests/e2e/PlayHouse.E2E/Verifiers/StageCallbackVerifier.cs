using PlayHouse.Connector.Protocol;
using PlayHouse.E2E.Shared.Proto;

namespace PlayHouse.E2E.Verifiers;


/// <summary>
/// IStage 콜백 검증
/// </summary>
public class StageCallbackVerifier : VerifierBase
{
    public override string CategoryName => "StageCallback";

    public StageCallbackVerifier(ServerContext serverContext) : base(serverContext)
    {
    }

    public override int GetTestCount() => 5;

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
        await RunTest("OnCreate_Success", Test_OnCreate_Success);
        await RunTest("OnJoinStage_Success", Test_OnJoinStage_Success);
        await RunTest("OnPostJoinStage_Called", Test_OnPostJoinStage_Called);
        await RunTest("OnDispatch_EchoMessage", Test_OnDispatch_EchoMessage);
        await RunTest("OnDestroy_Called", Test_OnDestroy_Called);
    }

    /// <summary>
    /// Stage 생성 시 OnCreate 콜백
    /// 인증 성공 = Stage 생성 완료
    /// </summary>
    private async Task Test_OnCreate_Success()
    {
        // Given - 서버에 연결 및 인증
        await ConnectToServerAsync();

        // Then - E2E 검증: 인증 성공 = Stage 생성 및 OnCreate 호출 완료
        Assert.IsTrue(Connector.IsAuthenticated(), "Authentication triggers OnCreate");

        // OnCreate 콜백은 서버 내부에서 호출되며,
        // 클라이언트는 IsAuthenticated()로 간접적으로 확인 가능
        // (OnCreate 실패 시 Stage 생성 실패)

        // Cleanup
        Connector.Disconnect();
        await Task.Delay(100);
    }

    /// <summary>
    /// Actor Join 시 OnJoinStage 콜백
    /// 인증 성공 = Actor가 Stage에 Join 완료
    /// </summary>
    private async Task Test_OnJoinStage_Success()
    {
        // Given - 서버에 연결 및 인증 (자동으로 Stage에 Join)
        await ConnectToServerAsync();

        // Then - E2E 검증: 인증 성공 = OnJoinStage 호출 완료
        Assert.IsTrue(Connector.IsAuthenticated(), "Authentication triggers OnJoinStage");

        // OnJoinStage 콜백은 서버 내부에서 호출되며,
        // 클라이언트는 IsAuthenticated()로 간접적으로 확인 가능
        // (OnJoinStage 실패 시 Join 실패하여 인증도 실패)

        // Cleanup
        Connector.Disconnect();
        await Task.Delay(100);
    }

    /// <summary>
    /// Actor Join 후 OnPostJoinStage 콜백
    /// 인증 성공 후 메시지 처리 가능 = OnPostJoinStage 호출 완료
    /// </summary>
    private async Task Test_OnPostJoinStage_Called()
    {
        // Given - 서버에 연결 및 인증
        await ConnectToServerAsync();

        // When - 메시지 전송 (OnPostJoinStage 이후에만 처리 가능)
        var echoRequest = new EchoRequest { Content = "Test", Sequence = 1 };
        using var packet = new Packet(echoRequest);
        var response = await Connector.RequestAsync(packet);

        // Then - E2E 검증: 메시지 처리 성공 = OnPostJoinStage 호출 완료
        Assert.Equals("EchoReply", response.MsgId, "Should receive EchoReply");
        var reply = EchoReply.Parser.ParseFrom(response.Payload.DataSpan);
        Assert.Equals("Test", reply.Content, "Should receive echo response");

        // Cleanup
        Connector.Disconnect();
        await Task.Delay(100);
    }

    /// <summary>
    /// EchoRequest → EchoReply (OnDispatch 콜백)
    /// </summary>
    private async Task Test_OnDispatch_EchoMessage()
    {
        // Given - 서버에 연결 및 인증
        await ConnectToServerAsync();

        // When - EchoRequest 전송
        var echoRequest = new EchoRequest { Content = "Stage echo", Sequence = 42 };
        using var packet = new Packet(echoRequest);
        var response = await Connector.RequestAsync(packet);

        // Then - E2E 검증: 응답 검증
        Assert.Equals("EchoReply", response.MsgId, "Should receive EchoReply");
        var reply = EchoReply.Parser.ParseFrom(response.Payload.DataSpan);
        Assert.Equals("Stage echo", reply.Content, "Echo content should match");
        Assert.Equals(42, reply.Sequence, "Sequence should match");

        // OnDispatch 콜백은 서버 내부에서 호출되며,
        // 클라이언트는 응답 수신으로 간접적으로 확인 가능

        // Cleanup
        Connector.Disconnect();
        await Task.Delay(100);
    }

    /// <summary>
    /// Stage 종료 시 OnDestroy 콜백
    /// CloseStageRequest → CloseStageReply.Success == true
    /// </summary>
    private async Task Test_OnDestroy_Called()
    {
        // Given - 서버에 연결 및 인증
        await ConnectToServerAsync();

        // When - CloseStageRequest 전송
        var closeRequest = new CloseStageRequest { Reason = "Test" };
        using var packet = new Packet(closeRequest);
        var response = await Connector.RequestAsync(packet);

        // 콜백 처리 대기
        await Task.Delay(200);

        // Then - E2E 검증: 응답 검증
        Assert.Equals("CloseStageReply", response.MsgId, "Should receive CloseStageReply");
        var reply = CloseStageReply.Parser.ParseFrom(response.Payload.DataSpan);
        Assert.IsTrue(reply.Success, "Stage close should succeed");

        // CloseStageReply.Success == true = OnDestroy 호출 완료

        // Cleanup
        Connector.Disconnect();
        await Task.Delay(100);
    }

    #region Helper Methods

    private async Task ConnectOnlyAsync()
    {
        var stageId = GenerateUniqueStageId();
        var connected = await Connector.ConnectAsync("127.0.0.1", ServerContext.TcpPort);
        Assert.IsTrue(connected, "Should connect to server");
        await Task.Delay(100);
    }

    private async Task ConnectToServerAsync()
    {
        await ConnectOnlyAsync();

        using var authPacket = Packet.Empty("AuthenticateRequest");
        await Connector.AuthenticateAsync(authPacket);
        await Task.Delay(100);
    }

    #endregion
}
