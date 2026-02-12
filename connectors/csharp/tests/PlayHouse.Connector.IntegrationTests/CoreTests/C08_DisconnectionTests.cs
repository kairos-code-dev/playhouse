using FluentAssertions;
using Xunit;

namespace PlayHouse.Connector.IntegrationTests.CoreTests;

/// <summary>
/// C-08: 연결 해제 테스트
/// </summary>
/// <remarks>
/// Connector의 Disconnect 메서드를 통해 정상적으로 연결을 끊을 수 있는지,
/// 연결 해제 후 상태가 올바른지 검증합니다.
/// </remarks>
public class C08_DisconnectionTests : BaseIntegrationTest
{
    public C08_DisconnectionTests(TestServerFixture testServer) : base(testServer)
    {
    }

    [Fact(DisplayName = "C-08-01: Disconnect를 호출하면 연결이 해제된다")]
    public async Task Disconnect_AfterConnection_DisconnectsSuccessfully()
    {
        // Given: 연결 및 인증 완료
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("disconnectUser");

        Connector!.IsConnected().Should().BeTrue("초기 상태는 연결됨");
        Connector.IsAuthenticated().Should().BeTrue("인증도 완료됨");

        // When: 연결 해제
        Connector.Disconnect();
        await Task.Delay(500); // 연결 해제 완료 대기

        // Then: 연결이 끊어져야 함
        Connector.IsConnected().Should().BeFalse("Disconnect 후 연결이 끊어져야 함");
        Connector.IsAuthenticated().Should().BeFalse("인증 상태도 false여야 함");
    }

    [Fact(DisplayName = "C-08-02: OnDisconnect 이벤트가 발생하지 않는다 (클라이언트에서 끊은 경우)")]
    public async Task OnDisconnect_WhenClientDisconnects_DoesNotTrigger()
    {
        // Given: 연결 및 인증 완료
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("clientDisconnectUser");

        var disconnectEventTriggered = false;
        Connector!.OnDisconnect += () => disconnectEventTriggered = true;

        // When: 클라이언트에서 연결 해제
        Connector.Disconnect();
        await Task.Delay(1000); // 이벤트 발생 대기

        // Then: OnDisconnect 이벤트가 발생하지 않아야 함 (의도적으로 끊은 경우)
        disconnectEventTriggered.Should().BeFalse("클라이언트가 의도적으로 끊은 경우 OnDisconnect가 발생하지 않아야 함");
    }

    [Fact(DisplayName = "C-08-03: 연결 해제 후 Send는 실패한다")]
    public async Task Send_AfterDisconnect_FailsWithError()
    {
        // Given: 연결 및 인증 후 연결 해제
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("sendAfterDisconnectUser");
        Connector!.Disconnect();
        await Task.Delay(500);

        ushort? receivedErrorCode = null;
        Connector.OnError += (stageId, stageType, errorCode, request) =>
        {
            receivedErrorCode = errorCode;
        };

        // When: 연결 해제 후 Send 시도
        var echoRequest = new PlayHouse.TestServer.Proto.EchoRequest { Content = "Test", Sequence = 1 };
        using var packet = new PlayHouse.Connector.Protocol.Packet(echoRequest);
        Connector.Send(packet);

        // Then: Disconnected 에러가 발생해야 함
        receivedErrorCode.Should().Be((ushort)PlayHouse.Connector.ConnectorErrorCode.Disconnected,
            "연결 해제 후 Send는 Disconnected 에러를 발생시켜야 함");
    }

    [Fact(DisplayName = "C-08-04: 연결 해제 후 RequestAsync는 예외를 발생시킨다")]
    public async Task RequestAsync_AfterDisconnect_ThrowsException()
    {
        // Given: 연결 및 인증 후 연결 해제
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("requestAfterDisconnectUser");
        Connector!.Disconnect();
        await Task.Delay(500);

        // When: 연결 해제 후 RequestAsync 시도
        var echoRequest = new PlayHouse.TestServer.Proto.EchoRequest { Content = "Test", Sequence = 1 };
        using var packet = new PlayHouse.Connector.Protocol.Packet(echoRequest);

        var action = async () => await Connector.RequestAsync(packet);

        // Then: ConnectorException이 발생해야 함
        await action.Should().ThrowAsync<PlayHouse.Connector.ConnectorException>()
            .Where(ex => ex.ErrorCode == (ushort)PlayHouse.Connector.ConnectorErrorCode.Disconnected,
                "연결 해제 후 RequestAsync는 예외를 발생시켜야 함");
    }

    [Fact(DisplayName = "C-08-05: 연결 해제 후 재연결할 수 있다")]
    public async Task Reconnect_AfterDisconnect_Succeeds()
    {
        // Given: 첫 번째 연결 및 해제
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("reconnectUser1");
        Connector!.Disconnect();
        await Task.Delay(500);

        Connector.IsConnected().Should().BeFalse("연결이 끊어진 상태");

        // When: 새로운 Stage로 재연결
        await TestServer.CreateTestStageAsync();
        var reconnected = await Connector.ConnectAsync(
            TestServer.Host,
            TestServer.TcpPort
        );

        // Then: 재연결이 성공해야 함
        reconnected.Should().BeTrue("재연결이 성공해야 함");
        Connector.IsConnected().Should().BeTrue("재연결 후 연결 상태가 true");

        // 인증도 가능해야 함
        var authReply = await AuthenticateAsync("reconnectUser2");
        authReply.Success.Should().BeTrue("재연결 후 인증이 성공해야 함");
    }

    [Fact(DisplayName = "C-08-06: 여러 번 Disconnect를 호출해도 안전하다")]
    public async Task Disconnect_MultipleTimes_IsSafe()
    {
        // Given: 연결 완료
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("multiDisconnectUser");

        // When: Disconnect를 여러 번 호출
        Connector!.Disconnect();
        await Task.Delay(200);

        var action = () =>
        {
            Connector.Disconnect();
            Connector.Disconnect();
            Connector.Disconnect();
        };

        // Then: 예외가 발생하지 않아야 함
        action.Should().NotThrow("여러 번 Disconnect를 호출해도 안전해야 함");
        Connector.IsConnected().Should().BeFalse("최종적으로 연결이 끊어져야 함");
    }

    [Fact(DisplayName = "C-08-07: DisposeAsync는 자동으로 연결을 해제한다")]
    public async Task DisposeAsync_AutomaticallyDisconnects()
    {
        // Given: 새로운 Connector 생성 및 연결
        var tempConnector = new PlayHouse.Connector.Connector();
        tempConnector.Init(new ConnectorConfig());

        var stageInfo = await TestServer.CreateTestStageAsync();
        await tempConnector.ConnectAsync(TestServer.Host, TestServer.TcpPort);

        var authRequest = new PlayHouse.TestServer.Proto.AuthenticateRequest
        {
            UserId = "disposeUser",
            Token = "valid_token"
        };
        using var authPacket = new PlayHouse.Connector.Protocol.Packet(authRequest);
        await tempConnector.AuthenticateAsync(authPacket);

        tempConnector.IsConnected().Should().BeTrue("초기 연결 상태");

        // When: DisposeAsync 호출
        await tempConnector.DisposeAsync();

        // Then: 연결이 해제되어야 함
        tempConnector.IsConnected().Should().BeFalse("DisposeAsync 후 연결이 해제되어야 함");
    }

    [Fact(DisplayName = "C-08-08: 연결 해제 후 IsAuthenticated는 false를 반환한다")]
    public async Task IsAuthenticated_AfterDisconnect_ReturnsFalse()
    {
        // Given: 연결 및 인증 완료
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("authCheckUser");

        Connector!.IsAuthenticated().Should().BeTrue("인증 완료 상태");

        // When: 연결 해제
        Connector.Disconnect();
        await Task.Delay(500);

        // Then: IsAuthenticated가 false여야 함
        Connector.IsAuthenticated().Should().BeFalse("연결 해제 후 IsAuthenticated는 false여야 함");
    }
}
