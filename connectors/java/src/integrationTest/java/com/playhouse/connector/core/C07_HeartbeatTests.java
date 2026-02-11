package com.playhouse.connector.core;

import com.playhouse.connector.Connector;
import com.playhouse.connector.ConnectorConfig;
import com.playhouse.connector.support.BaseIntegrationTest;
import com.playhouse.connector.support.CreateStageResponse;
import com.playhouse.connector.support.TestMessages;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;

import java.util.concurrent.TimeUnit;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * C-07: Heartbeat 자동 처리 테스트
 * <p>
 * Connector가 자동으로 Heartbeat를 전송하고,
 * 장시간 연결을 유지할 수 있는지 검증합니다.
 * </p>
 */
@DisplayName("C-07: Heartbeat Tests")
public class C07_HeartbeatTests extends BaseIntegrationTest {

    @Test
    @DisplayName("C-07-01: 연결 후 장시간 유지되어도 연결이 끊어지지 않는다")
    public void connection_maintainedOverTime_staysConnected() throws Exception {
        // Given: 연결 및 인증 완료
        createStageAndConnect();
        authenticate("heartbeatUser");

        assertThat(connector.isConnected()).isTrue();

        // When: 5초 동안 아무 동작 없이 대기 (Heartbeat가 자동으로 전송될 것임)
        Thread.sleep(5000);

        // Then: 연결이 유지되어야 함
        assertThat(connector.isConnected())
                .as("5초 후에도 연결이 유지되어야 함")
                .isTrue();
        assertThat(connector.isAuthenticated())
                .as("인증 상태도 유지되어야 함")
                .isTrue();
    }

    @Test
    @DisplayName("C-07-02: Heartbeat 주기 동안 Echo 요청이 정상 동작한다")
    public void echoRequest_duringHeartbeatPeriod_worksCorrectly() throws Exception {
        // Given: 연결 및 인증 완료
        createStageAndConnect();
        authenticate("echoHeartbeatUser");

        // When: 2초 대기 후 Echo 요청
        Thread.sleep(2000);
        TestMessages.EchoReply echoReply = echo("After Heartbeat", 1);

        // Then: Echo가 정상 동작해야 함
        assertThat(echoReply).isNotNull();
        assertThat(echoReply.content).isEqualTo("After Heartbeat");

        // 연결도 유지되어야 함
        assertThat(connector.isConnected()).isTrue();
    }

    @Test
    @DisplayName("C-07-03: 짧은 Heartbeat 간격으로 설정해도 정상 동작한다")
    public void heartbeat_withShortInterval_worksCorrectly() throws Exception {
        // Given: 짧은 Heartbeat 간격 (1초) 설정
        connector.close();
        connector = new Connector();
        connector.init(ConnectorConfig.builder()
                .requestTimeoutMs(5000)
                .heartbeatIntervalMs(1000) // 1초마다 Heartbeat
                .build());

        // When: 연결 및 인증
        createStageAndConnect();
        authenticate("shortHeartbeatUser");

        // 3초 대기 (3번의 Heartbeat가 전송될 것임)
        Thread.sleep(3000);

        // Then: 연결이 유지되어야 함
        assertThat(connector.isConnected())
                .as("짧은 Heartbeat 간격에도 연결이 유지되어야 함")
                .isTrue();

        // Echo도 정상 동작해야 함
        TestMessages.EchoReply echoReply = echo("Short Interval Test", 1);
        assertThat(echoReply.content).isEqualTo("Short Interval Test");
    }

    @Test
    @DisplayName("C-07-04: Heartbeat 중에도 메시지 송수신이 정상 동작한다")
    public void messageTransmission_duringHeartbeat_worksCorrectly() throws Exception {
        // Given: 연결 및 인증 완료
        createStageAndConnect();
        authenticate("transmitUser");

        // When: 10초 동안 주기적으로 Echo 요청 (Heartbeat와 동시에)
        for (int i = 1; i <= 5; i++) {
            TestMessages.EchoReply echoReply = echo("Message " + i, i);
            assertThat(echoReply.content)
                    .as(i + "번째 메시지가 정상 전송되어야 함")
                    .isEqualTo("Message " + i);

            Thread.sleep(2000); // 2초마다 전송
        }

        // Then: 모든 메시지가 정상 전송되고 연결이 유지되어야 함
        assertThat(connector.isConnected())
                .as("10초 후에도 연결이 유지되어야 함")
                .isTrue();
    }

    @Test
    @DisplayName("C-07-05: 연결 유지 중 OnDisconnect 이벤트가 발생하지 않는다")
    public void onDisconnect_duringNormalOperation_doesNotTrigger() throws Exception {
        // Given: 연결 및 인증 완료
        createStageAndConnect();
        authenticate("noDisconnectUser");

        boolean[] disconnectTriggered = {false};
        connector.setOnDisconnect(() -> disconnectTriggered[0] = true);

        // When: 5초 동안 대기
        Thread.sleep(5000);

        // Then: OnDisconnect가 발생하지 않아야 함
        assertThat(disconnectTriggered[0])
                .as("정상 동작 중에는 OnDisconnect가 발생하지 않아야 함")
                .isFalse();
        assertThat(connector.isConnected())
                .as("연결이 유지되어야 함")
                .isTrue();
    }

    @Test
    @DisplayName("C-07-06: 여러 Connector가 동시에 Heartbeat를 유지할 수 있다")
    public void multipleConnectors_maintainHeartbeat_simultaneously() throws Exception {
        // Given: 3개의 Connector 생성 및 연결
        CreateStageResponse stage1 = testServer.createTestStage();
        CreateStageResponse stage2 = testServer.createTestStage();
        CreateStageResponse stage3 = testServer.createTestStage();

        Connector connector1 = new Connector();
        Connector connector2 = new Connector();
        Connector connector3 = new Connector();

        try {
            connector1.init(ConnectorConfig.defaultConfig());
            connector2.init(ConnectorConfig.defaultConfig());
            connector3.init(ConnectorConfig.defaultConfig());


            connector1.connectAsync(host, tcpPort).get(5, TimeUnit.SECONDS);
            connector2.connectAsync(host, tcpPort).get(5, TimeUnit.SECONDS);
            connector3.connectAsync(host, tcpPort).get(5, TimeUnit.SECONDS);

            // 모두 인증
            TestMessages.AuthenticateRequest auth1Request = new TestMessages.AuthenticateRequest("user1", "valid_token");
            TestMessages.AuthenticateRequest auth2Request = new TestMessages.AuthenticateRequest("user2", "valid_token");
            TestMessages.AuthenticateRequest auth3Request = new TestMessages.AuthenticateRequest("user3", "valid_token");

            connector1.requestAsync(com.playhouse.connector.Packet.builder("AuthenticateRequest")
                    .payload(auth1Request.toByteArray()).build()).get(5, TimeUnit.SECONDS);
            connector2.requestAsync(com.playhouse.connector.Packet.builder("AuthenticateRequest")
                    .payload(auth2Request.toByteArray()).build()).get(5, TimeUnit.SECONDS);
            connector3.requestAsync(com.playhouse.connector.Packet.builder("AuthenticateRequest")
                    .payload(auth3Request.toByteArray()).build()).get(5, TimeUnit.SECONDS);

            // When: 5초 대기 (모든 Connector의 Heartbeat가 동작)
            Thread.sleep(5000);

            // Then: 모든 Connector의 연결이 유지되어야 함
            assertThat(connector1.isConnected())
                    .as("Connector 1의 연결이 유지되어야 함")
                    .isTrue();
            assertThat(connector2.isConnected())
                    .as("Connector 2의 연결이 유지되어야 함")
                    .isTrue();
            assertThat(connector3.isConnected())
                    .as("Connector 3의 연결이 유지되어야 함")
                    .isTrue();
        } finally {
            connector1.close();
            connector2.close();
            connector3.close();
        }
    }
}
