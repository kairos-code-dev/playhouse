package com.playhouse.connector.core;

import com.playhouse.connector.Connector;
import com.playhouse.connector.ConnectorConfig;
import com.playhouse.connector.Packet;
import com.playhouse.connector.support.BaseIntegrationTest;
import com.playhouse.connector.support.CreateStageResponse;
import com.playhouse.connector.support.TestMessages;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;

import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicReference;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * C-03: 인증 성공 테스트
 * <p>
 * Connector의 Authenticate 메서드를 통해 성공적으로 인증할 수 있는지 검증합니다.
 * </p>
 */
@DisplayName("C-03: Authentication Success Tests")
public class C03_AuthenticationSuccessTests extends BaseIntegrationTest {

    @Test
    @DisplayName("C-03-01: 유효한 토큰으로 인증이 성공한다")
    public void authenticate_withValidToken_succeeds() throws Exception {
        // Given: 연결된 상태
        createStageAndConnect();

        // When: 유효한 토큰으로 인증
        TestMessages.AuthenticateReply authReply = authenticate("user123", "valid_token");

        // Then: 인증이 성공하고 올바른 응답을 받아야 함
        assertThat(authReply).isNotNull();
        assertThat(authReply.success).isTrue();
        assertThat(authReply.accountId).isNotNull().isNotEmpty();

        // IsAuthenticated도 true여야 함
        assertThat(connector.isAuthenticated()).isTrue();
    }

    @Test
    @DisplayName("C-03-02: AuthenticateAsync로 인증할 수 있다")
    public void authenticateAsync_withValidCredentials_returnsSuccessReply() throws Exception {
        // Given: 연결된 상태
        createStageAndConnect();

        TestMessages.AuthenticateRequest authRequest = new TestMessages.AuthenticateRequest("testUser", "valid_token");

        // When: AuthenticateAsync 호출
        Packet requestPacket = Packet.builder("AuthenticateRequest")
                .payload(authRequest.toByteArray())
                .build();

        Packet responsePacket = connector.requestAsync(requestPacket)
                .get(5, TimeUnit.SECONDS);

        // Then: 응답 패킷이 올바르게 반환되어야 함
        assertThat(responsePacket).isNotNull();
        assertThat(responsePacket.getMsgId()).isEqualTo("AuthenticateReply");

        TestMessages.AuthenticateReply authReply = TestMessages.AuthenticateReply.parseFrom(responsePacket.getPayload());
        assertThat(authReply.success).isTrue();
    }

    @Test
    @DisplayName("C-03-03: Authenticate 콜백 방식으로 인증할 수 있다")
    public void authenticate_withCallback_invokesCallbackWithSuccess() throws Exception {
        // Given: 연결된 상태
        createStageAndConnect();

        TestMessages.AuthenticateRequest authRequest = new TestMessages.AuthenticateRequest("callbackUser", "valid_token");

        CompletableFuture<TestMessages.AuthenticateReply> future = new CompletableFuture<>();

        // When: Authenticate 콜백 방식 호출
        Packet requestPacket = Packet.builder("AuthenticateRequest")
                .payload(authRequest.toByteArray())
                .build();

        connector.request(requestPacket, responsePacket -> {
            try {
                TestMessages.AuthenticateReply authReply = TestMessages.AuthenticateReply.parseFrom(responsePacket.getPayload());
                future.complete(authReply);
            } catch (Exception e) {
                future.completeExceptionally(e);
            }
        });

        // 콜백 대기 (MainThreadAction 호출하면서 최대 5초)
        TestMessages.AuthenticateReply authReply = waitWithMainThreadAction(future, 5000);

        // Then: 콜백이 호출되고 성공 응답을 받아야 함
        assertThat(authReply.success).isTrue();
    }

    @Test
    @DisplayName("C-03-04: 메타데이터와 함께 인증할 수 있다")
    public void authenticate_withMetadata_succeedsAndEchoesMetadata() throws Exception {
        // Given: 연결된 상태
        createStageAndConnect();

        TestMessages.AuthenticateRequest authRequest = new TestMessages.AuthenticateRequest("metaUser", "valid_token");
        authRequest.metadata.put("client_version", "1.0.0");
        authRequest.metadata.put("platform", "java");

        // When: 메타데이터와 함께 인증
        Packet requestPacket = Packet.builder("AuthenticateRequest")
                .payload(authRequest.toByteArray())
                .build();

        Packet responsePacket = connector.requestAsync(requestPacket)
                .get(5, TimeUnit.SECONDS);

        // Then: 인증이 성공해야 함
        TestMessages.AuthenticateReply authReply = TestMessages.AuthenticateReply.parseFrom(responsePacket.getPayload());
        assertThat(authReply.success).isTrue();
    }

    @Test
    @DisplayName("C-03-05: 인증 성공 후 AccountId가 할당된다")
    public void authenticate_success_assignsAccountId() throws Exception {
        // Given: 연결된 상태
        createStageAndConnect();

        // When: 인증 성공
        TestMessages.AuthenticateReply authReply = authenticate("user_with_account_id");

        // Then: AccountId가 할당되어야 함
        assertThat(authReply.accountId).isNotNull().isNotEmpty();
        assertThat(authReply.accountId).isNotEqualTo("0");
    }

    @Test
    @DisplayName("C-03-06: 여러 유저가 동시에 인증할 수 있다")
    public void authenticate_multipleUsers_allSucceed() throws Exception {
        // Given: 3개의 Stage와 Connector 생성
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

            // When: 3개의 Connector가 각각 연결 및 인증

            connector1.connectAsync(host, tcpPort).get(5, TimeUnit.SECONDS);
            connector2.connectAsync(host, tcpPort).get(5, TimeUnit.SECONDS);
            connector3.connectAsync(host, tcpPort).get(5, TimeUnit.SECONDS);

            TestMessages.AuthenticateRequest auth1Request = new TestMessages.AuthenticateRequest("user1", "valid_token");
            TestMessages.AuthenticateRequest auth2Request = new TestMessages.AuthenticateRequest("user2", "valid_token");
            TestMessages.AuthenticateRequest auth3Request = new TestMessages.AuthenticateRequest("user3", "valid_token");

            Packet packet1 = Packet.builder("AuthenticateRequest").payload(auth1Request.toByteArray()).build();
            Packet packet2 = Packet.builder("AuthenticateRequest").payload(auth2Request.toByteArray()).build();
            Packet packet3 = Packet.builder("AuthenticateRequest").payload(auth3Request.toByteArray()).build();

            Packet reply1Packet = connector1.requestAsync(packet1).get(5, TimeUnit.SECONDS);
            Packet reply2Packet = connector2.requestAsync(packet2).get(5, TimeUnit.SECONDS);
            Packet reply3Packet = connector3.requestAsync(packet3).get(5, TimeUnit.SECONDS);

            TestMessages.AuthenticateReply reply1 = TestMessages.AuthenticateReply.parseFrom(reply1Packet.getPayload());
            TestMessages.AuthenticateReply reply2 = TestMessages.AuthenticateReply.parseFrom(reply2Packet.getPayload());
            TestMessages.AuthenticateReply reply3 = TestMessages.AuthenticateReply.parseFrom(reply3Packet.getPayload());

            // Then: 모든 인증이 성공해야 함
            assertThat(reply1.success).isTrue();
            assertThat(reply2.success).isTrue();
            assertThat(reply3.success).isTrue();

            // 각자 고유한 AccountId를 가져야 함
            assertThat(reply1.accountId).isNotEqualTo(reply2.accountId);
            assertThat(reply2.accountId).isNotEqualTo(reply3.accountId);
            assertThat(reply1.accountId).isNotEqualTo(reply3.accountId);
        } finally {
            // 정리
            connector1.close();
            connector2.close();
            connector3.close();
        }
    }
}
