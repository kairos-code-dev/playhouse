using PlayHouse.Connector.Protocol;
using PlayHouse.TestServer.Proto;
using Xunit;

namespace PlayHouse.Connector.IntegrationTests.AdvancedTests;

/// <summary>
/// A-06: Edge Case 테스트
/// </summary>
/// <remarks>
/// 경계 조건, 비정상 입력, Config 검증 등 엣지 케이스 테스트.
/// </remarks>
[Trait("Category", "Advanced")]
[Trait("Feature", "EdgeCases")]
public class A06_EdgeCaseTests : IClassFixture<TestServerFixture>, IAsyncLifetime
{
    private readonly TestServerFixture _testServer;
    private Connector? _connector;
    private CreateStageResponse? _stageInfo;

    public A06_EdgeCaseTests(TestServerFixture testServer)
    {
        _testServer = testServer;
    }

    public async Task InitializeAsync()
    {
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
            _connector = null;
        }
    }

    private void CreateConnectorWithConfig(ConnectorConfig config)
    {
        _connector = new Connector();
        _connector.Init(config);
    }

    [Fact(DisplayName = "A-06-01: Init 없이 Connect 시 예외가 발생한다")]
    public async Task Connect_Without_Init_Throws()
    {
        // Arrange
        _connector = new Connector();
        // Init을 호출하지 않음

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _connector.ConnectAsync(
                _testServer.Host,
                _testServer.TcpPort
            );
        });
    }

    [Fact(DisplayName = "A-06-02: null Config로 Init 시 예외가 발생한다")]
    public void Init_With_Null_Config_Throws()
    {
        // Arrange
        _connector = new Connector();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _connector.Init(null!));
    }

    [Fact(DisplayName = "A-06-03: 기본 Config 값이 올바르게 설정된다")]
    public void Default_Config_Values()
    {
        // Arrange
        var config = new ConnectorConfig();

        // Assert
        Assert.False(config.UseWebsocket);
        Assert.False(config.UseSsl);
        Assert.Equal("/ws", config.WebSocketPath);
        Assert.Equal(30000, config.ConnectionIdleTimeoutMs);
        Assert.Equal(10000, config.HeartBeatIntervalMs);
        Assert.Equal(30000, config.HeartbeatTimeoutMs);
        Assert.Equal(30000, config.RequestTimeoutMs);
        Assert.False(config.EnableLoggingResponseTime);
        Assert.False(config.TurnOnTrace);
    }

    [Fact(DisplayName = "A-06-04: 짧은 타임아웃 설정이 적용된다")]
    public async Task Short_Timeout_Config_Applied()
    {
        // Arrange
        CreateConnectorWithConfig(new ConnectorConfig
        {
            RequestTimeoutMs = 100,  // 매우 짧은 타임아웃
            HeartBeatIntervalMs = 10000
        });

        await _connector!.ConnectAsync(
            _testServer.Host,
            _testServer.TcpPort
        );

        var authRequest = new AuthenticateRequest { UserId = "timeout-user", Token = "valid_token" };
        using var authPacket = new Packet(authRequest);
        await _connector.AuthenticateAsync(authPacket);

        // NoResponseRequest는 응답하지 않으므로 타임아웃 발생
        var noResponseRequest = new NoResponseRequest { DelayMs = 1000 };

        // Act & Assert
        using var packet = new Packet(noResponseRequest);
        await Assert.ThrowsAsync<ConnectorException>(async () =>
        {
            await _connector.RequestAsync(packet);
        });
    }

    [Fact(DisplayName = "A-06-05: 잘못된 호스트로 연결 시 실패한다")]
    public async Task Connect_To_Invalid_Host_Fails()
    {
        // Arrange
        CreateConnectorWithConfig(new ConnectorConfig
        {
            RequestTimeoutMs = 3000,
            HeartBeatIntervalMs = 10000
        });

        // Act
        var connected = await _connector!.ConnectAsync(
            "invalid.host.that.does.not.exist.local",
            _testServer.TcpPort
        );

        // Assert
        Assert.False(connected);
        Assert.False(_connector.IsConnected());
    }

    [Fact(DisplayName = "A-06-06: 잘못된 포트로 연결 시 실패한다")]
    public async Task Connect_To_Invalid_Port_Fails()
    {
        // Arrange
        CreateConnectorWithConfig(new ConnectorConfig
        {
            RequestTimeoutMs = 3000,
            HeartBeatIntervalMs = 10000
        });

        // Act
        var connected = await _connector!.ConnectAsync(
            _testServer.Host,
            59999  // 사용하지 않는 포트
        );

        // Assert
        Assert.False(connected);
        Assert.False(_connector.IsConnected());
    }

    [Fact(DisplayName = "A-06-07: 빈 MsgId 패킷도 처리된다")]
    public async Task Empty_MsgId_Packet_Handled()
    {
        // Arrange
        CreateConnectorWithConfig(new ConnectorConfig
        {
            RequestTimeoutMs = 5000,
            HeartBeatIntervalMs = 10000
        });

        await _connector!.ConnectAsync(
            _testServer.Host,
            _testServer.TcpPort
        );

        var authRequest = new AuthenticateRequest { UserId = "empty-msgid-user", Token = "valid_token" };
        using var authPacket = new Packet(authRequest);
        await _connector.AuthenticateAsync(authPacket);

        // Act - 빈 MsgId로 패킷 생성 시도
        // Packet.Empty 사용
        using var emptyPacket = Packet.Empty("UnknownMessage");
        var response = await _connector.RequestAsync(emptyPacket);

        // Assert - 서버가 알 수 없는 메시지에 대해 기본 응답
        Assert.NotNull(response);
    }

    [Fact(DisplayName = "A-06-08: 동일 Connector로 여러 번 Disconnect 호출이 안전하다")]
    public async Task Multiple_Disconnect_Calls_Safe()
    {
        // Arrange
        CreateConnectorWithConfig(new ConnectorConfig
        {
            RequestTimeoutMs = 5000,
            HeartBeatIntervalMs = 10000
        });

        await _connector!.ConnectAsync(
            _testServer.Host,
            _testServer.TcpPort
        );

        // Act - 여러 번 Disconnect
        _connector.Disconnect();
        _connector.Disconnect();
        _connector.Disconnect();

        await Task.Delay(500);

        // Assert
        Assert.False(_connector.IsConnected());
    }

    [Fact(DisplayName = "A-06-09: DisposeAsync 후 연결 시도 시 예외가 발생한다")]
    public async Task Connect_After_Dispose_Throws()
    {
        // Arrange
        CreateConnectorWithConfig(new ConnectorConfig
        {
            RequestTimeoutMs = 5000,
            HeartBeatIntervalMs = 10000
        });

        await _connector!.DisposeAsync();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _connector.ConnectAsync(
                _testServer.Host,
                _testServer.TcpPort
            );
        });

        _connector = null;  // DisposeAsync에서 다시 처리하지 않도록
    }

    [Fact(DisplayName = "A-06-10: 연결 중 DisposeAsync가 안전하게 처리된다")]
    public async Task DisposeAsync_While_Connected()
    {
        // Arrange
        CreateConnectorWithConfig(new ConnectorConfig
        {
            RequestTimeoutMs = 5000,
            HeartBeatIntervalMs = 10000
        });

        await _connector!.ConnectAsync(
            _testServer.Host,
            _testServer.TcpPort
        );

        Assert.True(_connector.IsConnected());

        // Act
        await _connector.DisposeAsync();

        // Assert - DisposeAsync 후에는 연결 상태 확인 불가 (null)
        _connector = null;  // DisposeAsync에서 다시 처리하지 않도록
    }

    [Fact(DisplayName = "A-06-11: 인증 성공 후 StageId와 StageType이 Connector에 저장된다")]
    public async Task StageId_And_StageType_Stored_AfterAuthentication()
    {
        // Arrange
        CreateConnectorWithConfig(new ConnectorConfig
        {
            RequestTimeoutMs = 5000,
            HeartBeatIntervalMs = 10000
        });

        // Act
        await _connector!.ConnectAsync(
            _testServer.Host,
            _testServer.TcpPort
        );

        var userId = "stage-context-user";
        await _testServer.AssignStageAsync(userId, _stageInfo!.StageId);
        var authRequest = new AuthenticateRequest { UserId = userId, Token = "valid_token" };
        using var authPacket = new Packet(authRequest);
        await _connector.AuthenticateAsync(authPacket);

        // Assert
        Assert.Equal(_stageInfo!.StageId, _connector.StageId);
        Assert.Equal(_stageInfo.StageType, _connector.StageType);
    }

    [Fact(DisplayName = "A-06-12: 매우 긴 문자열도 에코할 수 있다")]
    public async Task Very_Long_String_Echo()
    {
        // Arrange
        CreateConnectorWithConfig(new ConnectorConfig
        {
            RequestTimeoutMs = 30000,
            HeartBeatIntervalMs = 10000
        });

        await _connector!.ConnectAsync(
            _testServer.Host,
            _testServer.TcpPort
        );

        var authRequest = new AuthenticateRequest { UserId = "long-string-user", Token = "valid_token" };
        using var authPacket = new Packet(authRequest);
        await _connector.AuthenticateAsync(authPacket);

        // 64KB 문자열
        var longContent = new string('X', 65536);
        var echoRequest = new EchoRequest { Content = longContent, Sequence = 1 };

        // Act
        using var packet = new Packet(echoRequest);
        var response = await _connector.RequestAsync(packet);
        var reply = EchoReply.Parser.ParseFrom(response.Payload.DataSpan.ToArray());

        // Assert
        Assert.Equal(longContent, reply.Content);
    }

    [Fact(DisplayName = "A-06-13: 특수문자가 포함된 문자열도 에코할 수 있다")]
    public async Task Special_Characters_Echo()
    {
        // Arrange
        CreateConnectorWithConfig(new ConnectorConfig
        {
            RequestTimeoutMs = 5000,
            HeartBeatIntervalMs = 10000
        });

        await _connector!.ConnectAsync(
            _testServer.Host,
            _testServer.TcpPort
        );

        var authRequest = new AuthenticateRequest { UserId = "special-char-user", Token = "valid_token" };
        using var authPacket = new Packet(authRequest);
        await _connector.AuthenticateAsync(authPacket);

        var specialContent = "Hello\0World\n\r\t\"'\\<>&한글日本語🎮";
        var echoRequest = new EchoRequest { Content = specialContent, Sequence = 1 };

        // Act
        using var packet = new Packet(echoRequest);
        var response = await _connector.RequestAsync(packet);
        var reply = EchoReply.Parser.ParseFrom(response.Payload.DataSpan.ToArray());

        // Assert
        Assert.Equal(specialContent, reply.Content);
    }
}
