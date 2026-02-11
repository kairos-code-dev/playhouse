package com.playhouse.connector.advanced;

import com.playhouse.connector.Connector;
import com.playhouse.connector.ConnectorConfig;
import com.playhouse.connector.Packet;
import com.playhouse.connector.support.BaseIntegrationTest;
import com.playhouse.connector.support.TestMessages.*;
import org.junit.jupiter.api.*;

import java.util.concurrent.CompletableFuture;
import java.util.concurrent.TimeUnit;

import static org.assertj.core.api.Assertions.*;

/**
 * A-07: TLS/WSS 연결 테스트
 * <p>
 * TCP+TLS 및 WSS 전송 계층 연결, 인증, 메시지 송수신 테스트.
 * </p>
 */
@DisplayName("A-07: TLS/WSS 연결 테스트")
@Tag("Advanced")
@Tag("TLS")
class A07_TlsConnectionTests extends BaseIntegrationTest {

    @Override
    @BeforeEach
    public void setUp() throws Exception {
        host = System.getenv().getOrDefault("TEST_SERVER_HOST", "127.0.0.1");
        httpPort = Integer.parseInt(System.getenv().getOrDefault("TEST_SERVER_HTTP_PORT", "28080"));
        httpsPort = Integer.parseInt(System.getenv().getOrDefault("TEST_SERVER_HTTPS_PORT", "28443"));
        tcpPort = Integer.parseInt(System.getenv().getOrDefault("TEST_SERVER_TCP_PORT", "28001"));
        tcpTlsPort = Integer.parseInt(System.getenv().getOrDefault("TEST_SERVER_TCP_TLS_PORT", "28002"));

        testServer = new com.playhouse.connector.support.TestServerClient(host, httpPort);
        stageInfo = testServer.createTestStage();

        connector = new Connector();
    }

    @Test
    @DisplayName("A-07-01: TCP+TLS로 서버에 연결할 수 있다")
    void tcpTlsConnectionSuccess() throws Exception {
        connector.init(ConnectorConfig.builder()
                .useSsl(true)
                .skipServerCertificateValidation(true)
                .requestTimeoutMs(5000)
                .heartbeatIntervalMs(10000)
                .build());

        CompletableFuture<Void> connectFuture = connector.connectAsync(host, tcpTlsPort);
        connectFuture.get(5, TimeUnit.SECONDS);

        assertThat(connector.isConnected()).isTrue();

        AuthenticateRequest authRequest = new AuthenticateRequest("tls-user-1", "valid_token");
        Packet authPacket = Packet.builder("AuthenticateRequest")
                .payload(authRequest.toByteArray())
                .build();

        Packet authReplyPacket = connector.requestAsync(authPacket)
                .get(5, TimeUnit.SECONDS);
        AuthenticateReply authReply = AuthenticateReply.parseFrom(authReplyPacket.getPayload());
        assertThat(authReply.success).isTrue();

        EchoRequest echoRequest = new EchoRequest("Hello TLS", 1);
        Packet echoPacket = Packet.builder("EchoRequest")
                .payload(echoRequest.toByteArray())
                .build();

        Packet echoReplyPacket = connector.requestAsync(echoPacket)
                .get(5, TimeUnit.SECONDS);
        EchoReply echoReply = EchoReply.parseFrom(echoReplyPacket.getPayload());
        assertThat(echoReply.content).isEqualTo("Hello TLS");
    }

    @Test
    @DisplayName("A-07-02: WSS로 서버에 연결할 수 있다")
    void wssConnectionSuccess() throws Exception {
        connector.init(ConnectorConfig.builder()
                .useWebsocket(true)
                .useSsl(true)
                .webSocketPath("/ws")
                .skipServerCertificateValidation(true)
                .requestTimeoutMs(5000)
                .heartbeatIntervalMs(10000)
                .build());

        CompletableFuture<Void> connectFuture = connector.connectAsync(host, httpsPort);
        connectFuture.get(5, TimeUnit.SECONDS);

        assertThat(connector.isConnected()).isTrue();

        AuthenticateRequest authRequest = new AuthenticateRequest("wss-user-1", "valid_token");
        Packet authPacket = Packet.builder("AuthenticateRequest")
                .payload(authRequest.toByteArray())
                .build();

        Packet authReplyPacket = connector.requestAsync(authPacket)
                .get(5, TimeUnit.SECONDS);
        AuthenticateReply authReply = AuthenticateReply.parseFrom(authReplyPacket.getPayload());
        assertThat(authReply.success).isTrue();

        EchoRequest echoRequest = new EchoRequest("Hello WSS", 2);
        Packet echoPacket = Packet.builder("EchoRequest")
                .payload(echoRequest.toByteArray())
                .build();

        Packet echoReplyPacket = connector.requestAsync(echoPacket)
                .get(5, TimeUnit.SECONDS);
        EchoReply echoReply = EchoReply.parseFrom(echoReplyPacket.getPayload());
        assertThat(echoReply.content).isEqualTo("Hello WSS");
    }
}
