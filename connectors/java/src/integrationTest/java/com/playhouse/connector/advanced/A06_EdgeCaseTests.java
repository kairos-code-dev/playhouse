package com.playhouse.connector.advanced;

import com.playhouse.connector.Connector;
import com.playhouse.connector.ConnectorConfig;
import com.playhouse.connector.ConnectorException;
import com.playhouse.connector.Packet;
import com.playhouse.connector.support.CreateStageResponse;
import com.playhouse.connector.support.TestMessages.*;
import com.playhouse.connector.support.TestServerClient;
import org.junit.jupiter.api.*;

import java.util.concurrent.CompletableFuture;
import java.util.concurrent.TimeUnit;

import static org.assertj.core.api.Assertions.*;

/**
 * A-06: Edge Case 테스트
 * <p>
 * 경계 조건, 비정상 입력, Config 검증 등 엣지 케이스 테스트.
 * </p>
 */
@DisplayName("A-06: Edge Case 테스트")
@Tag("Advanced")
@Tag("EdgeCases")
class A06_EdgeCaseTests {

    private TestServerClient testServer;
    private Connector connector;
    private CreateStageResponse stageInfo;

    private String host;
    private int tcpPort;
    private int httpPort;

    @BeforeEach
    void setUp() throws Exception {
        host = System.getenv().getOrDefault("TEST_SERVER_HOST", "127.0.0.1");
        httpPort = Integer.parseInt(System.getenv().getOrDefault("TEST_SERVER_HTTP_PORT", "28080"));
        tcpPort = Integer.parseInt(System.getenv().getOrDefault("TEST_SERVER_TCP_PORT", "28001"));

        testServer = new TestServerClient(host, httpPort);
        stageInfo = testServer.createTestStage();
    }

    @AfterEach
    void tearDown() throws Exception {
        if (connector != null) {
            if (connector.isConnected()) {
                connector.disconnect();
                Thread.sleep(100);
            }
            connector.close();
            connector = null;
        }

        if (testServer != null) {
            testServer.close();
        }
    }

    private void createConnectorWithConfig(ConnectorConfig config) {
        connector = new Connector();
        connector.init(config);
    }

    @Test
    @DisplayName("A-06-01: Init 없이 Connect 시 예외가 발생한다")
    void connectWithoutInitThrows() {
        // Arrange
        connector = new Connector();
        // Init을 호출하지 않음

        // Act & Assert - Java connector throws IllegalStateException from checkInitialized()
        assertThatThrownBy(() -> {
            CompletableFuture<Void> connectFuture = connector.connectAsync(host, tcpPort);
            connectFuture.get(5, TimeUnit.SECONDS);
        }).isInstanceOf(IllegalStateException.class);
    }

    @Test
    @DisplayName("A-06-02: null Config로 Init 시 예외가 발생한다")
    void initWithNullConfigThrows() {
        // Arrange
        connector = new Connector();

        // Act & Assert
        assertThatThrownBy(() -> connector.init(null))
                .isInstanceOf(IllegalArgumentException.class);
    }

    @Test
    @DisplayName("A-06-03: 기본 Config 값이 올바르게 설정된다")
    void defaultConfigValues() {
        // Arrange
        ConnectorConfig config = ConnectorConfig.builder().build();

        // Assert
        assertThat(config.isUseWebsocket()).isFalse();
        assertThat(config.isUseSsl()).isFalse();
        assertThat(config.getWebSocketPath()).isEqualTo("/ws");
        assertThat(config.getConnectionIdleTimeoutMs()).isEqualTo(30000);
        assertThat(config.getHeartbeatIntervalMs()).isEqualTo(10000);
        assertThat(config.getHeartbeatTimeoutMs()).isEqualTo(30000);
        assertThat(config.getRequestTimeoutMs()).isEqualTo(30000);
    }

    @Test
    @DisplayName("A-06-04: 짧은 타임아웃 설정이 적용된다")
    void shortTimeoutConfigApplied() throws Exception {
        // Arrange
        createConnectorWithConfig(ConnectorConfig.builder()
                .requestTimeoutMs(100)  // 매우 짧은 타임아웃
                .heartbeatIntervalMs(10000)
                .build());

        CompletableFuture<Void> connectFuture = connector.connectAsync(host, tcpPort);
        connectFuture.get(5, TimeUnit.SECONDS);

        AuthenticateRequest authRequest = new AuthenticateRequest("timeout-user", "valid_token");
        Packet authPacket = Packet.builder("AuthenticateRequest")
                .payload(authRequest.toByteArray())
                .build();
        connector.requestAsync(authPacket).get(5, TimeUnit.SECONDS);

        // NoResponseRequest는 응답하지 않으므로 타임아웃 발생
        NoResponseRequest noResponseRequest = new NoResponseRequest(1000);

        // Act & Assert
        Packet packet = Packet.builder("NoResponseRequest")
                .payload(noResponseRequest.toByteArray())
                .build();

        assertThatThrownBy(() -> connector.requestAsync(packet).get(10, TimeUnit.SECONDS))
                .hasCauseInstanceOf(ConnectorException.class);
    }

    @Test
    @DisplayName("A-06-05: 잘못된 호스트로 연결 시 실패한다")
    void connectToInvalidHostFails() throws Exception {
        // Arrange
        createConnectorWithConfig(ConnectorConfig.builder()
                .requestTimeoutMs(3000)
                .heartbeatIntervalMs(10000)
                .build());

        // Act
        CompletableFuture<Void> connectFuture = connector.connectAsync(
                "invalid.host.that.does.not.exist.local",
                tcpPort
        );

        try {
            connectFuture.get(5, TimeUnit.SECONDS);
        } catch (Exception e) {
            // Connection should fail
        }

        // Assert
        assertThat(connector.isConnected()).isFalse();
    }

    @Test
    @DisplayName("A-06-06: 잘못된 포트로 연결 시 실패한다")
    void connectToInvalidPortFails() throws Exception {
        // Arrange
        createConnectorWithConfig(ConnectorConfig.builder()
                .requestTimeoutMs(3000)
                .heartbeatIntervalMs(10000)
                .build());

        // Act
        CompletableFuture<Void> connectFuture = connector.connectAsync(
                host,
                59999  // 사용하지 않는 포트
        );

        try {
            connectFuture.get(5, TimeUnit.SECONDS);
        } catch (Exception e) {
            // Connection should fail
        }

        // Assert
        assertThat(connector.isConnected()).isFalse();
    }

    @Test
    @DisplayName("A-06-07: 빈 MsgId 패킷도 처리된다")
    void emptyMsgIdPacketHandled() throws Exception {
        // Arrange
        createConnectorWithConfig(ConnectorConfig.builder()
                .requestTimeoutMs(5000)
                .heartbeatIntervalMs(10000)
                .build());

        CompletableFuture<Void> connectFuture = connector.connectAsync(host, tcpPort);
        connectFuture.get(5, TimeUnit.SECONDS);

        AuthenticateRequest authRequest = new AuthenticateRequest("empty-msgid-user", "valid_token");
        Packet authPacket = Packet.builder("AuthenticateRequest")
                .payload(authRequest.toByteArray())
                .build();
        connector.requestAsync(authPacket).get(5, TimeUnit.SECONDS);

        // Act - 빈 MsgId로 패킷 생성 시도
        Packet emptyPacket = Packet.builder("UnknownMessage")
                .payload(new byte[0])
                .build();
        Packet response = connector.requestAsync(emptyPacket)
                .get(5, TimeUnit.SECONDS);

        // Assert - 서버가 알 수 없는 메시지에 대해 기본 응답
        assertThat(response).isNotNull();
    }

    @Test
    @DisplayName("A-06-08: 동일 Connector로 여러 번 Disconnect 호출이 안전하다")
    void multipleDisconnectCallsSafe() throws Exception {
        // Arrange
        createConnectorWithConfig(ConnectorConfig.builder()
                .requestTimeoutMs(5000)
                .heartbeatIntervalMs(10000)
                .build());

        CompletableFuture<Void> connectFuture = connector.connectAsync(host, tcpPort);
        connectFuture.get(5, TimeUnit.SECONDS);

        // Act - 여러 번 Disconnect
        connector.disconnect();
        connector.disconnect();
        connector.disconnect();

        Thread.sleep(500);

        // Assert
        assertThat(connector.isConnected()).isFalse();
    }

    @Test
    @DisplayName("A-06-09: Close 후 연결 시도 시 예외가 발생한다")
    void connectAfterCloseThrows() throws Exception {
        // Arrange
        createConnectorWithConfig(ConnectorConfig.builder()
                .requestTimeoutMs(5000)
                .heartbeatIntervalMs(10000)
                .build());

        connector.close();

        // Act & Assert - Java connector throws IllegalStateException from checkInitialized()
        assertThatThrownBy(() -> {
            CompletableFuture<Void> connectFuture = connector.connectAsync(host, tcpPort);
            connectFuture.get(5, TimeUnit.SECONDS);
        }).isInstanceOf(IllegalStateException.class);

        connector = null;  // tearDown에서 다시 처리하지 않도록
    }

    @Test
    @DisplayName("A-06-10: 연결 중 Close가 안전하게 처리된다")
    void closeWhileConnected() throws Exception {
        // Arrange
        createConnectorWithConfig(ConnectorConfig.builder()
                .requestTimeoutMs(5000)
                .heartbeatIntervalMs(10000)
                .build());

        CompletableFuture<Void> connectFuture = connector.connectAsync(host, tcpPort);
        connectFuture.get(5, TimeUnit.SECONDS);

        assertThat(connector.isConnected()).isTrue();

        // Act
        connector.close();

        // Assert - Close 후에는 연결 상태 확인 불가
        connector = null;  // tearDown에서 다시 처리하지 않도록
    }

    @Test
    @DisplayName("A-06-11: 연결 직후 StageId는 0이다")
    void stageIdIsZeroBeforeAuthentication() throws Exception {
        // Arrange
        createConnectorWithConfig(ConnectorConfig.builder()
                .requestTimeoutMs(5000)
                .heartbeatIntervalMs(10000)
                .build());

        // Act
        CompletableFuture<Void> connectFuture = connector.connectAsync(host, tcpPort);
        connectFuture.get(5, TimeUnit.SECONDS);

        // Assert
        assertThat(connector.getStageId()).isZero();
    }

    @Test
    @DisplayName("A-06-12: 매우 긴 문자열도 에코할 수 있다")
    void veryLongStringEcho() throws Exception {
        // Arrange
        createConnectorWithConfig(ConnectorConfig.builder()
                .requestTimeoutMs(30000)
                .heartbeatIntervalMs(10000)
                .build());

        CompletableFuture<Void> connectFuture = connector.connectAsync(host, tcpPort);
        connectFuture.get(5, TimeUnit.SECONDS);

        AuthenticateRequest authRequest = new AuthenticateRequest("long-string-user", "valid_token");
        Packet authPacket = Packet.builder("AuthenticateRequest")
                .payload(authRequest.toByteArray())
                .build();
        connector.requestAsync(authPacket).get(5, TimeUnit.SECONDS);

        // 64KB 문자열
        String longContent = "X".repeat(65536);
        EchoRequest echoRequest = new EchoRequest(longContent, 1);

        // Act
        Packet packet = Packet.builder("EchoRequest")
                .payload(echoRequest.toByteArray())
                .build();
        Packet response = connector.requestAsync(packet)
                .get(30, TimeUnit.SECONDS);
        EchoReply reply = EchoReply.parseFrom(response.getPayload());

        // Assert
        assertThat(reply.content).isEqualTo(longContent);
    }

    @Test
    @DisplayName("A-06-13: 특수문자가 포함된 문자열도 에코할 수 있다")
    void specialCharactersEcho() throws Exception {
        // Arrange
        createConnectorWithConfig(ConnectorConfig.builder()
                .requestTimeoutMs(5000)
                .heartbeatIntervalMs(10000)
                .build());

        CompletableFuture<Void> connectFuture = connector.connectAsync(host, tcpPort);
        connectFuture.get(5, TimeUnit.SECONDS);

        AuthenticateRequest authRequest = new AuthenticateRequest("special-char-user", "valid_token");
        Packet authPacket = Packet.builder("AuthenticateRequest")
                .payload(authRequest.toByteArray())
                .build();
        connector.requestAsync(authPacket).get(5, TimeUnit.SECONDS);

        String specialContent = "Hello\0World\n\r\t\"'\\<>&한글日本語🎮";
        EchoRequest echoRequest = new EchoRequest(specialContent, 1);

        // Act
        Packet packet = Packet.builder("EchoRequest")
                .payload(echoRequest.toByteArray())
                .build();
        Packet response = connector.requestAsync(packet)
                .get(5, TimeUnit.SECONDS);
        EchoReply reply = EchoReply.parseFrom(response.getPayload());

        // Assert
        assertThat(reply.content).isEqualTo(specialContent);
    }
}
