package com.playhouse.connector.core;

import com.playhouse.connector.support.BaseIntegrationTest;
import com.playhouse.connector.support.CreateStageResponse;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;

import java.util.concurrent.CompletableFuture;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * C-02: TCP 연결 테스트
 * <p>
 * Connector가 테스트 서버의 TCP 포트(28001)에 성공적으로 연결할 수 있는지 검증합니다.
 * </p>
 */
@DisplayName("C-02: TCP Connection Tests")
public class C02_TcpConnectionTests extends BaseIntegrationTest {

    @Test
    @DisplayName("C-02-01: Stage 생성 후 TCP 연결이 성공한다")
    public void connect_afterStageCreation_succeeds() throws Exception {
        // Given: Stage가 생성되어 있음
        stageInfo = testServer.createTestStage();

        // When: TCP 연결 시도
        CompletableFuture<Void> connectFuture = connector.connectAsync(host, tcpPort);
        connectFuture.get(5, TimeUnit.SECONDS);

        // Then: 연결이 성공해야 함
        assertThat(connector.isConnected()).isTrue();
        assertThat(connector.getStageId()).isZero();
    }

    @Test
    @DisplayName("C-02-02: 연결 후 IsConnected는 true를 반환한다")
    public void isConnected_afterConnection_returnsTrue() throws Exception {
        // Given: 연결되지 않은 상태
        assertThat(connector.isConnected()).isFalse();

        // When: 연결 성공
        createStageAndConnect();

        // Then: IsConnected가 true를 반환해야 함
        assertThat(connector.isConnected()).isTrue();
    }

    @Test
    @DisplayName("C-02-03: 연결 전 IsAuthenticated는 false를 반환한다")
    public void isAuthenticated_beforeAuthentication_returnsFalse() throws Exception {
        // Given & When: 연결만 성공한 상태 (인증 전)
        createStageAndConnect();

        // Then: 인증 전이므로 IsAuthenticated는 false여야 함
        assertThat(connector.isAuthenticated()).isFalse();
    }

    @Test
    @DisplayName("C-02-04: OnConnect 이벤트가 성공 결과로 발생한다")
    public void onConnect_event_triggersWithSuccess() throws Exception {
        // Given: Stage 생성
        stageInfo = testServer.createTestStage();

        AtomicBoolean connectResult = new AtomicBoolean(false);
        AtomicBoolean eventTriggered = new AtomicBoolean(false);

        connector.setOnConnect(() -> {
            connectResult.set(true);
            eventTriggered.set(true);
        });

        // When: 연결 시도
        connector.connectAsync(host, tcpPort);

        // OnConnect 이벤트 대기 (MainThreadAction 호출하면서 최대 5초)
        boolean completed = waitForCondition(eventTriggered::get, 5000);

        // Then: 이벤트가 발생하고 성공 결과를 전달해야 함
        assertThat(completed).isTrue();
        assertThat(eventTriggered.get()).isTrue();
        assertThat(connectResult.get()).isTrue();
    }

    @Test
    @DisplayName("C-02-05: 잘못된 Stage ID로 연결해도 TCP 연결은 성공한다")
    public void connect_withInvalidStageId_tcpConnectionSucceeds() throws Exception {
        // Given: 존재하지 않는 Stage ID
        // When: 잘못된 Stage ID로 연결 시도
        CompletableFuture<Void> connectFuture = connector.connectAsync(host, tcpPort);
        connectFuture.get(5, TimeUnit.SECONDS);

        // Then: TCP 연결 자체는 성공함 (Stage ID 검증은 서버 레벨에서 나중에 이루어짐)
        assertThat(connector.isConnected()).isTrue();
        assertThat(connector.isAuthenticated()).isFalse();
    }

    @Test
    @DisplayName("C-02-06: 동일한 Connector로 재연결할 수 있다")
    public void connect_multipleTimes_succeeds() throws Exception {
        // Given: 첫 번째 연결
        createStageAndConnect();
        assertThat(connector.isConnected()).isTrue();

        // When: 연결 해제 후 재연결
        connector.disconnect();
        Thread.sleep(500); // 연결 해제 대기

        testServer.createTestStage();
        CompletableFuture<Void> reconnectFuture = connector.connectAsync(host, tcpPort);
        reconnectFuture.get(5, TimeUnit.SECONDS);

        // Then: 재연결이 성공해야 함
        assertThat(connector.isConnected()).isTrue();
        assertThat(connector.getStageId()).isZero();
    }
}
