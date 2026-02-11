package com.playhouse.connector.advanced;

import com.playhouse.connector.Connector;
import com.playhouse.connector.ConnectorConfig;
import com.playhouse.connector.Packet;
import com.playhouse.connector.support.BaseIntegrationTest;
import com.playhouse.connector.support.TestMessages.*;
import org.junit.jupiter.api.*;

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicReference;

import static org.assertj.core.api.Assertions.*;

/**
 * A-01: WebSocket 연결 테스트
 * <p>
 * WebSocket 전송 계층을 통한 연결, 인증, 메시지 송수신 테스트.
 * UseWebsocket = true 설정으로 TCP 대신 WebSocket 사용.
 * </p>
 */
@DisplayName("A-01: WebSocket 연결 테스트")
@Tag("Advanced")
@Tag("WebSocket")
class A01_WebSocketConnectionTests extends BaseIntegrationTest {

    @Override
    @BeforeEach
    public void setUp() throws Exception {
        // 환경 변수에서 테스트 서버 설정 읽기
        host = System.getenv().getOrDefault("TEST_SERVER_HOST", "127.0.0.1");
        httpPort = Integer.parseInt(System.getenv().getOrDefault("TEST_SERVER_HTTP_PORT", "28080"));
        tcpPort = Integer.parseInt(System.getenv().getOrDefault("TEST_SERVER_TCP_PORT", "28001"));

        // 테스트 서버 클라이언트 초기화
        testServer = new com.playhouse.connector.support.TestServerClient(host, httpPort);

        // WebSocket Connector 설정
        connector = new Connector();
        connector.init(ConnectorConfig.builder()
                .useWebsocket(true)
                .webSocketPath("/ws")
                .requestTimeoutMs(5000)
                .heartbeatIntervalMs(10000)
                .build());

        stageInfo = testServer.createTestStage();
    }

    @Test
    @DisplayName("A-01-01: WebSocket으로 서버에 연결할 수 있다")
    void webSocketConnectionSuccess() throws Exception {
        // Act
        CompletableFuture<Void> connectFuture = connector.connectAsync(host, httpPort); // WebSocket은 HTTP 포트 사용
        connectFuture.get(5, TimeUnit.SECONDS);

        // Assert
        assertThat(connector.isConnected()).isTrue();
    }

    @Test
    @DisplayName("A-01-02: WebSocket 연결 후 인증이 성공한다")
    void webSocketAuthenticationSuccess() throws Exception {
        // Arrange
        CompletableFuture<Void> connectFuture = connector.connectAsync(host, httpPort);
        connectFuture.get(5, TimeUnit.SECONDS);

        AuthenticateRequest authRequest = new AuthenticateRequest("ws-user-1", "valid_token");

        // Act
        Packet requestPacket = Packet.builder("AuthenticateRequest")
                .payload(authRequest.toByteArray())
                .build();

        Packet responsePacket = connector.requestAsync(requestPacket)
                .get(5, TimeUnit.SECONDS);

        AuthenticateReply authReply = AuthenticateReply.parseFrom(responsePacket.getPayload());

        // Assert
        assertThat(authReply.success).isTrue();
        assertThat(responsePacket.getMsgId()).isEqualTo("AuthenticateReply");
    }

    @Test
    @DisplayName("A-01-03: WebSocket으로 Echo Request-Response가 동작한다")
    void webSocketEchoRequestResponse() throws Exception {
        // Arrange
        CompletableFuture<Void> connectFuture = connector.connectAsync(host, httpPort);
        connectFuture.get(5, TimeUnit.SECONDS);

        AuthenticateRequest authRequest = new AuthenticateRequest("ws-user-2", "valid_token");
        Packet authPacket = Packet.builder("AuthenticateRequest")
                .payload(authRequest.toByteArray())
                .build();
        connector.requestAsync(authPacket).get(5, TimeUnit.SECONDS);

        EchoRequest echoRequest = new EchoRequest("Hello WebSocket!", 42);

        // Act
        Packet requestPacket = Packet.builder("EchoRequest")
                .payload(echoRequest.toByteArray())
                .build();

        Packet responsePacket = connector.requestAsync(requestPacket)
                .get(5, TimeUnit.SECONDS);

        EchoReply echoReply = EchoReply.parseFrom(responsePacket.getPayload());

        // Assert
        assertThat(echoReply.content).isEqualTo("Hello WebSocket!");
        assertThat(echoReply.sequence).isEqualTo(42);
    }

    @Test
    @DisplayName("A-01-04: WebSocket으로 Push 메시지를 수신할 수 있다")
    void webSocketPushMessageReceived() throws Exception {
        // Arrange
        CompletableFuture<Void> connectFuture = connector.connectAsync(host, httpPort);
        connectFuture.get(5, TimeUnit.SECONDS);

        AuthenticateRequest authRequest = new AuthenticateRequest("ws-user-3", "valid_token");
        Packet authPacket = Packet.builder("AuthenticateRequest")
                .payload(authRequest.toByteArray())
                .build();
        connector.requestAsync(authPacket).get(5, TimeUnit.SECONDS);

        List<Packet> receivedMessages = new ArrayList<>();
        connector.setOnReceive((packet) -> {
            receivedMessages.add(packet);
        });

        BroadcastRequest broadcastRequest = new BroadcastRequest("WebSocket Broadcast Test");

        // Act
        Packet requestPacket = Packet.builder("BroadcastRequest")
                .payload(broadcastRequest.toByteArray())
                .build();
        connector.send(requestPacket);

        // Wait for push message
        boolean received = waitForCondition(() -> !receivedMessages.isEmpty(), 5000);

        // Assert
        assertThat(received).isTrue();
        assertThat(receivedMessages).isNotEmpty();
    }

    @Test
    @DisplayName("A-01-05: WebSocket 연결 해제 후 재연결이 가능하다")
    void webSocketReconnection() throws Exception {
        // Arrange
        CompletableFuture<Void> connectFuture = connector.connectAsync(host, httpPort);
        connectFuture.get(5, TimeUnit.SECONDS);
        assertThat(connector.isConnected()).isTrue();

        // Act - Disconnect
        connector.disconnect();
        Thread.sleep(500);
        assertThat(connector.isConnected()).isFalse();

        // Act - Reconnect
        var newStage = testServer.createTestStage();
        CompletableFuture<Void> reconnectFuture = connector.connectAsync(host, httpPort);
        reconnectFuture.get(5, TimeUnit.SECONDS);

        // Assert
        assertThat(connector.isConnected()).isTrue();
    }

    @Test
    @DisplayName("A-01-06: WebSocket으로 병렬 요청을 처리할 수 있다")
    void webSocketParallelRequests() throws Exception {
        // Arrange
        CompletableFuture<Void> connectFuture = connector.connectAsync(host, httpPort);
        connectFuture.get(5, TimeUnit.SECONDS);

        AuthenticateRequest authRequest = new AuthenticateRequest("ws-user-4", "valid_token");
        Packet authPacket = Packet.builder("AuthenticateRequest")
                .payload(authRequest.toByteArray())
                .build();
        connector.requestAsync(authPacket).get(5, TimeUnit.SECONDS);

        // Act - Send 10 parallel requests
        List<CompletableFuture<Packet>> tasks = new ArrayList<>();
        for (int i = 0; i < 10; i++) {
            EchoRequest echoRequest = new EchoRequest("Parallel " + i, i);
            Packet packet = Packet.builder("EchoRequest")
                    .payload(echoRequest.toByteArray())
                    .build();
            tasks.add(connector.requestAsync(packet));
        }

        CompletableFuture<Void> allTasks = CompletableFuture.allOf(
                tasks.toArray(new CompletableFuture[0])
        );
        allTasks.get(10, TimeUnit.SECONDS);

        // Assert
        assertThat(tasks).hasSize(10);
        for (CompletableFuture<Packet> task : tasks) {
            Packet response = task.get();
            EchoReply echoReply = EchoReply.parseFrom(response.getPayload());
            assertThat(echoReply.content).startsWith("Parallel");
        }
    }

    @Test
    @DisplayName("A-01-07: OnConnect 이벤트가 WebSocket 연결 시 발생한다")
    void webSocketOnConnectEventFired() throws Exception {
        // Arrange
        AtomicBoolean connectEventFired = new AtomicBoolean(false);

        connector.setOnConnect(() -> {
            connectEventFired.set(true);
        });

        // Act
        connector.connectAsync(host, httpPort);

        // Wait for connection with MainThreadAction
        boolean eventFired = waitForCondition(() -> connectEventFired.get(), 5000);

        // Assert
        assertThat(eventFired).isTrue();
    }
}
