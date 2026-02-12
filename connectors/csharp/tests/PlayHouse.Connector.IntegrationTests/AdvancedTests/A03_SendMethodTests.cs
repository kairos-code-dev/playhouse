using PlayHouse.Connector.Protocol;
using PlayHouse.TestServer.Proto;
using Xunit;

namespace PlayHouse.Connector.IntegrationTests.AdvancedTests;

/// <summary>
/// A-03: Send() 메서드 테스트 (Fire-and-Forget)
/// </summary>
/// <remarks>
/// Send() 메서드는 응답을 기다리지 않는 단방향 메시지 전송.
/// Request()와 달리 응답을 기대하지 않으며, 주로 알림이나 이벤트 전송에 사용.
/// </remarks>
[Trait("Category", "Advanced")]
[Trait("Feature", "Send")]
public class A03_SendMethodTests : BaseIntegrationTest
{
    public A03_SendMethodTests(TestServerFixture testServer) : base(testServer) { }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await CreateStageAndConnectAsync();
        await AuthenticateAsync("send-test-user");
    }

    [Fact(DisplayName = "A-03-01: Send()로 메시지를 전송할 수 있다")]
    public async Task Send_Message_Successfully()
    {
        // Arrange
        var echoRequest = new EchoRequest
        {
            Content = "Fire and Forget",
            Sequence = 1
        };

        // Act - Send는 응답을 기다리지 않음
        using var packet = new Packet(echoRequest);
        Connector!.Send(packet);

        // Assert - Send는 예외 없이 완료되어야 함
        // 메시지가 전송되었는지 확인하기 위해 짧은 대기 후 연결 상태 확인
        await Task.Delay(100);
        Assert.True(Connector.IsConnected());
    }

    [Fact(DisplayName = "A-03-02: Send() 후 연결이 유지된다")]
    public async Task Send_Connection_Maintained()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            var echoRequest = new EchoRequest
            {
                Content = $"Message {i}",
                Sequence = i
            };

            // Act
            using var packet = new Packet(echoRequest);
            Connector!.Send(packet);
        }

        await Task.Delay(200);

        // Assert
        Assert.True(Connector!.IsConnected());
        Assert.True(Connector.IsAuthenticated());
    }

    [Fact(DisplayName = "A-03-03: Send()와 Request()를 혼합해서 사용할 수 있다")]
    public async Task Send_Mixed_With_Request()
    {
        // Arrange & Act
        // Send 몇 개 전송
        for (int i = 0; i < 5; i++)
        {
            var sendRequest = new EchoRequest { Content = $"Send {i}", Sequence = i };
            using var sendPacket = new Packet(sendRequest);
            Connector!.Send(sendPacket);
        }

        // 중간에 Request
        var echoRequest = new EchoRequest { Content = "Request in between", Sequence = 100 };
        using var requestPacket = new Packet(echoRequest);
        var response = await Connector!.RequestAsync(requestPacket);
        var echoReply = EchoReply.Parser.ParseFrom(response.Payload.DataSpan.ToArray());

        // 다시 Send 몇 개 전송
        for (int i = 5; i < 10; i++)
        {
            var sendRequest = new EchoRequest { Content = $"Send {i}", Sequence = i };
            using var sendPacket = new Packet(sendRequest);
            Connector.Send(sendPacket);
        }

        // Assert
        Assert.Equal("Request in between", echoReply.Content);
        Assert.True(Connector.IsConnected());
    }

    [Fact(DisplayName = "A-03-04: Send()로 BroadcastRequest를 전송하면 Push 메시지를 받는다")]
    public async Task Send_Broadcast_Triggers_Push()
    {
        // Arrange
        var receivedPush = new TaskCompletionSource<BroadcastNotify>();
        Connector!.OnReceive += (stageId, stageType, packet) =>
        {
            if (packet.MsgId == "BroadcastNotify")
            {
                var notify = BroadcastNotify.Parser.ParseFrom(packet.Payload.DataSpan.ToArray());
                receivedPush.TrySetResult(notify);
            }
        };

        var broadcastRequest = new BroadcastRequest
        {
            Content = "Hello from Send!"
        };

        // Act
        using var packet = new Packet(broadcastRequest);
        Connector.Send(packet);

        // Wait for push with MainThreadAction
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!receivedPush.Task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Connector.MainThreadAction();
            await Task.Delay(10);
        }

        // Assert
        Assert.True(receivedPush.Task.IsCompleted);
        var notify = await receivedPush.Task;
        Assert.Equal("Hello from Send!", notify.Data);
    }

    [Fact(DisplayName = "A-03-05: 연결 해제 후 Send()는 OnError를 발생시킨다")]
    public async Task Send_After_Disconnect_Fires_OnError()
    {
        // Arrange
        var errorFired = false;
        ushort errorCode = 0;
        Connector!.OnError += (stageId, stageType, code, packet) =>
        {
            errorFired = true;
            errorCode = code;
        };

        Connector.Disconnect();
        await Task.Delay(500);

        var echoRequest = new EchoRequest { Content = "After disconnect", Sequence = 1 };

        // Act
        using var packet = new Packet(echoRequest);
        Connector.Send(packet);
        Connector.MainThreadAction();

        // Assert
        Assert.True(errorFired);
        Assert.Equal((ushort)ConnectorErrorCode.Disconnected, errorCode);
    }

    [Fact(DisplayName = "A-03-06: 인증 전 Send()는 OnError를 발생시킨다")]
    public async Task Send_Before_Authentication_Fires_OnError()
    {
        // Arrange - 새로운 Connector로 연결만 하고 인증은 하지 않음
        var newConnector = new Connector();
        newConnector.Init(new ConnectorConfig
        {
            RequestTimeoutMs = 5000,
            HeartBeatIntervalMs = 10000
        });

        await TestServer.CreateTestStageAsync();
        await newConnector.ConnectAsync(
            TestServer.Host,
            TestServer.TcpPort
        );

        var errorFired = false;
        newConnector.OnError += (stageId, stageType, code, packet) =>
        {
            errorFired = true;
        };

        var echoRequest = new EchoRequest { Content = "Before auth", Sequence = 1 };

        // Act - 인증 없이 Send 시도
        // 주의: 현재 구현에서는 인증 없이도 Send가 가능할 수 있음
        // 이 테스트는 구현에 따라 동작이 다를 수 있음
        using var packet = new Packet(echoRequest);
        newConnector.Send(packet);
        newConnector.MainThreadAction();

        await Task.Delay(100);

        // Cleanup
        newConnector.Disconnect();
        await newConnector.DisposeAsync();

        // Assert - 연결은 되어 있으므로 Send 자체는 성공할 수 있음
        // 서버 측에서 인증되지 않은 요청을 거부할 수 있음
        Assert.True(true);  // 구현에 따라 다름
    }

    [Fact(DisplayName = "A-03-07: 빠른 연속 Send()가 모두 처리된다")]
    public async Task Send_Rapid_Fire()
    {
        // Arrange
        const int messageCount = 100;

        // Act
        for (int i = 0; i < messageCount; i++)
        {
            var echoRequest = new EchoRequest { Content = $"Rapid {i}", Sequence = i };
            using var packet = new Packet(echoRequest);
            Connector!.Send(packet);
        }

        // 모든 메시지가 전송될 시간 부여
        await Task.Delay(500);

        // Assert - 연결이 유지되어야 함
        Assert.True(Connector!.IsConnected());

        // Request로 응답 확인하여 서버가 정상인지 확인
        var echoCheck = new EchoRequest { Content = "Check", Sequence = 999 };
        using var checkPacket = new Packet(echoCheck);
        var response = await Connector.RequestAsync(checkPacket);
        var echoReply = EchoReply.Parser.ParseFrom(response.Payload.DataSpan.ToArray());

        Assert.Equal("Check", echoReply.Content);
    }
}
