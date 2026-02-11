package com.playhouse.connector.support;

import com.playhouse.connector.Connector;
import com.playhouse.connector.ConnectorConfig;
import com.playhouse.connector.Packet;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;

import java.io.IOException;
import java.time.Duration;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.TimeoutException;
import java.util.function.BooleanSupplier;

/**
 * 통합 테스트 베이스 클래스
 * <p>
 * 각 테스트마다 Connector를 초기화하고 정리합니다.
 * 테스트 클래스는 이 클래스를 상속받아 TestServerClient와 Connector를 사용할 수 있습니다.
 * </p>
 */
public abstract class BaseIntegrationTest {

    protected TestServerClient testServer;
    protected Connector connector;
    protected CreateStageResponse stageInfo;

    // 테스트 서버 설정
    protected String host;
    protected int tcpPort;
    protected int tcpTlsPort;
    protected int httpPort;
    protected int httpsPort;

    /**
     * 각 테스트 실행 전 초기화
     */
    @BeforeEach
    public void setUp() throws Exception {
        // 환경 변수에서 테스트 서버 설정 읽기
        host = System.getenv().getOrDefault("TEST_SERVER_HOST", "127.0.0.1");
        httpPort = Integer.parseInt(System.getenv().getOrDefault("TEST_SERVER_HTTP_PORT", "28080"));
        httpsPort = Integer.parseInt(System.getenv().getOrDefault("TEST_SERVER_HTTPS_PORT", "28443"));
        tcpPort = Integer.parseInt(System.getenv().getOrDefault("TEST_SERVER_TCP_PORT", "28001"));
        tcpTlsPort = Integer.parseInt(System.getenv().getOrDefault("TEST_SERVER_TCP_TLS_PORT", "28002"));

        // 테스트 서버 클라이언트 초기화
        testServer = new TestServerClient(host, httpPort);

        // 기본 Connector 설정
        connector = new Connector();
        connector.init(ConnectorConfig.builder()
                .requestTimeoutMs(5000)
                .heartbeatIntervalMs(10000)
                .build());
    }

    /**
     * 각 테스트 실행 후 정리
     */
    @AfterEach
    public void tearDown() throws Exception {
        if (connector != null) {
            if (connector.isConnected()) {
                connector.disconnect();
                // 연결 해제가 완료될 때까지 대기
                Thread.sleep(100);
            }
            connector.close();
            connector = null;
        }

        if (testServer != null) {
            testServer.close();
            testServer = null;
        }

        stageInfo = null;
    }

    /**
     * 테스트용 Stage 생성 및 연결 헬퍼
     *
     * @return 연결 성공 여부
     * @throws IOException 연결 실패 시
     */
    protected boolean createStageAndConnect() throws Exception {
        return createStageAndConnect("TestStage");
    }

    /**
     * 테스트용 Stage 생성 및 연결 헬퍼
     *
     * @param stageType Stage 타입
     * @return 연결 성공 여부
     * @throws IOException 연결 실패 시
     */
    protected boolean createStageAndConnect(String stageType) throws Exception {
        stageInfo = testServer.createStage(stageType);


        CompletableFuture<Void> connectFuture = connector.connectAsync(host, tcpPort);
        connectFuture.get(5, TimeUnit.SECONDS);

        return connector.isConnected();
    }

    /**
     * 인증 헬퍼 메서드
     *
     * @param userId 사용자 ID
     * @return 인증 응답
     * @throws Exception 인증 실패 시
     */
    protected TestMessages.AuthenticateReply authenticate(String userId) throws Exception {
        return authenticate(userId, "valid_token");
    }

    /**
     * 인증 헬퍼 메서드
     *
     * @param userId 사용자 ID
     * @param token  인증 토큰
     * @return 인증 응답
     * @throws Exception 인증 실패 시
     */
    protected TestMessages.AuthenticateReply authenticate(String userId, String token) throws Exception {
        TestMessages.AuthenticateRequest authRequest = new TestMessages.AuthenticateRequest(userId, token);

        Packet requestPacket = Packet.builder("AuthenticateRequest")
                .payload(authRequest.toByteArray())
                .build();

        Packet responsePacket = connector.requestAsync(requestPacket)
                .get(5, TimeUnit.SECONDS);

        TestMessages.AuthenticateReply reply = TestMessages.AuthenticateReply.parseFrom(responsePacket.getPayload());

        // 인증 성공 시 상태 설정
        if (reply.success) {
            connector.setAuthenticated(true);
        }

        return reply;
    }

    /**
     * Echo 요청 헬퍼 메서드
     *
     * @param content  에코할 내용
     * @param sequence 시퀀스 번호
     * @return 에코 응답
     * @throws Exception 요청 실패 시
     */
    protected TestMessages.EchoReply echo(String content, int sequence) throws Exception {
        TestMessages.EchoRequest echoRequest = new TestMessages.EchoRequest(content, sequence);

        Packet requestPacket = Packet.builder("EchoRequest")
                .payload(echoRequest.toByteArray())
                .build();

        Packet responsePacket = connector.requestAsync(requestPacket)
                .get(5, TimeUnit.SECONDS);

        return TestMessages.EchoReply.parseFrom(responsePacket.getPayload());
    }

    /**
     * MainThreadAction을 호출하면서 특정 조건이 충족될 때까지 대기
     *
     * @param condition 조건
     * @param timeoutMs 타임아웃 (밀리초)
     * @return 조건이 충족되면 true, 타임아웃 시 false
     */
    protected boolean waitForCondition(BooleanSupplier condition, int timeoutMs) {
        long deadline = System.currentTimeMillis() + timeoutMs;
        while (!condition.getAsBoolean() && System.currentTimeMillis() < deadline) {
            connector.mainThreadAction();
            try {
                Thread.sleep(10);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
                return false;
            }
        }
        return condition.getAsBoolean();
    }

    /**
     * CompletableFuture를 MainThreadAction과 함께 대기
     *
     * @param future    대기할 Future
     * @param timeoutMs 타임아웃 (밀리초)
     * @param <T>       반환 타입
     * @return Future 결과
     * @throws TimeoutException 타임아웃 시
     */
    protected <T> T waitWithMainThreadAction(CompletableFuture<T> future, int timeoutMs) throws Exception {
        long deadline = System.currentTimeMillis() + timeoutMs;
        while (!future.isDone() && System.currentTimeMillis() < deadline) {
            connector.mainThreadAction();
            Thread.sleep(10);
        }

        if (future.isDone()) {
            return future.get();
        }

        throw new TimeoutException("Operation timed out after " + timeoutMs + "ms");
    }
}
