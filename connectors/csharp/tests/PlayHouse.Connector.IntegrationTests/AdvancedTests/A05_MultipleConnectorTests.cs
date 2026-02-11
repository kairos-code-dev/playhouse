using PlayHouse.Connector.Protocol;
using PlayHouse.TestServer.Proto;
using Xunit;

namespace PlayHouse.Connector.IntegrationTests.AdvancedTests;

/// <summary>
/// A-05: 다중 Connector 동시 사용 테스트
/// </summary>
/// <remarks>
/// 여러 Connector 인스턴스를 동시에 사용하여 독립적인 연결과 통신이 가능한지 검증.
/// 실제 게임에서 여러 서버에 동시 연결하거나 테스트 시 여러 클라이언트 시뮬레이션에 사용.
/// </remarks>
[Trait("Category", "Advanced")]
[Trait("Feature", "MultipleConnectors")]
public class A05_MultipleConnectorTests : IClassFixture<TestServerFixture>, IAsyncLifetime
{
    private readonly TestServerFixture _testServer;
    private readonly List<Connector> _connectors = new();
    private readonly List<CreateStageResponse> _stages = new();

    public A05_MultipleConnectorTests(TestServerFixture testServer)
    {
        _testServer = testServer;
    }

    public async Task InitializeAsync()
    {
        // 5개의 독립적인 Stage 생성
        for (int i = 0; i < 5; i++)
        {
            _stages.Add(await _testServer.CreateTestStageAsync());
        }
    }

    public async Task DisposeAsync()
    {
        foreach (var connector in _connectors)
        {
            if (connector.IsConnected())
            {
                connector.Disconnect();
                await Task.Delay(50);
            }
            await connector.DisposeAsync();
        }
        _connectors.Clear();
    }

    private Connector CreateConnector(int requestTimeoutMs = 10000)
    {
        var connector = new Connector();
        connector.Init(new ConnectorConfig
        {
            RequestTimeoutMs = requestTimeoutMs,
            HeartBeatIntervalMs = 10000
        });
        _connectors.Add(connector);
        return connector;
    }

    [Fact(DisplayName = "A-05-01: 여러 Connector가 동시에 연결할 수 있다")]
    public async Task Multiple_Connectors_Connect_Simultaneously()
    {
        // Arrange
        var connectors = new List<Connector>();
        for (int i = 0; i < 5; i++)
        {
            connectors.Add(CreateConnector());
        }

        // Act - 동시에 연결
        var connectTasks = connectors.Select((connector, index) =>
            connector.ConnectAsync(
                _testServer.Host,
                _testServer.TcpPort,
                _stages[index].StageId,
                _stages[index].StageType
            )
        ).ToList();

        var results = await Task.WhenAll(connectTasks);

        // Assert
        Assert.All(results, result => Assert.True(result));
        Assert.All(connectors, connector => Assert.True(connector.IsConnected()));
    }

    [Fact(DisplayName = "A-05-02: 여러 Connector가 독립적으로 인증할 수 있다")]
    public async Task Multiple_Connectors_Authenticate_Independently()
    {
        // Arrange
        var connectors = new List<Connector>();
        for (int i = 0; i < 3; i++)
        {
            var connector = CreateConnector();
            await connector.ConnectAsync(
                _testServer.Host,
                _testServer.TcpPort,
                _stages[i].StageId,
                _stages[i].StageType
            );
            connectors.Add(connector);
        }

        // Act - 각각 다른 사용자로 인증
        var authTasks = connectors.Select(async (connector, index) =>
        {
            var authRequest = new AuthenticateRequest
            {
                UserId = $"user-{index}",
                Token = "valid_token"
            };
            using var packet = new Packet(authRequest);
            return await connector.AuthenticateAsync(packet);
        }).ToList();

        var authResults = await Task.WhenAll(authTasks);

        // Assert
        Assert.All(connectors, connector => Assert.True(connector.IsAuthenticated()));
        for (int i = 0; i < authResults.Length; i++)
        {
            var reply = AuthenticateReply.Parser.ParseFrom(authResults[i].Payload.DataSpan.ToArray());
            Assert.True(reply.Success);
            Assert.Equal($"user-{i}", reply.ReceivedUserId);
        }
    }

    [Fact(DisplayName = "A-05-03: 여러 Connector가 동시에 요청을 보낼 수 있다")]
    public async Task Multiple_Connectors_Send_Requests_Simultaneously()
    {
        // Arrange
        var connectors = new List<Connector>();
        for (int i = 0; i < 3; i++)
        {
            var connector = CreateConnector();
            await connector.ConnectAsync(
                _testServer.Host,
                _testServer.TcpPort,
                _stages[i].StageId,
                _stages[i].StageType
            );

            var authRequest = new AuthenticateRequest { UserId = $"parallel-user-{i}", Token = "valid_token" };
            using var authPacket = new Packet(authRequest);
            await connector.AuthenticateAsync(authPacket);

            connectors.Add(connector);
        }

        // Act - 동시에 Echo 요청
        var requestTasks = connectors.Select(async (connector, index) =>
        {
            var echoRequest = new EchoRequest
            {
                Content = $"Hello from connector {index}",
                Sequence = index
            };
            using var packet = new Packet(echoRequest);
            return await connector.RequestAsync(packet);
        }).ToList();

        var responses = await Task.WhenAll(requestTasks);

        // Assert
        for (int i = 0; i < responses.Length; i++)
        {
            var reply = EchoReply.Parser.ParseFrom(responses[i].Payload.DataSpan.ToArray());
            Assert.Equal($"Hello from connector {i}", reply.Content);
            Assert.Equal(i, reply.Sequence);
        }
    }

    [Fact(DisplayName = "A-05-04: 한 Connector 연결 해제가 다른 Connector에 영향을 주지 않는다")]
    public async Task Connector_Disconnect_Does_Not_Affect_Others()
    {
        // Arrange
        var connectors = new List<Connector>();
        for (int i = 0; i < 3; i++)
        {
            var connector = CreateConnector();
            await connector.ConnectAsync(
                _testServer.Host,
                _testServer.TcpPort,
                _stages[i].StageId,
                _stages[i].StageType
            );

            var authRequest = new AuthenticateRequest { UserId = $"disconnect-test-{i}", Token = "valid_token" };
            using var authPacket = new Packet(authRequest);
            await connector.AuthenticateAsync(authPacket);

            connectors.Add(connector);
        }

        // Act - 첫 번째 Connector 연결 해제
        connectors[0].Disconnect();
        await Task.Delay(500);

        // Assert - 다른 Connector들은 여전히 연결되어 있어야 함
        Assert.False(connectors[0].IsConnected());
        Assert.True(connectors[1].IsConnected());
        Assert.True(connectors[2].IsConnected());

        // 나머지 Connector로 요청 가능해야 함
        for (int i = 1; i < connectors.Count; i++)
        {
            var echoRequest = new EchoRequest { Content = "Still connected", Sequence = i };
            using var packet = new Packet(echoRequest);
            var response = await connectors[i].RequestAsync(packet);
            var reply = EchoReply.Parser.ParseFrom(response.Payload.DataSpan.ToArray());
            Assert.Equal("Still connected", reply.Content);
        }
    }

    [Fact(DisplayName = "A-05-05: 동일 Stage에 여러 Connector가 연결할 수 있다")]
    public async Task Multiple_Connectors_Same_Stage()
    {
        // Arrange - 같은 Stage에 여러 Connector 연결
        var sharedStage = _stages[0];
        var connectors = new List<Connector>();

        for (int i = 0; i < 3; i++)
        {
            var connector = CreateConnector();
            await connector.ConnectAsync(
                _testServer.Host,
                _testServer.TcpPort,
                sharedStage.StageId,
                sharedStage.StageType
            );

            var authRequest = new AuthenticateRequest { UserId = $"same-stage-user-{i}", Token = "valid_token" };
            using var authPacket = new Packet(authRequest);
            await connector.AuthenticateAsync(authPacket);

            connectors.Add(connector);
        }

        // Act & Assert - 각 Connector가 독립적으로 요청 처리 가능
        for (int i = 0; i < connectors.Count; i++)
        {
            var echoRequest = new EchoRequest { Content = $"User {i} message", Sequence = i };
            using var packet = new Packet(echoRequest);
            var response = await connectors[i].RequestAsync(packet);
            var reply = EchoReply.Parser.ParseFrom(response.Payload.DataSpan.ToArray());
            Assert.Equal($"User {i} message", reply.Content);
        }
    }

    [Fact(DisplayName = "A-05-06: 대량의 Connector 동시 연결/해제 테스트")]
    public async Task Stress_Test_Many_Connectors()
    {
        // Arrange
        const int connectorCount = 10;
        var connectors = new List<Connector>();
        var extraStages = new List<CreateStageResponse>();

        // 추가 Stage 생성
        for (int i = 0; i < connectorCount; i++)
        {
            extraStages.Add(await _testServer.CreateTestStageAsync());
        }

        // Act - 동시에 연결
        var connectTasks = new List<Task<bool>>();
        for (int i = 0; i < connectorCount; i++)
        {
            var connector = CreateConnector();
            connectors.Add(connector);

            var stage = extraStages[i];
            connectTasks.Add(connector.ConnectAsync(
                _testServer.Host,
                _testServer.TcpPort,
                stage.StageId,
                stage.StageType
            ));
        }

        var connectResults = await Task.WhenAll(connectTasks);

        // Assert - 모든 연결 성공
        Assert.All(connectResults, result => Assert.True(result));

        // 동시에 해제
        foreach (var connector in connectors)
        {
            connector.Disconnect();
        }

        await Task.Delay(500);

        // 모든 연결 해제 확인
        Assert.All(connectors, connector => Assert.False(connector.IsConnected()));
    }

    [Fact(DisplayName = "A-05-07: 각 Connector의 이벤트가 독립적으로 발생한다")]
    public async Task Connector_Events_Are_Independent()
    {
        // Arrange
        var connector1Received = new List<string>();
        var connector2Received = new List<string>();

        var connector1 = CreateConnector();
        var connector2 = CreateConnector();

        connector1.OnReceive += (_, _, packet) =>
        {
            var notify = BroadcastNotify.Parser.ParseFrom(packet.Payload.DataSpan.ToArray());
            connector1Received.Add(notify.Data);
        };

        connector2.OnReceive += (_, _, packet) =>
        {
            var notify = BroadcastNotify.Parser.ParseFrom(packet.Payload.DataSpan.ToArray());
            connector2Received.Add(notify.Data);
        };

        await connector1.ConnectAsync(_testServer.Host, _testServer.TcpPort);
        await connector2.ConnectAsync(_testServer.Host, _testServer.TcpPort);

        var auth1 = new AuthenticateRequest { UserId = "event-user-1", Token = "valid_token" };
        using var auth1Packet = new Packet(auth1);
        await connector1.AuthenticateAsync(auth1Packet);

        var auth2 = new AuthenticateRequest { UserId = "event-user-2", Token = "valid_token" };
        using var auth2Packet = new Packet(auth2);
        await connector2.AuthenticateAsync(auth2Packet);

        // Act - 각 Connector에서 Broadcast 요청
        var broadcast1 = new BroadcastRequest { Content = "From Connector 1" };
        using var b1Packet = new Packet(broadcast1);
        connector1.Send(b1Packet);

        var broadcast2 = new BroadcastRequest { Content = "From Connector 2" };
        using var b2Packet = new Packet(broadcast2);
        connector2.Send(b2Packet);

        // Wait for push messages
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while ((connector1Received.Count == 0 || connector2Received.Count == 0) && DateTime.UtcNow < deadline)
        {
            connector1.MainThreadAction();
            connector2.MainThreadAction();
            await Task.Delay(10);
        }

        // Assert - 각 Connector는 자신에게 온 메시지만 받아야 함
        Assert.Contains("From Connector 1", connector1Received);
        Assert.Contains("From Connector 2", connector2Received);
    }
}
