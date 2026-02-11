package com.playhouse.connector.advanced;

import com.playhouse.connector.Connector;
import com.playhouse.connector.ConnectorConfig;
import com.playhouse.connector.Packet;
import com.playhouse.connector.support.CreateStageResponse;
import com.playhouse.connector.support.TestMessages.*;
import com.playhouse.connector.support.TestServerClient;
import org.junit.jupiter.api.*;

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.TimeUnit;

import static org.assertj.core.api.Assertions.*;

/**
 * A-05: 다중 Connector 동시 사용 테스트
 * <p>
 * 여러 Connector 인스턴스를 동시에 사용하여 독립적인 연결과 통신이 가능한지 검증.
 * 실제 게임에서 여러 서버에 동시 연결하거나 테스트 시 여러 클라이언트 시뮬레이션에 사용.
 * </p>
 */
@DisplayName("A-05: 다중 Connector 테스트")
@Tag("Advanced")
@Tag("MultipleConnectors")
class A05_MultipleConnectorTests {

    private TestServerClient testServer;
    private final List<Connector> connectors = new ArrayList<>();
    private final List<CreateStageResponse> stages = new ArrayList<>();

    private String host;
    private int tcpPort;
    private int httpPort;

    @BeforeEach
    void setUp() throws Exception {
        // 환경 변수에서 테스트 서버 설정 읽기
        host = System.getenv().getOrDefault("TEST_SERVER_HOST", "127.0.0.1");
        httpPort = Integer.parseInt(System.getenv().getOrDefault("TEST_SERVER_HTTP_PORT", "28080"));
        tcpPort = Integer.parseInt(System.getenv().getOrDefault("TEST_SERVER_TCP_PORT", "28001"));

        testServer = new TestServerClient(host, httpPort);

        // 5개의 독립적인 Stage 생성
        for (int i = 0; i < 5; i++) {
            stages.add(testServer.createTestStage());
        }
    }

    @AfterEach
    void tearDown() throws Exception {
        for (Connector connector : connectors) {
            if (connector.isConnected()) {
                connector.disconnect();
                Thread.sleep(50);
            }
            connector.close();
        }
        connectors.clear();

        if (testServer != null) {
            testServer.close();
        }
    }

    private Connector createConnector() {
        return createConnector(10000);
    }

    private Connector createConnector(int requestTimeoutMs) {
        Connector connector = new Connector();
        connector.init(ConnectorConfig.builder()
                .requestTimeoutMs(requestTimeoutMs)
                .heartbeatIntervalMs(10000)
                .build());
        connectors.add(connector);
        return connector;
    }

    @Test
    @DisplayName("A-05-01: 여러 Connector가 동시에 연결할 수 있다")
    void multipleConnectorsConnectSimultaneously() throws Exception {
        // Arrange
        List<Connector> testConnectors = new ArrayList<>();
        for (int i = 0; i < 5; i++) {
            testConnectors.add(createConnector());
        }

        // Act - 동시에 연결
        List<CompletableFuture<Void>> connectTasks = new ArrayList<>();
        for (int i = 0; i < testConnectors.size(); i++) {
            Connector connector = testConnectors.get(i);
            connectTasks.add(connector.connectAsync(host, tcpPort));
        }

        CompletableFuture<Void> allTasks = CompletableFuture.allOf(
                connectTasks.toArray(new CompletableFuture[0])
        );
        allTasks.get(10, TimeUnit.SECONDS);

        // Assert
        for (Connector connector : testConnectors) {
            assertThat(connector.isConnected()).isTrue();
        }
    }

    @Test
    @DisplayName("A-05-02: 여러 Connector가 독립적으로 인증할 수 있다")
    void multipleConnectorsAuthenticateIndependently() throws Exception {
        // Arrange
        List<Connector> testConnectors = new ArrayList<>();
        for (int i = 0; i < 3; i++) {
            Connector connector = createConnector();
            CompletableFuture<Void> connectFuture = connector.connectAsync(host, tcpPort);
            connectFuture.get(5, TimeUnit.SECONDS);
            testConnectors.add(connector);
        }

        // Act - 각각 다른 사용자로 인증
        List<CompletableFuture<Packet>> authTasks = new ArrayList<>();
        for (int i = 0; i < testConnectors.size(); i++) {
            Connector connector = testConnectors.get(i);
            AuthenticateRequest authRequest = new AuthenticateRequest("user-" + i, "valid_token");
            Packet packet = Packet.builder("AuthenticateRequest")
                    .payload(authRequest.toByteArray())
                    .build();
            authTasks.add(connector.requestAsync(packet));
        }

        CompletableFuture<Void> allTasks = CompletableFuture.allOf(
                authTasks.toArray(new CompletableFuture[0])
        );
        allTasks.get(10, TimeUnit.SECONDS);

        // Assert - Verify all auth responses are successful
        for (int i = 0; i < authTasks.size(); i++) {
            Packet response = authTasks.get(i).get();
            AuthenticateReply reply = AuthenticateReply.parseFrom(response.getPayload());
            assertThat(reply.success).isTrue();
            assertThat(reply.receivedUserId).isEqualTo("user-" + i);
        }
    }

    @Test
    @DisplayName("A-05-03: 여러 Connector가 동시에 요청을 보낼 수 있다")
    void multipleConnectorsSendRequestsSimultaneously() throws Exception {
        // Arrange
        List<Connector> testConnectors = new ArrayList<>();
        for (int i = 0; i < 3; i++) {
            Connector connector = createConnector();
            CompletableFuture<Void> connectFuture = connector.connectAsync(host, tcpPort);
            connectFuture.get(5, TimeUnit.SECONDS);

            AuthenticateRequest authRequest = new AuthenticateRequest("parallel-user-" + i, "valid_token");
            Packet authPacket = Packet.builder("AuthenticateRequest")
                    .payload(authRequest.toByteArray())
                    .build();
            connector.requestAsync(authPacket).get(5, TimeUnit.SECONDS);

            testConnectors.add(connector);
        }

        // Act - 동시에 Echo 요청
        List<CompletableFuture<Packet>> requestTasks = new ArrayList<>();
        for (int i = 0; i < testConnectors.size(); i++) {
            Connector connector = testConnectors.get(i);
            EchoRequest echoRequest = new EchoRequest("Hello from connector " + i, i);
            Packet packet = Packet.builder("EchoRequest")
                    .payload(echoRequest.toByteArray())
                    .build();
            requestTasks.add(connector.requestAsync(packet));
        }

        CompletableFuture<Void> allTasks = CompletableFuture.allOf(
                requestTasks.toArray(new CompletableFuture[0])
        );
        allTasks.get(10, TimeUnit.SECONDS);

        // Assert
        for (int i = 0; i < requestTasks.size(); i++) {
            Packet response = requestTasks.get(i).get();
            EchoReply reply = EchoReply.parseFrom(response.getPayload());
            assertThat(reply.content).isEqualTo("Hello from connector " + i);
            assertThat(reply.sequence).isEqualTo(i);
        }
    }

    @Test
    @DisplayName("A-05-04: 한 Connector 연결 해제가 다른 Connector에 영향을 주지 않는다")
    void connectorDisconnectDoesNotAffectOthers() throws Exception {
        // Arrange
        List<Connector> testConnectors = new ArrayList<>();
        for (int i = 0; i < 3; i++) {
            Connector connector = createConnector();
            CompletableFuture<Void> connectFuture = connector.connectAsync(host, tcpPort);
            connectFuture.get(5, TimeUnit.SECONDS);

            AuthenticateRequest authRequest = new AuthenticateRequest("disconnect-test-" + i, "valid_token");
            Packet authPacket = Packet.builder("AuthenticateRequest")
                    .payload(authRequest.toByteArray())
                    .build();
            connector.requestAsync(authPacket).get(5, TimeUnit.SECONDS);

            testConnectors.add(connector);
        }

        // Act - 첫 번째 Connector 연결 해제
        testConnectors.get(0).disconnect();
        Thread.sleep(500);

        // Assert - 다른 Connector들은 여전히 연결되어 있어야 함
        assertThat(testConnectors.get(0).isConnected()).isFalse();
        assertThat(testConnectors.get(1).isConnected()).isTrue();
        assertThat(testConnectors.get(2).isConnected()).isTrue();

        // 나머지 Connector로 요청 가능해야 함
        for (int i = 1; i < testConnectors.size(); i++) {
            EchoRequest echoRequest = new EchoRequest("Still connected", i);
            Packet packet = Packet.builder("EchoRequest")
                    .payload(echoRequest.toByteArray())
                    .build();
            Packet response = testConnectors.get(i).requestAsync(packet)
                    .get(5, TimeUnit.SECONDS);
            EchoReply reply = EchoReply.parseFrom(response.getPayload());
            assertThat(reply.content).isEqualTo("Still connected");
        }
    }

    @Test
    @DisplayName("A-05-05: 동일 Stage에 여러 Connector가 연결할 수 있다")
    void multipleConnectorsSameStage() throws Exception {
        // Arrange - 같은 Stage에 여러 Connector 연결
        CreateStageResponse sharedStage = stages.get(0);
        List<Connector> testConnectors = new ArrayList<>();

        for (int i = 0; i < 3; i++) {
            Connector connector = createConnector();
            CompletableFuture<Void> connectFuture = connector.connectAsync(host, tcpPort);
            connectFuture.get(5, TimeUnit.SECONDS);

            AuthenticateRequest authRequest = new AuthenticateRequest("same-stage-user-" + i, "valid_token");
            Packet authPacket = Packet.builder("AuthenticateRequest")
                    .payload(authRequest.toByteArray())
                    .build();
            connector.requestAsync(authPacket).get(5, TimeUnit.SECONDS);

            testConnectors.add(connector);
        }

        // Act & Assert - 각 Connector가 독립적으로 요청 처리 가능
        for (int i = 0; i < testConnectors.size(); i++) {
            EchoRequest echoRequest = new EchoRequest("User " + i + " message", i);
            Packet packet = Packet.builder("EchoRequest")
                    .payload(echoRequest.toByteArray())
                    .build();
            Packet response = testConnectors.get(i).requestAsync(packet)
                    .get(5, TimeUnit.SECONDS);
            EchoReply reply = EchoReply.parseFrom(response.getPayload());
            assertThat(reply.content).isEqualTo("User " + i + " message");
        }
    }

    @Test
    @DisplayName("A-05-06: 대량의 Connector 동시 연결/해제 테스트")
    void stressTestManyConnectors() throws Exception {
        // Arrange
        final int connectorCount = 10;
        List<Connector> testConnectors = new ArrayList<>();
        List<CreateStageResponse> extraStages = new ArrayList<>();

        // 추가 Stage 생성
        for (int i = 0; i < connectorCount; i++) {
            extraStages.add(testServer.createTestStage());
        }

        // Act - 동시에 연결
        List<CompletableFuture<Void>> connectTasks = new ArrayList<>();
        for (int i = 0; i < connectorCount; i++) {
            Connector connector = createConnector();
            testConnectors.add(connector);

            CreateStageResponse stage = extraStages.get(i);
            connectTasks.add(connector.connectAsync(host, tcpPort));
        }

        CompletableFuture<Void> allTasks = CompletableFuture.allOf(
                connectTasks.toArray(new CompletableFuture[0])
        );
        allTasks.get(15, TimeUnit.SECONDS);

        // Assert - 모든 연결 성공
        for (Connector connector : testConnectors) {
            assertThat(connector.isConnected()).isTrue();
        }

        // 동시에 해제
        for (Connector connector : testConnectors) {
            connector.disconnect();
        }

        Thread.sleep(500);

        // 모든 연결 해제 확인
        for (Connector connector : testConnectors) {
            assertThat(connector.isConnected()).isFalse();
        }
    }

    @Test
    @DisplayName("A-05-07: 각 Connector의 이벤트가 독립적으로 발생한다")
    void connectorEventsAreIndependent() throws Exception {
        // Arrange
        List<String> connector1Received = new ArrayList<>();
        List<String> connector2Received = new ArrayList<>();

        Connector connector1 = createConnector();
        Connector connector2 = createConnector();

        connector1.setOnReceive((packet) -> {
            try {
                BroadcastNotify notify = BroadcastNotify.parseFrom(packet.getPayload());
                connector1Received.add(notify.data);
            } catch (Exception e) {
                // Ignore parsing errors
            }
        });

        connector2.setOnReceive((packet) -> {
            try {
                BroadcastNotify notify = BroadcastNotify.parseFrom(packet.getPayload());
                connector2Received.add(notify.data);
            } catch (Exception e) {
                // Ignore parsing errors
            }
        });

        connector1.connectAsync(host, tcpPort).get(5, TimeUnit.SECONDS);

        connector2.connectAsync(host, tcpPort).get(5, TimeUnit.SECONDS);

        AuthenticateRequest auth1 = new AuthenticateRequest("event-user-1", "valid_token");
        Packet auth1Packet = Packet.builder("AuthenticateRequest")
                .payload(auth1.toByteArray())
                .build();
        connector1.requestAsync(auth1Packet).get(5, TimeUnit.SECONDS);

        AuthenticateRequest auth2 = new AuthenticateRequest("event-user-2", "valid_token");
        Packet auth2Packet = Packet.builder("AuthenticateRequest")
                .payload(auth2.toByteArray())
                .build();
        connector2.requestAsync(auth2Packet).get(5, TimeUnit.SECONDS);

        // Act - 각 Connector에서 Broadcast 요청
        BroadcastRequest broadcast1 = new BroadcastRequest("From Connector 1");
        Packet b1Packet = Packet.builder("BroadcastRequest")
                .payload(broadcast1.toByteArray())
                .build();
        connector1.send(b1Packet);

        BroadcastRequest broadcast2 = new BroadcastRequest("From Connector 2");
        Packet b2Packet = Packet.builder("BroadcastRequest")
                .payload(broadcast2.toByteArray())
                .build();
        connector2.send(b2Packet);

        // Wait for push messages
        long deadline = System.currentTimeMillis() + 5000;
        while ((connector1Received.isEmpty() || connector2Received.isEmpty())
                && System.currentTimeMillis() < deadline) {
            connector1.mainThreadAction();
            connector2.mainThreadAction();
            Thread.sleep(10);
        }

        // Assert - 각 Connector는 자신에게 온 메시지만 받아야 함
        assertThat(connector1Received).contains("From Connector 1");
        assertThat(connector2Received).contains("From Connector 2");
    }
}
