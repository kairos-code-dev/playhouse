using PlayHouse.Connector.Protocol;
using PlayHouse.TestServer.Proto;
using Xunit;

namespace PlayHouse.Connector.IntegrationTests.AdvancedTests;

/// <summary>
/// A-01: WebSocket 연결 테스트
/// </summary>
/// <remarks>
/// WebSocket 전송 계층을 통한 연결, 인증, 메시지 송수신 테스트.
/// UseWebsocket = true 설정으로 TCP 대신 WebSocket 사용.
/// </remarks>
[Trait("Category", "Advanced")]
[Trait("Transport", "WebSocket")]
public class A01_WebSocketConnectionTests : IClassFixture<TestServerFixture>, IAsyncLifetime
{
    private readonly TestServerFixture _testServer;
    private Connector? _connector;
    private CreateStageResponse? _stageInfo;

    public A01_WebSocketConnectionTests(TestServerFixture testServer)
    {
        _testServer = testServer;
    }

    public async Task InitializeAsync()
    {
        _connector = new Connector();
        _connector.Init(new ConnectorConfig
        {
            UseWebsocket = true,
            WebSocketPath = "/ws",
            RequestTimeoutMs = 5000,
            HeartBeatIntervalMs = 10000
        });

        _stageInfo = await _testServer.CreateTestStageAsync();
    }

    public async Task DisposeAsync()
    {
        if (_connector != null)
        {
            if (_connector.IsConnected())
            {
                _connector.Disconnect();
                await Task.Delay(100);
            }
            await _connector.DisposeAsync();
        }
    }

    [Fact(DisplayName = "A-01-01: WebSocket으로 서버에 연결할 수 있다")]
    public async Task WebSocket_Connection_Success()
    {
        // Act
        var connected = await _connector!.ConnectAsync(
            _testServer.Host,
            _testServer.HttpPort  // WebSocket은 HTTP 포트 사용
        );

        // Assert
        Assert.True(connected);
        Assert.True(_connector.IsConnected());
    }

    [Fact(DisplayName = "A-01-02: WebSocket 연결 후 인증이 성공한다")]
    public async Task WebSocket_Authentication_Success()
    {
        // Arrange
        await _connector!.ConnectAsync(
            _testServer.Host,
            _testServer.HttpPort
        );

        var authRequest = new AuthenticateRequest
        {
            UserId = "ws-user-1",
            Token = "valid_token"
        };
        await _testServer.AssignStageAsync(authRequest.UserId, _stageInfo!.StageId);

        // Act
        using var requestPacket = new Packet(authRequest);
        var responsePacket = await _connector.AuthenticateAsync(requestPacket);
        var authReply = AuthenticateReply.Parser.ParseFrom(responsePacket.Payload.DataSpan.ToArray());

        // Assert
        Assert.True(authReply.Success);
        Assert.True(_connector.IsAuthenticated());
    }

    [Fact(DisplayName = "A-01-03: WebSocket으로 Echo Request-Response가 동작한다")]
    public async Task WebSocket_Echo_Request_Response()
    {
        // Arrange
        await _connector!.ConnectAsync(
            _testServer.Host,
            _testServer.HttpPort
        );

        var authRequest = new AuthenticateRequest { UserId = "ws-user-2", Token = "valid_token" };
        await _testServer.AssignStageAsync(authRequest.UserId, _stageInfo!.StageId);
        using var authPacket = new Packet(authRequest);
        await _connector.AuthenticateAsync(authPacket);

        var echoRequest = new EchoRequest
        {
            Content = "Hello WebSocket!",
            Sequence = 42
        };

        // Act
        using var requestPacket = new Packet(echoRequest);
        var responsePacket = await _connector.RequestAsync(requestPacket);
        var echoReply = EchoReply.Parser.ParseFrom(responsePacket.Payload.DataSpan.ToArray());

        // Assert
        Assert.Equal("Hello WebSocket!", echoReply.Content);
        Assert.Equal(42, echoReply.Sequence);
    }

    [Fact(DisplayName = "A-01-04: WebSocket으로 Push 메시지를 수신할 수 있다")]
    public async Task WebSocket_Push_Message_Received()
    {
        // Arrange
        await _connector!.ConnectAsync(
            _testServer.Host,
            _testServer.HttpPort
        );

        var authRequest = new AuthenticateRequest { UserId = "ws-user-3", Token = "valid_token" };
        await _testServer.AssignStageAsync(authRequest.UserId, _stageInfo!.StageId);
        using var authPacket = new Packet(authRequest);
        await _connector.AuthenticateAsync(authPacket);

        var receivedMessages = new List<IPacket>();
        _connector.OnReceive += (stageId, stageType, packet) =>
        {
            receivedMessages.Add(packet);
        };

        var broadcastRequest = new BroadcastRequest { Content = "WebSocket Broadcast Test" };

        // Act
        using var requestPacket = new Packet(broadcastRequest);
        _connector.Send(requestPacket);

        // Wait for push message
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (receivedMessages.Count == 0 && DateTime.UtcNow < deadline)
        {
            _connector.MainThreadAction();
            await Task.Delay(10);
        }

        // Assert
        Assert.NotEmpty(receivedMessages);
    }

    [Fact(DisplayName = "A-01-05: WebSocket 연결 해제 후 재연결이 가능하다")]
    public async Task WebSocket_Reconnection()
    {
        // Arrange
        await _connector!.ConnectAsync(
            _testServer.Host,
            _testServer.HttpPort
        );
        Assert.True(_connector.IsConnected());

        // Act - Disconnect
        _connector.Disconnect();
        await Task.Delay(500);
        Assert.False(_connector.IsConnected());

        // Act - Reconnect
        await _testServer.CreateTestStageAsync();
        var reconnected = await _connector.ConnectAsync(
            _testServer.Host,
            _testServer.HttpPort
        );

        // Assert
        Assert.True(reconnected);
        Assert.True(_connector.IsConnected());
    }

    [Fact(DisplayName = "A-01-06: WebSocket으로 병렬 요청을 처리할 수 있다")]
    public async Task WebSocket_Parallel_Requests()
    {
        // Arrange
        await _connector!.ConnectAsync(
            _testServer.Host,
            _testServer.HttpPort
        );

        var authRequest = new AuthenticateRequest { UserId = "ws-user-4", Token = "valid_token" };
        await _testServer.AssignStageAsync(authRequest.UserId, _stageInfo!.StageId);
        using var authPacket = new Packet(authRequest);
        await _connector.AuthenticateAsync(authPacket);

        // Act - Send 10 parallel requests
        var tasks = new List<Task<IPacket>>();
        for (int i = 0; i < 10; i++)
        {
            var echoRequest = new EchoRequest { Content = $"Parallel {i}", Sequence = i };
            var packet = new Packet(echoRequest);
            tasks.Add(_connector.RequestAsync(packet));
        }

        var responses = await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(10, responses.Length);
        foreach (var response in responses)
        {
            var echoReply = EchoReply.Parser.ParseFrom(response.Payload.DataSpan.ToArray());
            Assert.StartsWith("Parallel", echoReply.Content);
        }
    }

    [Fact(DisplayName = "A-01-07: OnConnect 이벤트가 WebSocket 연결 시 발생한다")]
    public async Task WebSocket_OnConnect_Event_Fired()
    {
        // Arrange
        var connectEventFired = false;
        var connectResult = false;
        _connector!.OnConnect += (result) =>
        {
            connectEventFired = true;
            connectResult = result;
        };

        // Act
        _connector.Connect(
            _testServer.Host,
            _testServer.HttpPort
        );

        // Wait for connection with MainThreadAction
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!connectEventFired && DateTime.UtcNow < deadline)
        {
            _connector.MainThreadAction();
            await Task.Delay(10);
        }

        // Assert
        Assert.True(connectEventFired);
        Assert.True(connectResult);
    }
}
