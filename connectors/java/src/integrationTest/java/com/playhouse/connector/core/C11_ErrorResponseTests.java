package com.playhouse.connector.core;

import com.playhouse.connector.Connector;
import com.playhouse.connector.ConnectorConfig;
import com.playhouse.connector.Packet;
import com.playhouse.connector.support.BaseIntegrationTest;
import com.playhouse.connector.support.CreateStageResponse;
import com.playhouse.connector.support.TestMessages;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;

import java.util.concurrent.CompletableFuture;
import java.util.concurrent.TimeUnit;

import static org.assertj.core.api.Assertions.assertThat;

/**
 * C-11: 에러 응답 테스트
 * <p>
 * 서버가 에러 응답을 보내는 경우를 검증합니다.
 * FailRequest를 보내면 서버가 의도적으로 에러 응답을 반환합니다.
 * </p>
 */
@DisplayName("C-11: Error Response Tests")
public class C11_ErrorResponseTests extends BaseIntegrationTest {

    @Test
    @DisplayName("C-11-01: 서버 에러 응답을 받을 수 있다")
    public void requestAsync_withFailRequest_receivesErrorResponse() throws Exception {
        // Given: 연결 및 인증 완료
        createStageAndConnect();
        authenticate("errorUser");

        TestMessages.FailRequest failRequest = new TestMessages.FailRequest(1000, "Test Error");
        Packet requestPacket = Packet.builder("FailRequest")
                .payload(failRequest.toByteArray())
                .build();

        // When: 에러를 발생시키는 요청 전송
        Packet responsePacket = connector.requestAsync(requestPacket).get(5, TimeUnit.SECONDS);

        // Then: 에러 응답을 받아야 함
        assertThat(responsePacket).isNotNull();
        assertThat(responsePacket.getMsgId())
                .as("에러 응답 메시지 타입이어야 함")
                .isEqualTo("FailReply");

        TestMessages.FailReply failReply = TestMessages.FailReply.parseFrom(responsePacket.getPayload());
        assertThat(failReply.errorCode)
                .as("요청한 에러 코드가 반환되어야 함")
                .isEqualTo(1000);
        assertThat(failReply.message)
                .as("에러 메시지가 포함되어야 함")
                .contains("Test Error");
    }

    @Test
    @DisplayName("C-11-02: 다양한 에러 코드를 처리할 수 있다")
    public void errorResponse_withDifferentErrorCodes_handlesCorrectly() throws Exception {
        // Given: 연결 및 인증 완료
        createStageAndConnect();
        authenticate("multiErrorUser");

        int[] errorCodes = {1000, 1001, 1002, 1003, 1004};

        // When: 다양한 에러 코드로 요청
        for (int errorCode : errorCodes) {
            TestMessages.FailRequest failRequest = new TestMessages.FailRequest(errorCode, "Error " + errorCode);
            Packet requestPacket = Packet.builder("FailRequest")
                    .payload(failRequest.toByteArray())
                    .build();

            Packet responsePacket = connector.requestAsync(requestPacket).get(5, TimeUnit.SECONDS);

            // Then: 각 에러 코드가 올바르게 반환되어야 함
            TestMessages.FailReply failReply = TestMessages.FailReply.parseFrom(responsePacket.getPayload());
            assertThat(failReply.errorCode)
                    .as("에러 코드 " + errorCode + "가 반환되어야 함")
                    .isEqualTo(errorCode);
            assertThat(failReply.message)
                    .as("에러 메시지가 일치해야 함")
                    .contains("Error " + errorCode);
        }
    }

    @Test
    @DisplayName("C-11-03: 에러 응답 후에도 연결은 유지된다")
    public void connection_afterErrorResponse_remainsConnected() throws Exception {
        // Given: 연결 및 인증 완료
        createStageAndConnect();
        authenticate("connectionErrorUser");

        TestMessages.FailRequest failRequest = new TestMessages.FailRequest(1000, "Connection Test Error");
        Packet requestPacket = Packet.builder("FailRequest")
                .payload(failRequest.toByteArray())
                .build();

        // When: 에러 응답 받기
        connector.requestAsync(requestPacket).get(5, TimeUnit.SECONDS);

        // Then: 연결과 인증 상태가 유지되어야 함
        assertThat(connector.isConnected())
                .as("에러 응답 후에도 연결이 유지되어야 함")
                .isTrue();
        assertThat(connector.isAuthenticated())
                .as("인증 상태도 유지되어야 함")
                .isTrue();

        // 다른 요청도 정상 동작해야 함
        TestMessages.EchoReply echoReply = echo("After Error", 1);
        assertThat(echoReply.content)
                .as("에러 후 정상 요청이 동작해야 함")
                .isEqualTo("After Error");
    }

    @Test
    @DisplayName("C-11-04: 콜백 방식도 에러 응답을 처리할 수 있다")
    public void request_withCallback_handlesErrorResponse() throws Exception {
        // Given: 연결 및 인증 완료
        createStageAndConnect();
        authenticate("callbackErrorUser");

        TestMessages.FailRequest failRequest = new TestMessages.FailRequest(2000, "Callback Error Test");
        Packet requestPacket = Packet.builder("FailRequest")
                .payload(failRequest.toByteArray())
                .build();

        CompletableFuture<TestMessages.FailReply> future = new CompletableFuture<>();

        // When: 콜백 방식으로 에러 요청
        connector.request(requestPacket, responsePacket -> {
            try {
                TestMessages.FailReply failReply = TestMessages.FailReply.parseFrom(responsePacket.getPayload());
                future.complete(failReply);
            } catch (Exception e) {
                future.completeExceptionally(e);
            }
        });

        // 콜백 대기 (MainThreadAction 호출하면서 최대 5초)
        TestMessages.FailReply failReply = waitWithMainThreadAction(future, 5000);

        // Then: 콜백이 호출되고 에러 정보를 받아야 함
        assertThat(failReply.errorCode).isEqualTo(2000);
        assertThat(failReply.message).contains("Callback Error Test");
    }

    @Test
    @DisplayName("C-11-05: 에러 응답과 정상 응답을 섞어서 처리할 수 있다")
    public void mixedRequests_errorAndSuccess_bothHandled() throws Exception {
        // Given: 연결 및 인증 완료
        createStageAndConnect();
        authenticate("mixedUser");

        // When: 정상 요청과 에러 요청을 번갈아 보냄
        TestMessages.EchoReply echo1 = echo("Success 1", 1);
        assertThat(echo1.content).isEqualTo("Success 1");

        TestMessages.FailRequest failRequest1 = new TestMessages.FailRequest(3001, "Error 1");
        Packet failPacket1 = Packet.builder("FailRequest")
                .payload(failRequest1.toByteArray())
                .build();
        Packet failResponse1 = connector.requestAsync(failPacket1).get(5, TimeUnit.SECONDS);
        TestMessages.FailReply failReply1 = TestMessages.FailReply.parseFrom(failResponse1.getPayload());
        assertThat(failReply1.errorCode).isEqualTo(3001);

        TestMessages.EchoReply echo2 = echo("Success 2", 2);
        assertThat(echo2.content).isEqualTo("Success 2");

        TestMessages.FailRequest failRequest2 = new TestMessages.FailRequest(3002, "Error 2");
        Packet failPacket2 = Packet.builder("FailRequest")
                .payload(failRequest2.toByteArray())
                .build();
        Packet failResponse2 = connector.requestAsync(failPacket2).get(5, TimeUnit.SECONDS);
        TestMessages.FailReply failReply2 = TestMessages.FailReply.parseFrom(failResponse2.getPayload());
        assertThat(failReply2.errorCode).isEqualTo(3002);

        TestMessages.EchoReply echo3 = echo("Success 3", 3);
        assertThat(echo3.content).isEqualTo("Success 3");

        // Then: 모든 요청이 올바르게 처리되어야 함
        assertThat(connector.isConnected())
                .as("연결이 유지되어야 함")
                .isTrue();
    }

    @Test
    @DisplayName("C-11-06: 빈 에러 메시지도 처리할 수 있다")
    public void errorResponse_withEmptyMessage_handlesCorrectly() throws Exception {
        // Given: 연결 및 인증 완료
        createStageAndConnect();
        authenticate("emptyErrorUser");

        TestMessages.FailRequest failRequest = new TestMessages.FailRequest(4000, ""); // 빈 에러 메시지
        Packet requestPacket = Packet.builder("FailRequest")
                .payload(failRequest.toByteArray())
                .build();

        // When: 빈 에러 메시지로 요청
        Packet responsePacket = connector.requestAsync(requestPacket).get(5, TimeUnit.SECONDS);

        // Then: 에러 응답을 받아야 함
        TestMessages.FailReply failReply = TestMessages.FailReply.parseFrom(responsePacket.getPayload());
        assertThat(failReply.errorCode)
                .as("에러 코드가 반환되어야 함")
                .isEqualTo(4000);
    }

    @Test
    @DisplayName("C-11-07: 여러 클라이언트가 각자 에러를 받을 수 있다")
    public void multipleClients_eachReceivesOwnError() throws Exception {
        // Given: 2개의 Connector 생성 및 연결
        CreateStageResponse stage1 = testServer.createTestStage();
        CreateStageResponse stage2 = testServer.createTestStage();

        Connector connector1 = new Connector();
        Connector connector2 = new Connector();

        try {
            connector1.init(ConnectorConfig.defaultConfig());
            connector2.init(ConnectorConfig.defaultConfig());


            connector1.connectAsync(host, tcpPort).get(5, TimeUnit.SECONDS);
            connector2.connectAsync(host, tcpPort).get(5, TimeUnit.SECONDS);

            TestMessages.AuthenticateRequest auth1 = new TestMessages.AuthenticateRequest("user1", "valid_token");
            TestMessages.AuthenticateRequest auth2 = new TestMessages.AuthenticateRequest("user2", "valid_token");

            Packet authPacket1 = Packet.builder("AuthenticateRequest")
                    .payload(auth1.toByteArray()).build();
            Packet authPacket2 = Packet.builder("AuthenticateRequest")
                    .payload(auth2.toByteArray()).build();

            connector1.requestAsync(authPacket1).get(5, TimeUnit.SECONDS);
            connector2.requestAsync(authPacket2).get(5, TimeUnit.SECONDS);

            // When: 각 클라이언트가 다른 에러 코드로 요청
            TestMessages.FailRequest fail1 = new TestMessages.FailRequest(5001, "Error from Client 1");
            TestMessages.FailRequest fail2 = new TestMessages.FailRequest(5002, "Error from Client 2");

            Packet failPacket1 = Packet.builder("FailRequest")
                    .payload(fail1.toByteArray()).build();
            Packet failPacket2 = Packet.builder("FailRequest")
                    .payload(fail2.toByteArray()).build();

            Packet response1 = connector1.requestAsync(failPacket1).get(5, TimeUnit.SECONDS);
            Packet response2 = connector2.requestAsync(failPacket2).get(5, TimeUnit.SECONDS);

            // Then: 각 클라이언트가 자신의 에러를 받아야 함
            TestMessages.FailReply reply1 = TestMessages.FailReply.parseFrom(response1.getPayload());
            TestMessages.FailReply reply2 = TestMessages.FailReply.parseFrom(response2.getPayload());

            assertThat(reply1.errorCode)
                    .as("클라이언트 1의 에러 코드")
                    .isEqualTo(5001);
            assertThat(reply2.errorCode)
                    .as("클라이언트 2의 에러 코드")
                    .isEqualTo(5002);

            assertThat(reply1.message).contains("Client 1");
            assertThat(reply2.message).contains("Client 2");
        } finally {
            connector1.close();
            connector2.close();
        }
    }

    @Test
    @DisplayName("C-11-08: 에러 응답 타입이 올바르다")
    public void errorResponse_messageType_isCorrect() throws Exception {
        // Given: 연결 및 인증 완료
        createStageAndConnect();
        authenticate("typeCheckUser");

        TestMessages.FailRequest failRequest = new TestMessages.FailRequest(6000, "Type Check");
        Packet requestPacket = Packet.builder("FailRequest")
                .payload(failRequest.toByteArray())
                .build();

        // When: 에러 요청
        Packet responsePacket = connector.requestAsync(requestPacket).get(5, TimeUnit.SECONDS);

        // Then: 응답 메시지 타입이 FailReply여야 함
        assertThat(responsePacket.getMsgId())
                .as("에러 응답 타입이 FailReply여야 함")
                .isEqualTo("FailReply");
    }
}
