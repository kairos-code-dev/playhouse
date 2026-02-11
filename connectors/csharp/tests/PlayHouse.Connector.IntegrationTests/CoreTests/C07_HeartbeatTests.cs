using FluentAssertions;
using Xunit;

namespace PlayHouse.Connector.IntegrationTests.CoreTests;

/// <summary>
/// C-07: Heartbeat 자동 처리 테스트
/// </summary>
/// <remarks>
/// Connector가 자동으로 Heartbeat를 전송하고,
/// 장시간 연결을 유지할 수 있는지 검증합니다.
/// </remarks>
public class C07_HeartbeatTests : BaseIntegrationTest
{
    public C07_HeartbeatTests(TestServerFixture testServer) : base(testServer)
    {
    }

    [Fact(DisplayName = "C-07-01: 연결 후 장시간 유지되어도 연결이 끊어지지 않는다")]
    public async Task Connection_MaintainedOverTime_StaysConnected()
    {
        // Given: 연결 및 인증 완료
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("heartbeatUser");

        Connector!.IsConnected().Should().BeTrue("초기 연결 상태는 true");

        // When: 5초 동안 아무 동작 없이 대기 (Heartbeat가 자동으로 전송될 것임)
        await Task.Delay(5000);

        // Then: 연결이 유지되어야 함
        Connector.IsConnected().Should().BeTrue("5초 후에도 연결이 유지되어야 함");
        Connector.IsAuthenticated().Should().BeTrue("인증 상태도 유지되어야 함");
    }

    [Fact(DisplayName = "C-07-02: Heartbeat 주기 동안 Echo 요청이 정상 동작한다")]
    public async Task EchoRequest_DuringHeartbeatPeriod_WorksCorrectly()
    {
        // Given: 연결 및 인증 완료
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("echoHeartbeatUser");

        // When: 2초 대기 후 Echo 요청
        await Task.Delay(2000);
        var echoReply = await EchoAsync("After Heartbeat", 1);

        // Then: Echo가 정상 동작해야 함
        echoReply.Should().NotBeNull();
        echoReply.Content.Should().Be("After Heartbeat");

        // 연결도 유지되어야 함
        Connector!.IsConnected().Should().BeTrue();
    }

    [Fact(DisplayName = "C-07-03: 짧은 Heartbeat 간격으로 설정해도 정상 동작한다")]
    public async Task Heartbeat_WithShortInterval_WorksCorrectly()
    {
        // Given: 짧은 Heartbeat 간격 (1초) 설정
        Connector = new PlayHouse.Connector.Connector();
        Connector.Init(new ConnectorConfig
        {
            RequestTimeoutMs = 5000,
            HeartBeatIntervalMs = 1000 // 1초마다 Heartbeat
        });

        // When: 연결 및 인증
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("shortHeartbeatUser");

        // 3초 대기 (3번의 Heartbeat가 전송될 것임)
        await Task.Delay(3000);

        // Then: 연결이 유지되어야 함
        Connector.IsConnected().Should().BeTrue("짧은 Heartbeat 간격에도 연결이 유지되어야 함");

        // Echo도 정상 동작해야 함
        var echoReply = await EchoAsync("Short Interval Test", 1);
        echoReply.Content.Should().Be("Short Interval Test");
    }

    [Fact(DisplayName = "C-07-04: Heartbeat 중에도 메시지 송수신이 정상 동작한다")]
    public async Task MessageTransmission_DuringHeartbeat_WorksCorrectly()
    {
        // Given: 연결 및 인증 완료
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("transmitUser");

        // When: 10초 동안 주기적으로 Echo 요청 (Heartbeat와 동시에)
        for (int i = 1; i <= 5; i++)
        {
            var echoReply = await EchoAsync($"Message {i}", i);
            echoReply.Content.Should().Be($"Message {i}", $"{i}번째 메시지가 정상 전송되어야 함");

            await Task.Delay(2000); // 2초마다 전송
        }

        // Then: 모든 메시지가 정상 전송되고 연결이 유지되어야 함
        Connector!.IsConnected().Should().BeTrue("10초 후에도 연결이 유지되어야 함");
    }

    [Fact(DisplayName = "C-07-05: 연결 유지 중 OnDisconnect 이벤트가 발생하지 않는다")]
    public async Task OnDisconnect_DuringNormalOperation_DoesNotTrigger()
    {
        // Given: 연결 및 인증 완료
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("noDisconnectUser");

        var disconnectTriggered = false;
        Connector!.OnDisconnect += () => disconnectTriggered = true;

        // When: 5초 동안 대기
        await Task.Delay(5000);

        // Then: OnDisconnect가 발생하지 않아야 함
        disconnectTriggered.Should().BeFalse("정상 동작 중에는 OnDisconnect가 발생하지 않아야 함");
        Connector.IsConnected().Should().BeTrue("연결이 유지되어야 함");
    }

    [Fact(DisplayName = "C-07-06: 여러 Connector가 동시에 Heartbeat를 유지할 수 있다")]
    public async Task MultipleConnectors_MaintainHeartbeat_Simultaneously()
    {
        // Given: 3개의 Connector 생성 및 연결
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

            await connector1.ConnectAsync(TestServer.Host, TestServer.TcpPort);
            await connector2.ConnectAsync(TestServer.Host, TestServer.TcpPort);
            await connector3.ConnectAsync(TestServer.Host, TestServer.TcpPort);

            // 모두 인증
            var auth1 = new PlayHouse.TestServer.Proto.AuthenticateRequest { UserId = "user1", Token = "valid_token" };
            var auth2 = new PlayHouse.TestServer.Proto.AuthenticateRequest { UserId = "user2", Token = "valid_token" };
            var auth3 = new PlayHouse.TestServer.Proto.AuthenticateRequest { UserId = "user3", Token = "valid_token" };

            using var packet1 = new PlayHouse.Connector.Protocol.Packet(auth1);
            using var packet2 = new PlayHouse.Connector.Protocol.Packet(auth2);
            using var packet3 = new PlayHouse.Connector.Protocol.Packet(auth3);

            await connector1.AuthenticateAsync(packet1);
            await connector2.AuthenticateAsync(packet2);
            await connector3.AuthenticateAsync(packet3);

            // When: 5초 대기 (모든 Connector의 Heartbeat가 동작)
            await Task.Delay(5000);

            // Then: 모든 Connector의 연결이 유지되어야 함
            connector1.IsConnected().Should().BeTrue("Connector 1의 연결이 유지되어야 함");
            connector2.IsConnected().Should().BeTrue("Connector 2의 연결이 유지되어야 함");
            connector3.IsConnected().Should().BeTrue("Connector 3의 연결이 유지되어야 함");
        }
        finally
        {
            await connector1.DisposeAsync();
            await connector2.DisposeAsync();
            await connector3.DisposeAsync();
        }
    }
}
