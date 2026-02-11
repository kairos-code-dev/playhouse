package com.playhouse.connector.advanced;

import com.playhouse.connector.Connector;
import com.playhouse.connector.ConnectorConfig;
import com.playhouse.connector.ConnectorException;
import com.playhouse.connector.Packet;
import com.playhouse.connector.support.BaseIntegrationTest;
import com.playhouse.connector.support.TestMessages.*;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Tag;
import org.junit.jupiter.api.Test;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicInteger;

import static org.assertj.core.api.Assertions.*;

/**
 * A-07: Virtual Thread 동기 API 테스트
 * <p>
 * Java 21+의 Virtual Thread를 사용한 동기 API가 정상 동작하는지 검증합니다.
 * Virtual Thread는 경량 스레드로, 수백만 개의 스레드를 생성해도 OS 리소스를 적게 사용합니다.
 * 동기 API는 Virtual Thread에서 실행되어야 효율적입니다.
 * </p>
 */
@DisplayName("A-07: Virtual Thread 동기 API 테스트")
@Tag("Advanced")
@Tag("VirtualThread")
class A07_VirtualThreadTests extends BaseIntegrationTest {

    @Test
    @DisplayName("A-07-01: Virtual Thread에서 동기 connect()가 정상 동작한다")
    void connectInVirtualThread_succeeds() throws Exception {
        // Given: Stage 생성
        stageInfo = testServer.createStage("TestStage");

        // When: Virtual Thread에서 동기 연결
        Thread.ofVirtual().start(() -> {
            connector.connect(host, tcpPort);
        }).join();

        // Then: 연결 성공
        assertThat(connector.isConnected()).isTrue();
    }

    @Test
    @DisplayName("A-07-02: Virtual Thread에서 동기 request()가 정상 동작한다")
    void requestInVirtualThread_succeeds() throws Exception {
        // Given: 연결 및 인증
        createStageAndConnect();
        authenticate("virtualUser");

        // When: Virtual Thread에서 동기 요청
        CompletableFuture<EchoReply> futureReply = new CompletableFuture<>();
        Thread.ofVirtual().start(() -> {
            try {
                EchoRequest echoRequest = new EchoRequest("Hello from Virtual Thread", 1);
                Packet requestPacket = Packet.builder("EchoRequest")
                        .payload(echoRequest.toByteArray())
                        .build();

                Packet responsePacket = connector.request(requestPacket);
                EchoReply reply = EchoReply.parseFrom(responsePacket.getPayload());
                futureReply.complete(reply);
            } catch (Exception e) {
                futureReply.completeExceptionally(e);
            }
        }).join();

        // Then: 응답 수신 성공
        EchoReply reply = futureReply.get(1, TimeUnit.SECONDS);
        assertThat(reply.content).isEqualTo("Hello from Virtual Thread");
        assertThat(reply.sequence).isEqualTo(1);
    }

    @Test
    @DisplayName("A-07-03: Virtual Thread에서 동기 authenticate()가 정상 동작한다")
    void authenticateInVirtualThread_succeeds() throws Exception {
        // Given: 연결
        createStageAndConnect();

        // When: Virtual Thread에서 동기 인증
        CompletableFuture<Boolean> authResult = new CompletableFuture<>();
        Thread.ofVirtual().start(() -> {
            try {
                AuthenticateRequest authRequest = new AuthenticateRequest("virtualAuthUser", "valid_token");
                Packet authPacket = Packet.builder("AuthenticateRequest")
                        .payload(authRequest.toByteArray())
                        .build();

                boolean authenticated = connector.authenticate(authPacket);
                authResult.complete(authenticated);
            } catch (Exception e) {
                authResult.completeExceptionally(e);
            }
        }).join();

        // Then: 인증 성공
        assertThat(authResult.get(1, TimeUnit.SECONDS)).isTrue();
        assertThat(connector.isAuthenticated()).isTrue();
    }

    @Test
    @DisplayName("A-07-04: 여러 Virtual Thread에서 동시 요청이 정상 동작한다")
    void concurrentRequestsFromMultipleVirtualThreads_succeed() throws Exception {
        // Given: 연결 및 인증
        createStageAndConnect();
        authenticate("concurrentUser");

        final int threadCount = 10;
        List<Thread> threads = new ArrayList<>();
        List<CompletableFuture<EchoReply>> futures = Collections.synchronizedList(new ArrayList<>());

        // When: 10개의 Virtual Thread에서 동시 요청
        for (int i = 0; i < threadCount; i++) {
            final int sequence = i;
            CompletableFuture<EchoReply> future = new CompletableFuture<>();
            futures.add(future);

            Thread thread = Thread.ofVirtual().start(() -> {
                try {
                    EchoRequest echoRequest = new EchoRequest("Message " + sequence, sequence);
                    Packet requestPacket = Packet.builder("EchoRequest")
                            .payload(echoRequest.toByteArray())
                            .build();

                    Packet responsePacket = connector.request(requestPacket);
                    EchoReply reply = EchoReply.parseFrom(responsePacket.getPayload());
                    future.complete(reply);
                } catch (Exception e) {
                    future.completeExceptionally(e);
                }
            });
            threads.add(thread);
        }

        // 모든 스레드 완료 대기
        for (Thread thread : threads) {
            thread.join();
        }

        // Then: 모든 응답이 정상적으로 수신되어야 함
        assertThat(futures).hasSize(threadCount);
        for (int i = 0; i < threadCount; i++) {
            EchoReply reply = futures.get(i).get(1, TimeUnit.SECONDS);
            assertThat(reply).isNotNull();
            assertThat(reply.sequence).isEqualTo(i);
        }
    }

    @Test
    @DisplayName("A-07-05: Virtual Thread 풀에서 다수의 동기 요청을 효율적으로 처리한다")
    void largeNumberOfVirtualThreads_handleRequestsEfficiently() throws Exception {
        // Given: 연결 및 인증
        createStageAndConnect();
        authenticate("poolUser");

        final int requestCount = 100;
        ExecutorService executor = Executors.newVirtualThreadPerTaskExecutor();

        try {
            // When: 100개의 동시 요청을 Virtual Thread 풀에서 실행
            List<Future<EchoReply>> futures = new ArrayList<>();

            for (int i = 0; i < requestCount; i++) {
                final int sequence = i;
                Future<EchoReply> future = executor.submit(() -> {
                    EchoRequest echoRequest = new EchoRequest("Pool Request " + sequence, sequence);
                    Packet requestPacket = Packet.builder("EchoRequest")
                            .payload(echoRequest.toByteArray())
                            .build();

                    Packet responsePacket = connector.request(requestPacket);
                    return EchoReply.parseFrom(responsePacket.getPayload());
                });
                futures.add(future);
            }

            // Then: 모든 요청이 성공적으로 처리되어야 함
            AtomicInteger successCount = new AtomicInteger(0);
            for (int i = 0; i < requestCount; i++) {
                EchoReply reply = futures.get(i).get(10, TimeUnit.SECONDS);
                assertThat(reply).isNotNull();
                successCount.incrementAndGet();
            }

            assertThat(successCount.get()).isEqualTo(requestCount);
        } finally {
            executor.shutdown();
            assertThat(executor.awaitTermination(5, TimeUnit.SECONDS)).isTrue();
        }
    }

    @Test
    @DisplayName("A-07-06: 동기 API와 비동기 API를 혼용해도 정상 동작한다")
    void mixingSyncAndAsyncAPIs_worksCorrectly() throws Exception {
        // Given: 연결 및 인증
        createStageAndConnect();
        authenticate("mixedUser");

        // When: 비동기 API로 요청 후, Virtual Thread에서 동기 API로 요청
        EchoRequest asyncRequest = new EchoRequest("Async Request", 1);
        Packet asyncPacket = Packet.builder("EchoRequest")
                .payload(asyncRequest.toByteArray())
                .build();

        CompletableFuture<Packet> asyncFuture = connector.requestAsync(asyncPacket);

        CompletableFuture<EchoReply> syncFuture = new CompletableFuture<>();
        Thread.ofVirtual().start(() -> {
            try {
                EchoRequest syncRequest = new EchoRequest("Sync Request", 2);
                Packet syncPacket = Packet.builder("EchoRequest")
                        .payload(syncRequest.toByteArray())
                        .build();

                Packet responsePacket = connector.request(syncPacket);
                EchoReply reply = EchoReply.parseFrom(responsePacket.getPayload());
                syncFuture.complete(reply);
            } catch (Exception e) {
                syncFuture.completeExceptionally(e);
            }
        });

        // Then: 두 요청 모두 성공
        Packet asyncResponse = asyncFuture.get(5, TimeUnit.SECONDS);
        EchoReply asyncReply = EchoReply.parseFrom(asyncResponse.getPayload());
        assertThat(asyncReply.content).isEqualTo("Async Request");
        assertThat(asyncReply.sequence).isEqualTo(1);

        EchoReply syncReply = syncFuture.get(5, TimeUnit.SECONDS);
        assertThat(syncReply.content).isEqualTo("Sync Request");
        assertThat(syncReply.sequence).isEqualTo(2);
    }

    @Test
    @DisplayName("A-07-07: Virtual Thread에서 순차적인 여러 요청이 정상 동작한다")
    void sequentialRequestsInVirtualThread_succeed() throws Exception {
        // Given: 연결 및 인증
        createStageAndConnect();
        authenticate("sequentialVirtualUser");

        // When: Virtual Thread에서 순차적으로 여러 요청 실행
        List<EchoReply> replies = Collections.synchronizedList(new ArrayList<>());
        Thread.ofVirtual().start(() -> {
            try {
                for (int i = 0; i < 5; i++) {
                    EchoRequest echoRequest = new EchoRequest("Sequential " + i, i);
                    Packet requestPacket = Packet.builder("EchoRequest")
                            .payload(echoRequest.toByteArray())
                            .build();

                    Packet responsePacket = connector.request(requestPacket);
                    EchoReply reply = EchoReply.parseFrom(responsePacket.getPayload());
                    replies.add(reply);
                }
            } catch (Exception e) {
                throw new RuntimeException(e);
            }
        }).join();

        // Then: 5개의 응답이 순서대로 수신되어야 함
        assertThat(replies).hasSize(5);
        for (int i = 0; i < 5; i++) {
            assertThat(replies.get(i).content).isEqualTo("Sequential " + i);
            assertThat(replies.get(i).sequence).isEqualTo(i);
        }
    }

    @Test
    @DisplayName("A-07-08: Virtual Thread에서 동기 API 예외가 올바르게 처리된다")
    void syncAPIExceptionsInVirtualThread_areHandledCorrectly() throws Exception {
        // Given: 연결 및 인증 후 Disconnect
        createStageAndConnect();
        authenticate("exceptionUser");
        connector.disconnect();
        Thread.sleep(500);  // Ensure disconnect completes

        // When: Virtual Thread에서 연결 해제 후 요청 시도
        CompletableFuture<Throwable> exceptionFuture = new CompletableFuture<>();
        Thread.ofVirtual().start(() -> {
            try {
                EchoRequest echoRequest = new EchoRequest("Test", 1);
                Packet requestPacket = Packet.builder("EchoRequest")
                        .payload(echoRequest.toByteArray())
                        .build();

                // This should throw because we're disconnected
                connector.request(requestPacket);
                exceptionFuture.complete(null);
            } catch (Exception e) {
                exceptionFuture.complete(e);
            }
        }).join();

        // Then: 예외가 발생해야 함 (연결 해제 상태에서 요청 시)
        Throwable exception = exceptionFuture.get(1, TimeUnit.SECONDS);
        assertThat(exception).isNotNull();
        assertThat(exception).isInstanceOf(ConnectorException.class);
    }

    @Test
    @DisplayName("A-07-09: 여러 Connector를 각각의 Virtual Thread에서 사용할 수 있다")
    void multipleConnectorsInSeparateVirtualThreads_workIndependently() throws Exception {
        // Given: 단일 Stage 사용 (테스트 서버 부하 감소)
        stageInfo = testServer.createStage("TestStage");

        Connector connector1 = new Connector();
        Connector connector2 = new Connector();
        connector1.init(ConnectorConfig.builder()
                .requestTimeoutMs(10000)  // 여유있는 타임아웃
                .heartbeatIntervalMs(30000)
                .build());
        connector2.init(ConnectorConfig.builder()
                .requestTimeoutMs(10000)  // 여유있는 타임아웃
                .heartbeatIntervalMs(30000)
                .build());

        try {
            // When: 각 Connector를 Virtual Thread에서 순차적으로 연결 및 요청
            // 동시 연결 시 테스트 서버 부하를 피하기 위해 순차 실행
            CompletableFuture<EchoReply> future1 = new CompletableFuture<>();
            CompletableFuture<EchoReply> future2 = new CompletableFuture<>();

            // 첫 번째 Connector 연결
            Thread thread1 = Thread.ofVirtual().start(() -> {
                try {
                    connector1.connect(host, tcpPort);

                    AuthenticateRequest auth = new AuthenticateRequest("user1", "valid_token");
                    Packet authPacket = Packet.builder("AuthenticateRequest")
                            .payload(auth.toByteArray())
                            .build();
                    connector1.authenticate(authPacket);

                    EchoRequest echo = new EchoRequest("From Connector 1", 1);
                    Packet echoPacket = Packet.builder("EchoRequest")
                            .payload(echo.toByteArray())
                            .build();
                    Packet response = connector1.request(echoPacket);
                    future1.complete(EchoReply.parseFrom(response.getPayload()));
                } catch (Exception e) {
                    future1.completeExceptionally(e);
                }
            });

            thread1.join();  // 첫 번째 완료 대기

            // 두 번째 Connector 연결 (동일 Stage, 다른 사용자)
            Thread thread2 = Thread.ofVirtual().start(() -> {
                try {
                    connector2.connect(host, tcpPort);

                    AuthenticateRequest auth = new AuthenticateRequest("user2", "valid_token");
                    Packet authPacket = Packet.builder("AuthenticateRequest")
                            .payload(auth.toByteArray())
                            .build();
                    connector2.authenticate(authPacket);

                    EchoRequest echo = new EchoRequest("From Connector 2", 2);
                    Packet echoPacket = Packet.builder("EchoRequest")
                            .payload(echo.toByteArray())
                            .build();
                    Packet response = connector2.request(echoPacket);
                    future2.complete(EchoReply.parseFrom(response.getPayload()));
                } catch (Exception e) {
                    future2.completeExceptionally(e);
                }
            });

            thread2.join();  // 두 번째 완료 대기

            // Then: 두 Connector 모두 독립적으로 동작
            EchoReply reply1 = future1.get(5, TimeUnit.SECONDS);
            EchoReply reply2 = future2.get(5, TimeUnit.SECONDS);

            assertThat(reply1.content).isEqualTo("From Connector 1");
            assertThat(reply2.content).isEqualTo("From Connector 2");
        } finally {
            if (connector1.isConnected()) {
                connector1.disconnect();
            }
            connector1.close();

            if (connector2.isConnected()) {
                connector2.disconnect();
            }
            connector2.close();
        }
    }

    @Test
    @DisplayName("A-07-10: Virtual Thread에서 Packet 기반 인증 API가 정상 동작한다")
    void packetBasedAuthenticationAPIInVirtualThread_succeeds() throws Exception {
        // Given: 연결
        createStageAndConnect();

        // When: Virtual Thread에서 Packet 기반 인증 API 사용
        // Note: 간편 API는 "AuthRequest"를 사용하지만, 테스트 서버는 "AuthenticateRequest"만 지원
        CompletableFuture<Boolean> authResult = new CompletableFuture<>();
        Thread.ofVirtual().start(() -> {
            try {
                AuthenticateRequest authRequest = new AuthenticateRequest("virtualAuthUser", "valid_token");
                Packet authPacket = Packet.builder("AuthenticateRequest")
                        .payload(authRequest.toByteArray())
                        .build();

                boolean authenticated = connector.authenticate(authPacket);
                authResult.complete(authenticated);
            } catch (Exception e) {
                authResult.completeExceptionally(e);
            }
        }).join();

        // Then: 인증 성공
        assertThat(authResult.get(1, TimeUnit.SECONDS)).isTrue();
        assertThat(connector.isAuthenticated()).isTrue();
    }
}
