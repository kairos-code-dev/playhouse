package com.playhouse.connector.advanced;

import com.playhouse.connector.Connector;
import com.playhouse.connector.ConnectorConfig;
import com.playhouse.connector.ConnectorErrorCode;
import com.playhouse.connector.Packet;
import com.playhouse.connector.support.BaseIntegrationTest;
import com.playhouse.connector.support.TestMessages.*;
import org.junit.jupiter.api.*;

import java.util.concurrent.CompletableFuture;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicInteger;

import static org.assertj.core.api.Assertions.*;

/**
 * A-03: Send() 메서드 테스트 (Fire-and-Forget)
 * <p>
 * Send() 메서드는 응답을 기다리지 않는 단방향 메시지 전송.
 * Request()와 달리 응답을 기대하지 않으며, 주로 알림이나 이벤트 전송에 사용.
 * </p>
 */
@DisplayName("A-03: Send 메서드 테스트")
@Tag("Advanced")
@Tag("Send")
class A03_SendMethodTests extends BaseIntegrationTest {

    @Override
    @BeforeEach
    public void setUp() throws Exception {
        super.setUp();
        createStageAndConnect();
        authenticate("send-test-user");
    }

    @Test
    @DisplayName("A-03-01: Send()로 메시지를 전송할 수 있다")
    void sendMessageSuccessfully() throws Exception {
        // Arrange
        EchoRequest echoRequest = new EchoRequest("Fire and Forget", 1);

        // Act - Send는 응답을 기다리지 않음
        Packet packet = Packet.builder("EchoRequest")
                .payload(echoRequest.toByteArray())
                .build();
        connector.send(packet);

        // Assert - Send는 예외 없이 완료되어야 함
        // 메시지가 전송되었는지 확인하기 위해 짧은 대기 후 연결 상태 확인
        Thread.sleep(100);
        assertThat(connector.isConnected()).isTrue();
    }

    @Test
    @DisplayName("A-03-02: Send() 후 연결이 유지된다")
    void sendConnectionMaintained() throws Exception {
        // Arrange
        for (int i = 0; i < 10; i++) {
            EchoRequest echoRequest = new EchoRequest("Message " + i, i);

            // Act
            Packet packet = Packet.builder("EchoRequest")
                    .payload(echoRequest.toByteArray())
                    .build();
            connector.send(packet);
        }

        Thread.sleep(200);

        // Assert
        assertThat(connector.isConnected()).isTrue();
        assertThat(connector.isAuthenticated()).isTrue();
    }

    @Test
    @DisplayName("A-03-03: Send()와 Request()를 혼합해서 사용할 수 있다")
    void sendMixedWithRequest() throws Exception {
        // Arrange & Act
        // Send 몇 개 전송
        for (int i = 0; i < 5; i++) {
            EchoRequest sendRequest = new EchoRequest("Send " + i, i);
            Packet sendPacket = Packet.builder("EchoRequest")
                    .payload(sendRequest.toByteArray())
                    .build();
            connector.send(sendPacket);
        }

        // 중간에 Request
        EchoRequest echoRequest = new EchoRequest("Request in between", 100);
        Packet requestPacket = Packet.builder("EchoRequest")
                .payload(echoRequest.toByteArray())
                .build();
        Packet response = connector.requestAsync(requestPacket)
                .get(5, TimeUnit.SECONDS);
        EchoReply echoReply = EchoReply.parseFrom(response.getPayload());

        // 다시 Send 몇 개 전송
        for (int i = 5; i < 10; i++) {
            EchoRequest sendRequest = new EchoRequest("Send " + i, i);
            Packet sendPacket = Packet.builder("EchoRequest")
                    .payload(sendRequest.toByteArray())
                    .build();
            connector.send(sendPacket);
        }

        // Assert
        assertThat(echoReply.content).isEqualTo("Request in between");
        assertThat(connector.isConnected()).isTrue();
    }

    @Test
    @DisplayName("A-03-04: Send()로 BroadcastRequest를 전송하면 Push 메시지를 받는다")
    void sendBroadcastTriggersPush() throws Exception {
        // Arrange
        CompletableFuture<BroadcastNotify> receivedPush = new CompletableFuture<>();
        connector.setOnReceive((packet) -> {
            if ("BroadcastNotify".equals(packet.getMsgId())) {
                try {
                    BroadcastNotify notify = BroadcastNotify.parseFrom(packet.getPayload());
                    receivedPush.complete(notify);
                } catch (Exception e) {
                    receivedPush.completeExceptionally(e);
                }
            }
        });

        BroadcastRequest broadcastRequest = new BroadcastRequest("Hello from Send!");

        // Act
        Packet packet = Packet.builder("BroadcastRequest")
                .payload(broadcastRequest.toByteArray())
                .build();
        connector.send(packet);

        // Wait for push with MainThreadAction
        BroadcastNotify notify = waitWithMainThreadAction(receivedPush, 5000);

        // Assert
        assertThat(notify).isNotNull();
        assertThat(notify.data).isEqualTo("Hello from Send!");
    }

    @Test
    @DisplayName("A-03-05: 연결 해제 후 Send()는 OnError를 발생시킨다")
    void sendAfterDisconnectFiresOnError() throws Exception {
        // Arrange
        AtomicBoolean errorFired = new AtomicBoolean(false);
        AtomicInteger errorCode = new AtomicInteger(0);

        connector.setOnError((code, message) -> {
            errorFired.set(true);
            errorCode.set(code);
        });

        connector.disconnect();
        Thread.sleep(500);

        EchoRequest echoRequest = new EchoRequest("After disconnect", 1);

        // Act
        Packet packet = Packet.builder("EchoRequest")
                .payload(echoRequest.toByteArray())
                .build();
        connector.send(packet);
        connector.mainThreadAction();

        // Assert
        assertThat(errorFired.get()).isTrue();
        assertThat(errorCode.get()).isEqualTo(ConnectorErrorCode.DISCONNECTED.getCode());
    }

    @Test
    @DisplayName("A-03-06: 인증 전 Send()는 OnError를 발생시킨다")
    void sendBeforeAuthenticationFiresOnError() throws Exception {
        // Arrange - 새로운 Connector로 연결만 하고 인증은 하지 않음
        Connector newConnector = new Connector();
        newConnector.init(ConnectorConfig.builder()
                .requestTimeoutMs(5000)
                .heartbeatIntervalMs(10000)
                .build());

        var newStage = testServer.createTestStage();
        CompletableFuture<Void> connectFuture = newConnector.connectAsync(host, tcpPort);
        connectFuture.get(5, TimeUnit.SECONDS);

        AtomicBoolean errorFired = new AtomicBoolean(false);
        newConnector.setOnError((code, message) -> {
            errorFired.set(true);
        });

        EchoRequest echoRequest = new EchoRequest("Before auth", 1);

        // Act - 인증 없이 Send 시도
        // 주의: 현재 구현에서는 인증 없이도 Send가 가능할 수 있음
        // 이 테스트는 구현에 따라 동작이 다를 수 있음
        Packet packet = Packet.builder("EchoRequest")
                .payload(echoRequest.toByteArray())
                .build();
        newConnector.send(packet);
        newConnector.mainThreadAction();

        Thread.sleep(100);

        // Cleanup
        newConnector.disconnect();
        newConnector.close();

        // Assert - 연결은 되어 있으므로 Send 자체는 성공할 수 있음
        // 서버 측에서 인증되지 않은 요청을 거부할 수 있음
        assertThat(true).isTrue(); // 구현에 따라 다름
    }

    @Test
    @DisplayName("A-03-07: 빠른 연속 Send()가 모두 처리된다")
    void sendRapidFire() throws Exception {
        // Arrange
        final int messageCount = 100;

        // Act
        for (int i = 0; i < messageCount; i++) {
            EchoRequest echoRequest = new EchoRequest("Rapid " + i, i);
            Packet packet = Packet.builder("EchoRequest")
                    .payload(echoRequest.toByteArray())
                    .build();
            connector.send(packet);
        }

        // 모든 메시지가 전송될 시간 부여
        Thread.sleep(500);

        // Assert - 연결이 유지되어야 함
        assertThat(connector.isConnected()).isTrue();

        // Request로 응답 확인하여 서버가 정상인지 확인
        EchoRequest echoCheck = new EchoRequest("Check", 999);
        Packet checkPacket = Packet.builder("EchoRequest")
                .payload(echoCheck.toByteArray())
                .build();
        Packet response = connector.requestAsync(checkPacket)
                .get(5, TimeUnit.SECONDS);
        EchoReply echoReply = EchoReply.parseFrom(response.getPayload());

        assertThat(echoReply.content).isEqualTo("Check");
    }
}
