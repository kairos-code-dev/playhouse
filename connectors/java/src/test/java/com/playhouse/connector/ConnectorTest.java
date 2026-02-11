package com.playhouse.connector;

import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.util.concurrent.CompletableFuture;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;

import static org.assertj.core.api.Assertions.*;

/**
 * Connector 단위 테스트
 */
class ConnectorTest {

    private Connector connector;

    @BeforeEach
    void setUp() {
        connector = new Connector();
    }

    @AfterEach
    void tearDown() {
        if (connector != null) {
            connector.close();
        }
    }

    @Test
    void testInitialization() {
        // Given
        ConnectorConfig config = ConnectorConfig.builder()
            .requestTimeoutMs(5000)
            .heartbeatIntervalMs(10000)
            .build();

        // When
        connector.init(config);

        // Then
        assertThat(connector.getConfig()).isEqualTo(config);
        assertThat(connector.isConnected()).isFalse();
        assertThat(connector.isAuthenticated()).isFalse();
    }

    @Test
    void testDefaultInitialization() {
        // When
        connector.init();

        // Then
        assertThat(connector.getConfig()).isNotNull();
        assertThat(connector.getConfig().getRequestTimeoutMs()).isEqualTo(30000);
    }

    @Test
    void testDoubleInitializationThrows() {
        // Given
        connector.init();

        // When/Then
        assertThatThrownBy(() -> connector.init())
            .isInstanceOf(IllegalStateException.class)
            .hasMessageContaining("already initialized");
    }

    @Test
    void testConnectWithoutInitThrows() {
        // When/Then
        assertThatThrownBy(() -> connector.connectAsync("localhost", 34001))
            .isInstanceOf(IllegalStateException.class)
            .hasMessageContaining("not initialized");
    }

    @Test
    void testSendWithoutConnectionNoThrow() {
        // Given
        connector.init();
        Packet packet = Packet.empty("TestMessage");

        // When/Then - send() should not throw when disconnected (logs warning instead)
        assertThatCode(() -> connector.send(packet)).doesNotThrowAnyException();
    }

    @Test
    void testStageIdDefaultValue() {
        // Given
        connector.init();

        // Then
        assertThat(connector.getStageId()).isZero();
    }

    @Test
    void testCallbackSetup() {
        // Given
        connector.init();
        AtomicBoolean connectCalled = new AtomicBoolean(false);
        AtomicBoolean receiveCalled = new AtomicBoolean(false);
        AtomicBoolean errorCalled = new AtomicBoolean(false);
        AtomicBoolean disconnectCalled = new AtomicBoolean(false);

        // When
        connector.setOnConnect(() -> connectCalled.set(true));
        connector.setOnReceive(packet -> receiveCalled.set(true));
        connector.setOnError((code, msg) -> errorCalled.set(true));
        connector.setOnDisconnect(() -> disconnectCalled.set(true));

        // Then - 콜백 설정 시 예외가 발생하지 않아야 함
        assertThat(connectCalled).isFalse();
        assertThat(receiveCalled).isFalse();
        assertThat(errorCalled).isFalse();
        assertThat(disconnectCalled).isFalse();
    }

    @Test
    void testClose() {
        // Given
        connector.init();

        // When
        connector.close();

        // Then
        assertThatThrownBy(() -> connector.connectAsync("localhost", 34001))
            .isInstanceOf(IllegalStateException.class);
    }

    @Test
    void testCloseIdempotent() {
        // Given
        connector.init();

        // When/Then - 여러 번 호출해도 예외가 발생하지 않아야 함
        assertThatCode(() -> {
            connector.close();
            connector.close();
            connector.close();
        }).doesNotThrowAnyException();
    }

    @Test
    void testMainThreadAction() {
        // Given
        connector.init();

        // When/Then - 예외가 발생하지 않아야 함
        assertThatCode(() -> connector.mainThreadAction())
            .doesNotThrowAnyException();
    }

    @Test
    void testRequestAsyncWithoutConnection() {
        // Given
        connector.init();
        Packet request = Packet.fromBytes("TestRequest", new byte[]{1, 2, 3});

        // When/Then - requestAsync returns a failed future with ConnectorException
        var future = connector.requestAsync(request);
        assertThat(future.isCompletedExceptionally()).isTrue();
        assertThatThrownBy(() -> future.join())
            .hasCauseInstanceOf(ConnectorException.class);
    }

    // ===== 동기 API 테스트 =====

    @Test
    void testConnectSyncWithoutInitThrows() {
        // When/Then
        assertThatThrownBy(() -> connector.connect("localhost", 34001))
            .isInstanceOf(IllegalStateException.class)
            .hasMessageContaining("not initialized");
    }

    @Test
    void testRequestSyncWithoutConnection() {
        // Given
        connector.init();
        Packet request = Packet.fromBytes("TestRequest", new byte[]{1, 2, 3});

        // When/Then - request() throws ConnectorException when not connected
        assertThatThrownBy(() -> connector.request(request))
            .isInstanceOf(ConnectorException.class);
    }

    @Test
    void testAuthenticateSyncWithPayloadWithoutConnection() {
        // Given
        connector.init();
        byte[] payload = new byte[]{1, 2, 3};

        // When/Then - authenticate() throws ConnectorException when not connected
        assertThatThrownBy(() -> connector.authenticate("service", "account", payload))
            .isInstanceOf(ConnectorException.class);
    }

    @Test
    void testAuthenticateSyncWithPacketWithoutConnection() {
        // Given
        connector.init();
        Packet authPacket = Packet.fromBytes("AuthRequest", new byte[]{1, 2, 3});

        // When/Then - authenticate() throws ConnectorException when not connected
        assertThatThrownBy(() -> connector.authenticate(authPacket))
            .isInstanceOf(ConnectorException.class);
    }

    // Note: 실제 서버 연결 테스트는 통합 테스트에서 수행
}
