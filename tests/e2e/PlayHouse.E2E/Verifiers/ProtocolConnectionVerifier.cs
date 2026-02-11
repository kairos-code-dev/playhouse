#nullable enable

using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;

namespace PlayHouse.E2E.Verifiers;

/// <summary>
/// 프로토콜별 연결 테스트 (TCP, TCP+TLS, WebSocket)
/// </summary>
public class ProtocolConnectionVerifier(ServerContext serverContext) : VerifierBase(serverContext)
{
    public override string CategoryName => "ProtocolConnection";

    public override int GetTestCount()
    {
        var count = 2; // TCP 기본 테스트
        if (ServerContext.TcpTlsPort > 0) count += 2;
        if (ServerContext.WebSocketPort > 0) count += 2;
        return count;
    }

    protected override async Task RunTestsAsync()
    {
        // TCP 테스트 (기본)
        await RunTest("Tcp_Connect_Success", Test_Tcp_Connect_Success);
        await RunTest("Tcp_Authenticate_Success", Test_Tcp_Authenticate_Success);

        // TCP + TLS 테스트 (서버가 설정된 경우)
        if (ServerContext.TcpTlsPort > 0)
        {
            await RunTest("TcpTls_Connect_Success", Test_TcpTls_Connect_Success);
            await RunTest("TcpTls_Authenticate_Success", Test_TcpTls_Authenticate_Success);
        }

        // WebSocket 테스트 (서버가 설정된 경우)
        if (ServerContext.WebSocketPort > 0)
        {
            await RunTest("WebSocket_Connect_Success", Test_WebSocket_Connect_Success);
            await RunTest("WebSocket_Authenticate_Success", Test_WebSocket_Authenticate_Success);
        }
    }

    #region TCP Tests

    private async Task Test_Tcp_Connect_Success()
    {
        // Given
        var connector = CreateTcpConnector();
        var stageId = GenerateUniqueStageId();

        try
        {
            // When
            var result = await connector.ConnectAsync("127.0.0.1", ServerContext.TcpPort);

            // Then
            Assert.IsTrue(result, "TCP ConnectAsync should return true");
            Assert.IsTrue(connector.IsConnected(), "TCP IsConnected() should be true");
        }
        finally
        {
            connector.Disconnect();
            await Task.Delay(100);
        }
    }

    private async Task Test_Tcp_Authenticate_Success()
    {
        // Given
        var connector = CreateTcpConnector();
        var stageId = GenerateUniqueStageId();
        var connected = await connector.ConnectAsync("127.0.0.1", ServerContext.TcpPort);
        Assert.IsTrue(connected, "Should connect via TCP");

        try
        {
            // When
            using var authPacket = Packet.Empty("AuthenticateRequest");
            var response = await connector.AuthenticateAsync(authPacket);

            // Then
            Assert.NotNull(response, "TCP Should receive authentication response");
            Assert.IsTrue(connector.IsAuthenticated(), "TCP IsAuthenticated() should be true");
        }
        finally
        {
            connector.Disconnect();
            await Task.Delay(100);
        }
    }

    #endregion

    #region TCP + TLS Tests

    private async Task Test_TcpTls_Connect_Success()
    {
        // Given
        var connector = CreateTcpTlsConnector();
        var stageId = GenerateUniqueStageId();

        try
        {
            // When
            var result = await connector.ConnectAsync("127.0.0.1", ServerContext.TcpTlsPort);

            // Then
            Assert.IsTrue(result, "TCP+TLS ConnectAsync should return true");
            Assert.IsTrue(connector.IsConnected(), "TCP+TLS IsConnected() should be true");
        }
        finally
        {
            connector.Disconnect();
            await Task.Delay(100);
        }
    }

    private async Task Test_TcpTls_Authenticate_Success()
    {
        // Given
        var connector = CreateTcpTlsConnector();
        var stageId = GenerateUniqueStageId();
        var connected = await connector.ConnectAsync("127.0.0.1", ServerContext.TcpTlsPort);
        Assert.IsTrue(connected, "Should connect via TCP+TLS");

        try
        {
            // When
            using var authPacket = Packet.Empty("AuthenticateRequest");
            var response = await connector.AuthenticateAsync(authPacket);

            // Then
            Assert.NotNull(response, "TCP+TLS Should receive authentication response");
            Assert.IsTrue(connector.IsAuthenticated(), "TCP+TLS IsAuthenticated() should be true");
        }
        finally
        {
            connector.Disconnect();
            await Task.Delay(100);
        }
    }

    #endregion

    #region WebSocket Tests

    private async Task Test_WebSocket_Connect_Success()
    {
        // Given
        var connector = CreateWebSocketConnector();
        var stageId = GenerateUniqueStageId();

        try
        {
            // When
            var result = await connector.ConnectAsync("127.0.0.1", ServerContext.WebSocketPort);

            // Then
            Assert.IsTrue(result, "WebSocket ConnectAsync should return true");
            Assert.IsTrue(connector.IsConnected(), "WebSocket IsConnected() should be true");
        }
        finally
        {
            connector.Disconnect();
            await Task.Delay(100);
        }
    }

    private async Task Test_WebSocket_Authenticate_Success()
    {
        // Given
        var connector = CreateWebSocketConnector();
        var stageId = GenerateUniqueStageId();
        var connected = await connector.ConnectAsync("127.0.0.1", ServerContext.WebSocketPort);
        Assert.IsTrue(connected, "Should connect via WebSocket");

        try
        {
            // When
            using var authPacket = Packet.Empty("AuthenticateRequest");
            var response = await connector.AuthenticateAsync(authPacket);

            // Then
            Assert.NotNull(response, "WebSocket Should receive authentication response");
            Assert.IsTrue(connector.IsAuthenticated(), "WebSocket IsAuthenticated() should be true");
        }
        finally
        {
            connector.Disconnect();
            await Task.Delay(100);
        }
    }

    #endregion

    #region Helper Methods

    private static PlayHouse.Connector.Connector CreateTcpConnector()
    {
        var connector = new PlayHouse.Connector.Connector();
        connector.Init(new ConnectorConfig
        {
            UseWebsocket = false,
            UseSsl = false,
            RequestTimeoutMs = 30000
        });
        return connector;
    }

    private static PlayHouse.Connector.Connector CreateTcpTlsConnector()
    {
        var connector = new PlayHouse.Connector.Connector();
        connector.Init(new ConnectorConfig
        {
            UseWebsocket = false,
            UseSsl = true,
            SkipServerCertificateValidation = true, // 테스트용 자체 서명 인증서
            RequestTimeoutMs = 30000
        });
        return connector;
    }

    private static PlayHouse.Connector.Connector CreateWebSocketConnector()
    {
        var connector = new PlayHouse.Connector.Connector();
        connector.Init(new ConnectorConfig
        {
            UseWebsocket = true,
            UseSsl = false,
            RequestTimeoutMs = 30000
        });
        return connector;
    }

    #endregion
}
