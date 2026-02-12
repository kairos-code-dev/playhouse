using PlayHouse.Connector.Protocol;
using PlayHouse.TestServer.Proto;
using Xunit;

namespace PlayHouse.Connector.IntegrationTests.AdvancedTests;

/// <summary>
/// A-07: TLS/WSS 연결 테스트
/// </summary>
/// <remarks>
/// TCP+TLS 및 WSS 전송 계층 연결, 인증, Echo 동작을 검증합니다.
/// </remarks>
[Trait("Category", "Advanced")]
[Trait("Transport", "TLS")]
public class A07_TlsConnectionTests : IClassFixture<TestServerFixture>, IAsyncLifetime
{
    private readonly TestServerFixture _testServer;
    private Connector? _connector;
    private CreateStageResponse? _stageInfo;

    public A07_TlsConnectionTests(TestServerFixture testServer)
    {
        _testServer = testServer;
    }

    public async Task InitializeAsync()
    {
        _connector = new Connector();
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

    [Fact(DisplayName = "A-07-01: TCP+TLS로 서버에 연결할 수 있다")]
    public async Task TcpTls_Connection_Success()
    {
        _connector!.Init(new ConnectorConfig
        {
            UseWebsocket = false,
            UseSsl = true,
            SkipServerCertificateValidation = true,
            RequestTimeoutMs = 5000,
            HeartBeatIntervalMs = 10000
        });

        var connected = await _connector.ConnectAsync(
            _testServer.Host,
            _testServer.TcpTlsPort
        );

        Assert.True(connected);
        Assert.True(_connector.IsConnected());

        var authRequest = new AuthenticateRequest { UserId = "tls-user-1", Token = "valid_token" };
        await _testServer.AssignStageAsync(authRequest.UserId, _stageInfo!.StageId);
        using var authPacket = new Packet(authRequest);
        var authReplyPacket = await _connector.AuthenticateAsync(authPacket);
        var authReply = AuthenticateReply.Parser.ParseFrom(authReplyPacket.Payload.DataSpan.ToArray());
        Assert.True(authReply.Success);

        var echoRequest = new EchoRequest { Content = "Hello TLS", Sequence = 1 };
        using var echoPacket = new Packet(echoRequest);
        var echoReplyPacket = await _connector.RequestAsync(echoPacket);
        var echoReply = EchoReply.Parser.ParseFrom(echoReplyPacket.Payload.DataSpan.ToArray());
        Assert.Equal("Hello TLS", echoReply.Content);
    }

    [Fact(DisplayName = "A-07-02: WSS로 서버에 연결할 수 있다")]
    public async Task Wss_Connection_Success()
    {
        _connector!.Init(new ConnectorConfig
        {
            UseWebsocket = true,
            UseSsl = true,
            SkipServerCertificateValidation = true,
            WebSocketPath = "/ws",
            RequestTimeoutMs = 5000,
            HeartBeatIntervalMs = 10000
        });

        var connected = await _connector.ConnectAsync(
            _testServer.Host,
            _testServer.HttpsPort
        );

        Assert.True(connected);
        Assert.True(_connector.IsConnected());

        var authRequest = new AuthenticateRequest { UserId = "wss-user-1", Token = "valid_token" };
        await _testServer.AssignStageAsync(authRequest.UserId, _stageInfo!.StageId);
        using var authPacket = new Packet(authRequest);
        var authReplyPacket = await _connector.AuthenticateAsync(authPacket);
        var authReply = AuthenticateReply.Parser.ParseFrom(authReplyPacket.Payload.DataSpan.ToArray());
        Assert.True(authReply.Success);

        var echoRequest = new EchoRequest { Content = "Hello WSS", Sequence = 2 };
        using var echoPacket = new Packet(echoRequest);
        var echoReplyPacket = await _connector.RequestAsync(echoPacket);
        var echoReply = EchoReply.Parser.ParseFrom(echoReplyPacket.Payload.DataSpan.ToArray());
        Assert.Equal("Hello WSS", echoReply.Content);
    }
}
