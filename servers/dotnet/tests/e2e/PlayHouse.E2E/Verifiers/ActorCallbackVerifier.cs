using PlayHouse.Connector.Protocol;
using PlayHouse.E2E.Shared.Proto;

namespace PlayHouse.E2E.Verifiers;


/// <summary>
/// IActor 콜백 검증
/// </summary>
public class ActorCallbackVerifier(ServerContext serverContext) : VerifierBase(serverContext)
{
    public override string CategoryName => "ActorCallback";

    public override int GetTestCount() => 3;

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
        await RunTest("OnAuthenticate_Success", Test_OnAuthenticate_Success);
        await RunTest("OnPostAuthenticate_Called", Test_OnPostAuthenticate_Called);
        await RunTest("OnCreate_Called", Test_OnCreate_Called);
    }

    /// <summary>
    /// OnAuthenticate 콜백 후 IsAuthenticated() == true
    /// authPacket이 OnAuthenticate에 정상 전달되는지 검증 (reply에서 echo된 값 확인)
    /// </summary>
    private async Task Test_OnAuthenticate_Success()
    {
        // Given - 연결만 된 상태
        await ConnectOnlyAsync();
        Assert.IsFalse(Connector.IsAuthenticated(), "Should not be authenticated yet");

        // When - proto 메시지로 인증 요청
        var authRequest = new AuthenticateRequest
        {
            UserId = "test-user-123",
            Token = "test-token-abc"
        };
        using var authPacket = new Packet(authRequest);
        var response = await Connector.AuthenticateAsync(authPacket);

        // Then - E2E 검증: 응답 검증
        Assert.NotNull(response, "Should receive authentication response");
        Assert.IsTrue(Connector.IsAuthenticated(), "Should be authenticated after OnAuthenticate");

        // OnAuthenticate에 authPacket이 정상 전달되었는지 검증 (reply에서 echo된 값 확인)
        Assert.IsTrue(response.Payload.Length > 0, "OnAuthenticate should return reply packet");
        var authReply = AuthenticateReply.Parser.ParseFrom(response.Payload.DataSpan);
        Assert.IsTrue(authReply.Success, "AuthenticateReply.Success should be true");
        Assert.Equals("test-user-123", authReply.ReceivedUserId, "OnAuthenticate should receive authPacket.UserId");
        Assert.Equals("test-token-abc", authReply.ReceivedToken, "OnAuthenticate should receive authPacket.Token");

        // Cleanup
        Connector.Disconnect();
        await Task.Delay(100);
    }

    /// <summary>
    /// OnPostAuthenticate 콜백 (인증 후)
    /// 인증이 성공하면 OnPostAuthenticate가 호출됨
    /// </summary>
    private async Task Test_OnPostAuthenticate_Called()
    {
        // Given - 연결만 된 상태
        await ConnectOnlyAsync();

        // When - 인증 성공
        using var authPacket = Packet.Empty("AuthenticateRequest");
        await Connector.AuthenticateAsync(authPacket);

        // Then - E2E 검증: 응답 검증
        Assert.IsTrue(Connector.IsAuthenticated(), "Authentication should succeed");

        // OnPostAuthenticate 콜백은 서버 내부에서 호출되며,
        // 클라이언트는 IsAuthenticated()로 간접적으로 확인 가능
        // (OnPostAuthenticate 실패 시 인증이 취소되므로)

        // Cleanup
        Connector.Disconnect();
        await Task.Delay(100);
    }

    /// <summary>
    /// OnCreate 콜백 (Actor 생성)
    /// Stage 가입 시 Actor가 생성되고 OnCreate 호출
    /// </summary>
    private async Task Test_OnCreate_Called()
    {
        // Given - 서버에 연결 및 인증 (자동으로 Stage에 Join)
        await ConnectToServerAsync();

        // Then - E2E 검증: 인증 성공 = Actor 생성 완료
        Assert.IsTrue(Connector.IsAuthenticated(), "Actor should be created and authenticated");

        // OnCreate 콜백은 서버 내부에서 호출되며,
        // 클라이언트는 IsAuthenticated()로 간접적으로 확인 가능
        // (OnCreate 실패 시 Actor 생성이 실패하여 인증도 실패)

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
