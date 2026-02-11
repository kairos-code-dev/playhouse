package com.playhouse.connector.core;

import com.playhouse.connector.ConnectorErrorCode;
import com.playhouse.connector.Packet;
import com.playhouse.connector.support.BaseIntegrationTest;
import com.playhouse.connector.support.CreateStageResponse;
import com.playhouse.connector.support.TestMessages;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;

import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicReference;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

/**
 * C-08: 연결 해제 테스트
 * <p>
 * Connector의 Disconnect 메서드를 통해 정상적으로 연결을 끊을 수 있는지,
 * 연결 해제 후 상태가 올바른지 검증합니다.
 * </p>
 */
@DisplayName("C-08: Disconnection Tests")
public class C08_DisconnectionTests extends BaseIntegrationTest {

    @Test
    @DisplayName("C-08-01: Disconnect를 호출하면 연결이 해제된다")
    public void disconnect_afterConnection_disconnectsSuccessfully() throws Exception {
        // Given: 연결 및 인증 완료
        createStageAndConnect();
        authenticate("disconnectUser");

        assertThat(connector.isConnected()).as("초기 상태는 연결됨").isTrue();
        assertThat(connector.isAuthenticated()).as("인증도 완료됨").isTrue();

        // When: 연결 해제
        connector.disconnect();
        Thread.sleep(500); // 연결 해제 완료 대기

        // Then: 연결이 끊어져야 함
        assertThat(connector.isConnected())
                .as("Disconnect 후 연결이 끊어져야 함")
                .isFalse();
        assertThat(connector.isAuthenticated())
                .as("인증 상태도 false여야 함")
                .isFalse();
    }

    @Test
    @DisplayName("C-08-02: OnDisconnect 이벤트가 발생하지 않는다 (클라이언트에서 끊은 경우)")
    public void onDisconnect_whenClientDisconnects_doesNotTrigger() throws Exception {
        // Given: 연결 및 인증 완료
        createStageAndConnect();
        authenticate("clientDisconnectUser");

        boolean[] disconnectEventTriggered = {false};
        connector.setOnDisconnect(() -> disconnectEventTriggered[0] = true);

        // When: 클라이언트에서 연결 해제
        connector.disconnect();
        Thread.sleep(1000); // 이벤트 발생 대기

        // Then: OnDisconnect 이벤트가 발생하지 않아야 함 (의도적으로 끊은 경우)
        assertThat(disconnectEventTriggered[0])
                .as("클라이언트가 의도적으로 끊은 경우 OnDisconnect가 발생하지 않아야 함")
                .isFalse();
    }

    @Test
    @DisplayName("C-08-03: 연결 해제 후 Send는 실패한다")
    public void send_afterDisconnect_failsWithError() throws Exception {
        // Given: 연결 및 인증 후 연결 해제
        createStageAndConnect();
        authenticate("sendAfterDisconnectUser");
        connector.disconnect();
        Thread.sleep(500);

        AtomicReference<Integer> receivedErrorCode = new AtomicReference<>();
        connector.setOnError((errorCode, errorMessage) -> {
            receivedErrorCode.set(errorCode);
        });

        // When: 연결 해제 후 Send 시도
        TestMessages.EchoRequest echoRequest = new TestMessages.EchoRequest("Test", 1);
        Packet packet = Packet.builder("EchoRequest")
                .payload(echoRequest.toByteArray())
                .build();
        connector.send(packet);

        // MainThreadAction을 호출하여 에러 이벤트 처리
        Thread.sleep(100);
        connector.mainThreadAction();

        // Then: Disconnected 에러가 발생해야 함
        assertThat(receivedErrorCode.get())
                .as("연결 해제 후 Send는 Disconnected 에러를 발생시켜야 함")
                .isEqualTo(ConnectorErrorCode.DISCONNECTED.getCode());
    }

    @Test
    @DisplayName("C-08-04: 연결 해제 후 RequestAsync는 예외를 발생시킨다")
    public void requestAsync_afterDisconnect_throwsException() throws Exception {
        // Given: 연결 및 인증 후 연결 해제
        createStageAndConnect();
        authenticate("requestAfterDisconnectUser");
        connector.disconnect();
        Thread.sleep(500);

        // When: 연결 해제 후 RequestAsync 시도
        TestMessages.EchoRequest echoRequest = new TestMessages.EchoRequest("Test", 1);
        Packet packet = Packet.builder("EchoRequest")
                .payload(echoRequest.toByteArray())
                .build();

        // Then: ConnectorException이 발생해야 함
        assertThatThrownBy(() -> connector.requestAsync(packet).get(5, TimeUnit.SECONDS))
                .as("연결 해제 후 RequestAsync는 예외를 발생시켜야 함")
                .hasMessageContaining("Disconnected");
    }

    @Test
    @DisplayName("C-08-05: 연결 해제 후 재연결할 수 있다")
    public void reconnect_afterDisconnect_succeeds() throws Exception {
        // Given: 첫 번째 연결 및 해제
        createStageAndConnect();
        authenticate("reconnectUser1");
        connector.disconnect();
        Thread.sleep(500);

        assertThat(connector.isConnected()).as("연결이 끊어진 상태").isFalse();

        // When: 새로운 Stage로 재연결
        CreateStageResponse newStageInfo = testServer.createTestStage();
        connector.connectAsync(host, tcpPort).get(5, TimeUnit.SECONDS);

        // Then: 재연결이 성공해야 함
        assertThat(connector.isConnected())
                .as("재연결 후 연결 상태가 true")
                .isTrue();

        // 인증도 가능해야 함
        TestMessages.AuthenticateReply authReply = authenticate("reconnectUser2");
        assertThat(authReply.success)
                .as("재연결 후 인증이 성공해야 함")
                .isTrue();
    }

    @Test
    @DisplayName("C-08-06: 여러 번 Disconnect를 호출해도 안전하다")
    public void disconnect_multipleTimes_isSafe() throws Exception {
        // Given: 연결 완료
        createStageAndConnect();
        authenticate("multiDisconnectUser");

        // When: Disconnect를 여러 번 호출
        connector.disconnect();
        Thread.sleep(200);

        // Then: 예외가 발생하지 않아야 함
        org.assertj.core.api.Assertions.assertThatCode(() -> {
            connector.disconnect();
            connector.disconnect();
            connector.disconnect();
        }).as("여러 번 Disconnect를 호출해도 안전해야 함")
                .doesNotThrowAnyException();

        assertThat(connector.isConnected())
                .as("최종적으로 연결이 끊어져야 함")
                .isFalse();
    }

    @Test
    @DisplayName("C-08-07: Close는 자동으로 연결을 해제한다")
    public void close_automaticallyDisconnects() throws Exception {
        // Given: 새로운 Connector 생성 및 연결
        com.playhouse.connector.Connector tempConnector = new com.playhouse.connector.Connector();
        tempConnector.init(com.playhouse.connector.ConnectorConfig.defaultConfig());

        CreateStageResponse stageInfo = testServer.createTestStage();
        tempConnector.connectAsync(host, tcpPort).get(5, TimeUnit.SECONDS);

        TestMessages.AuthenticateRequest authRequest = new TestMessages.AuthenticateRequest("disposeUser", "valid_token");
        Packet authPacket = Packet.builder("AuthenticateRequest")
                .payload(authRequest.toByteArray())
                .build();
        tempConnector.requestAsync(authPacket).get(5, TimeUnit.SECONDS);

        assertThat(tempConnector.isConnected()).as("초기 연결 상태").isTrue();

        // When: Close 호출
        tempConnector.close();

        // Then: 연결이 해제되어야 함
        assertThat(tempConnector.isConnected())
                .as("Close 후 연결이 해제되어야 함")
                .isFalse();
    }

    @Test
    @DisplayName("C-08-08: 연결 해제 후 IsAuthenticated는 false를 반환한다")
    public void isAuthenticated_afterDisconnect_returnsFalse() throws Exception {
        // Given: 연결 및 인증 완료
        createStageAndConnect();
        authenticate("authCheckUser");

        assertThat(connector.isAuthenticated()).as("인증 완료 상태").isTrue();

        // When: 연결 해제
        connector.disconnect();
        Thread.sleep(500);

        // Then: IsAuthenticated가 false여야 함
        assertThat(connector.isAuthenticated())
                .as("연결 해제 후 IsAuthenticated는 false여야 함")
                .isFalse();
    }
}
