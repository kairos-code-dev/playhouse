using FluentAssertions;
using PlayHouse.Connector.Protocol;
using PlayHouse.TestServer.Proto;
using Xunit;

namespace PlayHouse.Connector.IntegrationTests.CoreTests;

/// <summary>
/// C-11: 에러 응답 테스트
/// </summary>
/// <remarks>
/// 서버가 에러 응답을 보내는 경우를 검증합니다.
/// FailRequest를 보내면 서버가 의도적으로 에러 응답을 반환합니다.
/// </remarks>
public class C11_ErrorResponseTests : BaseIntegrationTest
{
    public C11_ErrorResponseTests(TestServerFixture testServer) : base(testServer)
    {
    }

    [Fact(DisplayName = "C-11-01: 서버 에러 응답을 받을 수 있다")]
    public async Task RequestAsync_WithFailRequest_ReceivesErrorResponse()
    {
        // Given: 연결 및 인증 완료
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("errorUser");

        var failRequest = new FailRequest
        {
            ErrorCode = 1000,
            ErrorMessage = "Test Error"
        };

        // When: 에러를 발생시키는 요청 전송
        using var requestPacket = new Packet(failRequest);
        var responsePacket = await Connector!.RequestAsync(requestPacket);

        // Then: 에러 응답을 받아야 함
        responsePacket.Should().NotBeNull();
        responsePacket.MsgId.Should().Be("FailReply", "에러 응답 메시지 타입이어야 함");

        var failReply = ParsePayload<FailReply>(responsePacket.Payload);
        failReply.ErrorCode.Should().Be(1000, "요청한 에러 코드가 반환되어야 함");
        failReply.Message.Should().Contain("Test Error", "에러 메시지가 포함되어야 함");
    }

    [Fact(DisplayName = "C-11-02: 다양한 에러 코드를 처리할 수 있다")]
    public async Task ErrorResponse_WithDifferentErrorCodes_HandlesCorrectly()
    {
        // Given: 연결 및 인증 완료
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("multiErrorUser");

        var errorCodes = new[] { 1000, 1001, 1002, 1003, 1004 };

        // When: 다양한 에러 코드로 요청
        foreach (var errorCode in errorCodes)
        {
            var failRequest = new FailRequest
            {
                ErrorCode = errorCode,
                ErrorMessage = $"Error {errorCode}"
            };

            using var requestPacket = new Packet(failRequest);
            var responsePacket = await Connector!.RequestAsync(requestPacket);

            // Then: 각 에러 코드가 올바르게 반환되어야 함
            var failReply = ParsePayload<FailReply>(responsePacket.Payload);
            failReply.ErrorCode.Should().Be(errorCode, $"에러 코드 {errorCode}가 반환되어야 함");
            failReply.Message.Should().Contain($"Error {errorCode}", "에러 메시지가 일치해야 함");
        }
    }

    [Fact(DisplayName = "C-11-03: 에러 응답 후에도 연결은 유지된다")]
    public async Task Connection_AfterErrorResponse_RemainsConnected()
    {
        // Given: 연결 및 인증 완료
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("connectionErrorUser");

        var failRequest = new FailRequest
        {
            ErrorCode = 1000,
            ErrorMessage = "Connection Test Error"
        };

        // When: 에러 응답 받기
        using var requestPacket = new Packet(failRequest);
        await Connector!.RequestAsync(requestPacket);

        // Then: 연결과 인증 상태가 유지되어야 함
        Connector.IsConnected().Should().BeTrue("에러 응답 후에도 연결이 유지되어야 함");
        Connector.IsAuthenticated().Should().BeTrue("인증 상태도 유지되어야 함");

        // 다른 요청도 정상 동작해야 함
        var echoReply = await EchoAsync("After Error", 1);
        echoReply.Content.Should().Be("After Error", "에러 후 정상 요청이 동작해야 함");
    }

    [Fact(DisplayName = "C-11-04: 콜백 방식도 에러 응답을 처리할 수 있다")]
    public async Task Request_WithCallback_HandlesErrorResponse()
    {
        // Given: 연결 및 인증 완료
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("callbackErrorUser");

        var failRequest = new FailRequest
        {
            ErrorCode = 2000,
            ErrorMessage = "Callback Error Test"
        };

        var tcs = new TaskCompletionSource<FailReply>();

        // When: 콜백 방식으로 에러 요청
        using var requestPacket = new Packet(failRequest);
        Connector!.Request(requestPacket, responsePacket =>
        {
            var failReply = ParsePayload<FailReply>(responsePacket.Payload);
            tcs.TrySetResult(failReply);
        });

        // 콜백 대기 (MainThreadAction 호출하면서 최대 5초)
        FailReply failReply;
        try
        {
            failReply = await WaitWithMainThreadActionAsync(tcs, 5000);
        }
        catch (TimeoutException)
        {
            true.Should().BeFalse("콜백이 호출되어야 함");
            return;
        }

        // Then: 콜백이 호출되고 에러 정보를 받아야 함
        failReply.ErrorCode.Should().Be(2000);
        failReply.Message.Should().Contain("Callback Error Test");
    }

    [Fact(DisplayName = "C-11-05: 에러 응답과 정상 응답을 섞어서 처리할 수 있다")]
    public async Task MixedRequests_ErrorAndSuccess_BothHandled()
    {
        // Given: 연결 및 인증 완료
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("mixedUser");

        // When: 정상 요청과 에러 요청을 번갈아 보냄
        var echo1 = await EchoAsync("Success 1", 1);
        echo1.Content.Should().Be("Success 1");

        var failRequest1 = new FailRequest { ErrorCode = 3001, ErrorMessage = "Error 1" };
        using var failPacket1 = new Packet(failRequest1);
        var failResponse1 = await Connector!.RequestAsync(failPacket1);
        var failReply1 = ParsePayload<FailReply>(failResponse1.Payload);
        failReply1.ErrorCode.Should().Be(3001);

        var echo2 = await EchoAsync("Success 2", 2);
        echo2.Content.Should().Be("Success 2");

        var failRequest2 = new FailRequest { ErrorCode = 3002, ErrorMessage = "Error 2" };
        using var failPacket2 = new Packet(failRequest2);
        var failResponse2 = await Connector.RequestAsync(failPacket2);
        var failReply2 = ParsePayload<FailReply>(failResponse2.Payload);
        failReply2.ErrorCode.Should().Be(3002);

        var echo3 = await EchoAsync("Success 3", 3);
        echo3.Content.Should().Be("Success 3");

        // Then: 모든 요청이 올바르게 처리되어야 함
        Connector.IsConnected().Should().BeTrue("연결이 유지되어야 함");
    }

    [Fact(DisplayName = "C-11-06: 빈 에러 메시지도 처리할 수 있다")]
    public async Task ErrorResponse_WithEmptyMessage_HandlesCorrectly()
    {
        // Given: 연결 및 인증 완료
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("emptyErrorUser");

        var failRequest = new FailRequest
        {
            ErrorCode = 4000,
            ErrorMessage = "" // 빈 에러 메시지
        };

        // When: 빈 에러 메시지로 요청
        using var requestPacket = new Packet(failRequest);
        var responsePacket = await Connector!.RequestAsync(requestPacket);

        // Then: 에러 응답을 받아야 함
        var failReply = ParsePayload<FailReply>(responsePacket.Payload);
        failReply.ErrorCode.Should().Be(4000, "에러 코드가 반환되어야 함");
        // 에러 메시지가 비어있을 수 있음
    }

    [Fact(DisplayName = "C-11-07: 여러 클라이언트가 각자 에러를 받을 수 있다")]
    public async Task MultipleClients_EachReceivesOwnError()
    {
        // Given: 2개의 Connector 생성 및 연결
        var stage1 = await TestServer.CreateTestStageAsync();
        var stage2 = await TestServer.CreateTestStageAsync();

        var connector1 = new PlayHouse.Connector.Connector();
        var connector2 = new PlayHouse.Connector.Connector();

        try
        {
            connector1.Init(new ConnectorConfig());
            connector2.Init(new ConnectorConfig());

            await connector1.ConnectAsync(TestServer.Host, TestServer.TcpPort);
            await connector2.ConnectAsync(TestServer.Host, TestServer.TcpPort);

            var auth1 = new AuthenticateRequest { UserId = "user1", Token = "valid_token" };
            var auth2 = new AuthenticateRequest { UserId = "user2", Token = "valid_token" };

            using var authPacket1 = new Packet(auth1);
            using var authPacket2 = new Packet(auth2);

            await connector1.AuthenticateAsync(authPacket1);
            await connector2.AuthenticateAsync(authPacket2);

            // When: 각 클라이언트가 다른 에러 코드로 요청
            var fail1 = new FailRequest { ErrorCode = 5001, ErrorMessage = "Error from Client 1" };
            var fail2 = new FailRequest { ErrorCode = 5002, ErrorMessage = "Error from Client 2" };

            using var failPacket1 = new Packet(fail1);
            using var failPacket2 = new Packet(fail2);

            var response1 = await connector1.RequestAsync(failPacket1);
            var response2 = await connector2.RequestAsync(failPacket2);

            // Then: 각 클라이언트가 자신의 에러를 받아야 함
            var reply1 = ParsePayload<FailReply>(response1.Payload);
            var reply2 = ParsePayload<FailReply>(response2.Payload);

            reply1.ErrorCode.Should().Be(5001, "클라이언트 1의 에러 코드");
            reply2.ErrorCode.Should().Be(5002, "클라이언트 2의 에러 코드");

            reply1.Message.Should().Contain("Client 1");
            reply2.Message.Should().Contain("Client 2");
        }
        finally
        {
            await connector1.DisposeAsync();
            await connector2.DisposeAsync();
        }
    }

    [Fact(DisplayName = "C-11-08: 에러 응답 타입이 올바르다")]
    public async Task ErrorResponse_MessageType_IsCorrect()
    {
        // Given: 연결 및 인증 완료
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("typeCheckUser");

        var failRequest = new FailRequest
        {
            ErrorCode = 6000,
            ErrorMessage = "Type Check"
        };

        // When: 에러 요청
        using var requestPacket = new Packet(failRequest);
        var responsePacket = await Connector!.RequestAsync(requestPacket);

        // Then: 응답 메시지 타입이 FailReply여야 함
        responsePacket.MsgId.Should().Be("FailReply", "에러 응답 타입이 FailReply여야 함");
    }
}
