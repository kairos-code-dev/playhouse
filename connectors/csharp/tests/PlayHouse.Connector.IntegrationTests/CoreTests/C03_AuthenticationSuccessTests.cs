using FluentAssertions;
using PlayHouse.Connector.Protocol;
using PlayHouse.TestServer.Proto;
using Xunit;

namespace PlayHouse.Connector.IntegrationTests.CoreTests;

/// <summary>
/// C-03: 인증 성공 테스트
/// </summary>
/// <remarks>
/// Connector의 Authenticate 메서드를 통해 성공적으로 인증할 수 있는지 검증합니다.
/// </remarks>
public class C03_AuthenticationSuccessTests : BaseIntegrationTest
{
    public C03_AuthenticationSuccessTests(TestServerFixture testServer) : base(testServer)
    {
    }

    [Fact(DisplayName = "C-03-01: 유효한 토큰으로 인증이 성공한다")]
    public async Task Authenticate_WithValidToken_Succeeds()
    {
        // Given: 연결된 상태
        await CreateStageAndConnectAsync();

        // When: 유효한 토큰으로 인증
        var authReply = await AuthenticateAsync("user123", "valid_token");

        // Then: 인증이 성공하고 올바른 응답을 받아야 함
        authReply.Should().NotBeNull("인증 응답이 반환되어야 함");
        authReply.Success.Should().BeTrue("인증이 성공해야 함");
        authReply.ReceivedUserId.Should().Be("user123", "전송한 User ID가 에코되어야 함");
        authReply.ReceivedToken.Should().Be("valid_token", "전송한 토큰이 에코되어야 함");
        authReply.AccountId.Should().NotBeNullOrWhiteSpace("Account ID가 할당되어야 함");

        // IsAuthenticated도 true여야 함
        Connector!.IsAuthenticated().Should().BeTrue("인증 후 IsAuthenticated는 true여야 함");
    }

    [Fact(DisplayName = "C-03-02: AuthenticateAsync로 인증할 수 있다")]
    public async Task AuthenticateAsync_WithValidCredentials_ReturnsSuccessReply()
    {
        // Given: 연결된 상태
        await CreateStageAndConnectAsync();

        var authRequest = new AuthenticateRequest
        {
            UserId = "testUser",
            Token = "valid_token"
        };

        // When: AuthenticateAsync 호출
        using var requestPacket = new Packet(authRequest);
        var responsePacket = await Connector!.AuthenticateAsync(requestPacket);

        // Then: 응답 패킷이 올바르게 반환되어야 함
        responsePacket.Should().NotBeNull();
        responsePacket.MsgId.Should().Be("AuthenticateReply", "응답 메시지 타입이 일치해야 함");

        var authReply = ParsePayload<AuthenticateReply>(responsePacket.Payload);
        authReply.Success.Should().BeTrue();
        authReply.ReceivedUserId.Should().Be("testUser");
    }

    [Fact(DisplayName = "C-03-03: Authenticate 콜백 방식으로 인증할 수 있다")]
    public async Task Authenticate_WithCallback_InvokesCallbackWithSuccess()
    {
        // Given: 연결된 상태
        await CreateStageAndConnectAsync();

        var authRequest = new AuthenticateRequest
        {
            UserId = "callbackUser",
            Token = "valid_token"
        };

        var tcs = new TaskCompletionSource<AuthenticateReply>();

        // When: Authenticate 콜백 방식 호출
        using var requestPacket = new Packet(authRequest);
        Connector!.Authenticate(requestPacket, responsePacket =>
        {
            var authReply = ParsePayload<AuthenticateReply>(responsePacket.Payload);
            tcs.TrySetResult(authReply);
        });

        // 콜백 대기 (MainThreadAction 호출하면서 최대 5초)
        AuthenticateReply authReply;
        try
        {
            authReply = await WaitWithMainThreadActionAsync(tcs, 5000);
        }
        catch (TimeoutException)
        {
            true.Should().BeFalse("콜백이 5초 이내에 호출되어야 함");
            return;
        }

        // Then: 콜백이 호출되고 성공 응답을 받아야 함
        authReply.Success.Should().BeTrue();
        authReply.ReceivedUserId.Should().Be("callbackUser");
    }

    [Fact(DisplayName = "C-03-04: 메타데이터와 함께 인증할 수 있다")]
    public async Task Authenticate_WithMetadata_SucceedsAndEchoesMetadata()
    {
        // Given: 연결된 상태
        await CreateStageAndConnectAsync();

        var authRequest = new AuthenticateRequest
        {
            UserId = "metaUser",
            Token = "valid_token"
        };
        authRequest.Metadata.Add("client_version", "1.0.0");
        authRequest.Metadata.Add("platform", "dotnet");

        // When: 메타데이터와 함께 인증
        using var requestPacket = new Packet(authRequest);
        var responsePacket = await Connector!.AuthenticateAsync(requestPacket);

        // Then: 인증이 성공해야 함
        var authReply = ParsePayload<AuthenticateReply>(responsePacket.Payload);
        authReply.Success.Should().BeTrue("메타데이터가 있어도 인증이 성공해야 함");
        authReply.ReceivedUserId.Should().Be("metaUser");
    }

    [Fact(DisplayName = "C-03-05: 인증 성공 후 AccountId가 할당된다")]
    public async Task Authenticate_Success_AssignsAccountId()
    {
        // Given: 연결된 상태
        await CreateStageAndConnectAsync();

        // When: 인증 성공
        var authReply = await AuthenticateAsync("user_with_account_id");

        // Then: AccountId가 할당되어야 함
        authReply.AccountId.Should().NotBeNullOrWhiteSpace("AccountId가 할당되어야 함");
        authReply.AccountId.Should().NotBe("0", "AccountId는 유효한 값이어야 함");
    }

    [Fact(DisplayName = "C-03-06: 여러 유저가 동시에 인증할 수 있다")]
    public async Task Authenticate_MultipleUsers_AllSucceed()
    {
        // Given: 3개의 Stage와 Connector 생성
        var stage1 = await TestServer.CreateTestStageAsync();
        var stage2 = await TestServer.CreateTestStageAsync();
        var stage3 = await TestServer.CreateTestStageAsync();

        var connector1 = new PlayHouse.Connector.Connector();
        var connector2 = new PlayHouse.Connector.Connector();
        var connector3 = new PlayHouse.Connector.Connector();

        try
        {
            connector1.Init(new ConnectorConfig());
            connector2.Init(new ConnectorConfig());
            connector3.Init(new ConnectorConfig());

            // When: 3개의 Connector가 각각 연결 및 인증
            await connector1.ConnectAsync(TestServer.Host, TestServer.TcpPort);
            await connector2.ConnectAsync(TestServer.Host, TestServer.TcpPort);
            await connector3.ConnectAsync(TestServer.Host, TestServer.TcpPort);

            var auth1Request = new AuthenticateRequest { UserId = "user1", Token = "valid_token" };
            var auth2Request = new AuthenticateRequest { UserId = "user2", Token = "valid_token" };
            var auth3Request = new AuthenticateRequest { UserId = "user3", Token = "valid_token" };

            using var packet1 = new Packet(auth1Request);
            using var packet2 = new Packet(auth2Request);
            using var packet3 = new Packet(auth3Request);

            var reply1Packet = await connector1.AuthenticateAsync(packet1);
            var reply2Packet = await connector2.AuthenticateAsync(packet2);
            var reply3Packet = await connector3.AuthenticateAsync(packet3);

            var reply1 = ParsePayload<AuthenticateReply>(reply1Packet.Payload);
            var reply2 = ParsePayload<AuthenticateReply>(reply2Packet.Payload);
            var reply3 = ParsePayload<AuthenticateReply>(reply3Packet.Payload);

            // Then: 모든 인증이 성공해야 함
            reply1.Success.Should().BeTrue();
            reply2.Success.Should().BeTrue();
            reply3.Success.Should().BeTrue();

            reply1.ReceivedUserId.Should().Be("user1");
            reply2.ReceivedUserId.Should().Be("user2");
            reply3.ReceivedUserId.Should().Be("user3");

            // 각자 고유한 AccountId를 가져야 함
            reply1.AccountId.Should().NotBe(reply2.AccountId);
            reply2.AccountId.Should().NotBe(reply3.AccountId);
            reply1.AccountId.Should().NotBe(reply3.AccountId);
        }
        finally
        {
            // 정리
            await connector1.DisposeAsync();
            await connector2.DisposeAsync();
            await connector3.DisposeAsync();
        }
    }
}
