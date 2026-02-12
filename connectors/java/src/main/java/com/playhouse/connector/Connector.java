package com.playhouse.connector;

import com.playhouse.connector.internal.ClientNetwork;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionException;
import java.util.function.BiConsumer;
import java.util.function.Consumer;

/**
 * PlayHouse Connector
 * <p>
 * PlayHouse 실시간 게임 서버에 연결하여 메시지 송수신을 처리합니다.
 * Java NIO 기반 비동기 I/O를 사용하며, 동기/비동기/콜백 세 가지 API 스타일을 지원합니다.
 * <p>
 * <h2>API 스타일</h2>
 * <ul>
 *   <li><b>동기 API</b>: Virtual Thread에서 효율적으로 동작 (Java 21+)</li>
 *   <li><b>비동기 API</b>: CompletableFuture 반환 (*Async 접미사)</li>
 *   <li><b>콜백 API</b>: Consumer/Runnable 콜백 파라미터</li>
 * </ul>
 * <p>
 * <h2>동기 API 사용 예제 (권장 - Virtual Thread)</h2>
 * <pre>
 * ConnectorConfig config = ConnectorConfig.builder()
 *     .requestTimeoutMs(10000)
 *     .build();
 *
 * try (Connector connector = new Connector()) {
 *     connector.init(config);
 *     connector.setOnReceive(packet -> System.out.println("Received: " + packet.getMsgId()));
 *
 *     // Virtual Thread에서 실행
 *     Thread.startVirtualThread(() -> {
 *         try {
 *             // 동기 연결
 *             connector.connect("localhost", 34001);
 *
 *             // 동기 인증
 *             Packet authPacket = Packet.fromBytes("AuthRequest", authData);
 *             boolean authenticated = connector.authenticate(authPacket);
 *
 *             // 동기 요청-응답
 *             Packet request = Packet.fromBytes("EchoRequest", "Hello".getBytes());
 *             Packet response = connector.request(request);
 *             System.out.println("Response: " + response.getMsgId());
 *         } catch (ConnectorException e) {
 *             System.err.println("Error: " + e.getMessage());
 *         }
 *     });
 * }
 * </pre>
 * <p>
 * <h2>비동기 API 사용 예제</h2>
 * <pre>
 * try (Connector connector = new Connector()) {
 *     connector.init();
 *     connector.setOnConnect(() -> System.out.println("Connected"));
 *     connector.setOnReceive(packet -> System.out.println("Received: " + packet.getMsgId()));
 *
 *     connector.connectAsync("localhost", 34001)
 *         .thenCompose(v -> {
 *             Packet authPacket = Packet.fromBytes("AuthRequest", authData);
 *             return connector.authenticateAsync(authPacket);
 *         })
 *         .thenCompose(authenticated -> {
 *             Packet request = Packet.fromBytes("EchoRequest", "Hello".getBytes());
 *             return connector.requestAsync(request);
 *         })
 *         .thenAccept(response -> System.out.println("Response: " + response.getMsgId()))
 *         .join();
 * }
 * </pre>
 * <p>
 * <h2>콜백 API 사용 예제 (게임 엔진용)</h2>
 * <pre>
 * try (Connector connector = new Connector()) {
 *     connector.init();
 *     connector.setOnConnect(() -> System.out.println("Connected"));
 *
 *     // 콜백 방식 요청
 *     Packet request = Packet.fromBytes("EchoRequest", "Hello".getBytes());
 *     connector.request(request, response -> {
 *         System.out.println("Response: " + response.getMsgId());
 *     });
 * }
 * </pre>
 */
public final class Connector implements AutoCloseable {

    private static final Logger logger = LoggerFactory.getLogger(Connector.class);

    private ClientNetwork clientNetwork;
    private ConnectorConfig config;
    private volatile long stageId;
    private volatile boolean initialized;

    /**
     * Connector 초기화
     *
     * @param config 설정
     * @throws IllegalStateException 이미 초기화된 경우
     */
    public void init(ConnectorConfig config) {
        if (initialized) {
            throw new IllegalStateException("Connector already initialized");
        }

        if (config == null) {
            throw new IllegalArgumentException("Config cannot be null");
        }

        this.config = config;
        this.clientNetwork = new ClientNetwork(config);
        this.stageId = 0L;
        this.initialized = true;

        logger.info("Connector initialized: {}", config);
    }

    /**
     * 기본 설정으로 초기화
     */
    public void init() {
        init(ConnectorConfig.defaultConfig());
    }

    /**
     * 서버에 비동기 연결
     *
     * @param host 서버 호스트
     * @param port 서버 포트
     * @return 연결 성공 여부 Future
     * @throws IllegalStateException 초기화되지 않은 경우
     */
    public CompletableFuture<Void> connectAsync(String host, int port) {
        checkInitialized();

        logger.info("Connecting to {}:{}...", host, port);

        return clientNetwork.connectAsync(host, port)
            .thenApply(success -> {
                if (!success) {
                    throw new ConnectorException(1001, "Connection failed");
                }
                return null;
            });
    }

    /**
     * 서버에 동기 연결 (Virtual Thread에서 효율적으로 동작)
     *
     * @param host 서버 호스트
     * @param port 서버 포트
     * @throws ConnectorException    연결 실패 시
     * @throws IllegalStateException 초기화되지 않은 경우
     */
    public void connect(String host, int port) {
        try {
            connectAsync(host, port).join();
        } catch (CompletionException e) {
            throw unwrapException(e);
        }
    }

    /**
     * 연결 해제
     */
    public void disconnect() {
        if (!initialized) {
            return;
        }

        logger.info("Disconnecting...");
        clientNetwork.disconnectAsync().join();
    }

    /**
     * 연결 상태 확인
     *
     * @return 연결되어 있으면 true
     */
    public boolean isConnected() {
        return initialized && clientNetwork.isConnected();
    }

    /**
     * 인증 상태 확인
     *
     * @return 인증되어 있으면 true
     */
    public boolean isAuthenticated() {
        return initialized && clientNetwork.isAuthenticated();
    }

    /**
     * 인증 상태 직접 설정 (테스트 또는 특수 상황용)
     *
     * @param authenticated 인증 상태
     */
    public void setAuthenticated(boolean authenticated) {
        if (initialized) {
            clientNetwork.setAuthenticated(authenticated);
            if (!authenticated) {
                this.stageId = 0L;
            }
        }
    }

    /**
     * 인증 요청 (간편 API - 비동기)
     *
     * @param serviceId 서비스 ID
     * @param accountId 계정 ID
     * @param payload   인증 페이로드
     * @return 인증 성공 여부 Future
     */
    public CompletableFuture<Boolean> authenticateAsync(String serviceId, String accountId, byte[] payload) {
        // 실제 구현에서는 AuthRequest 프로토콜 사용
        Packet authPacket = Packet.builder("AuthRequest")
            .payload(payload)
            .build();

        return requestAsync(authPacket)
            .thenApply(response -> {
                boolean success = response.getErrorCode() == 0;
                if (success) {
                    clientNetwork.setAuthenticated(true);
                    logger.info("Authentication successful: serviceId={}, accountId={}", serviceId, accountId);
                } else {
                    logger.warn("Authentication failed: errorCode={}", response.getErrorCode());
                }
                return success;
            });
    }

    /**
     * 인증 요청 (동기 방식 - Virtual Thread에서 효율적으로 동작)
     *
     * @param serviceId 서비스 ID
     * @param accountId 계정 ID
     * @param payload   인증 페이로드
     * @return 인증 성공 여부
     * @throws ConnectorException 인증 실패 시
     */
    public boolean authenticate(String serviceId, String accountId, byte[] payload) {
        try {
            return authenticateAsync(serviceId, accountId, payload).join();
        } catch (CompletionException e) {
            throw unwrapException(e);
        }
    }

    /**
     * Packet을 사용한 인증 요청 (비동기)
     *
     * @param authPacket 인증 패킷
     * @return 인증 성공 여부 Future
     */
    public CompletableFuture<Boolean> authenticateAsync(Packet authPacket) {
        return requestAsync(authPacket)
            .thenApply(response -> {
                boolean success = response.getErrorCode() == 0;
                if (success) {
                    logger.info("Authentication successful");
                } else {
                    logger.warn("Authentication failed: errorCode={}", response.getErrorCode());
                }
                return success;
            });
    }

    /**
     * Packet을 사용한 인증 요청 (동기 방식 - Virtual Thread에서 효율적으로 동작)
     *
     * @param authPacket 인증 패킷
     * @return 인증 성공 여부
     * @throws ConnectorException 인증 실패 시
     */
    public boolean authenticate(Packet authPacket) {
        try {
            return authenticateAsync(authPacket).join();
        } catch (CompletionException e) {
            throw unwrapException(e);
        }
    }


    /**
     * 메시지 전송 (응답 없음)
     * <p>
     * 연결되지 않은 경우 OnError 콜백이 호출됩니다.
     *
     * @param packet 전송할 패킷
     */
    public void send(Packet packet) {
        if (!initialized) {
            logger.warn("Cannot send: Connector not initialized");
            return;
        }
        if (!isConnected()) {
            logger.warn("Cannot send: Not connected");
            clientNetwork.triggerError(
                ConnectorErrorCode.DISCONNECTED.getCode(),
                ConnectorErrorCode.DISCONNECTED.getMessage()
            );
            return;
        }
        clientNetwork.send(packet, stageId);
    }

    /**
     * 요청 전송 (비동기 응답)
     *
     * @param packet 요청 패킷
     * @return 응답 패킷 Future
     */
    public CompletableFuture<Packet> requestAsync(Packet packet) {
        if (!initialized) {
            return CompletableFuture.failedFuture(
                new IllegalStateException("Connector not initialized. Call init() first.")
            );
        }
        if (!isConnected()) {
            return CompletableFuture.failedFuture(
                new ConnectorException(ConnectorErrorCode.DISCONNECTED.getCode(),
                    ConnectorErrorCode.DISCONNECTED.getMessage(), packet)
            );
        }
        return clientNetwork.requestAsync(packet, stageId)
            .thenApply(response -> {
                if ("AuthenticateRequest".equals(packet.getMsgId()) && response.getErrorCode() == 0) {
                    clientNetwork.setAuthenticated(true);
                    this.stageId = extractAuthStageId(response.getPayload());
                }
                return response;
            });
    }

    /**
     * 요청 전송 (동기 방식 - Virtual Thread에서 효율적으로 동작)
     *
     * @param packet 요청 패킷
     * @return 응답 패킷
     * @throws ConnectorException    요청 실패 시
     * @throws IllegalStateException 초기화되지 않았거나 연결되지 않은 경우
     */
    public Packet request(Packet packet) {
        try {
            return requestAsync(packet).join();
        } catch (CompletionException e) {
            throw unwrapException(e);
        }
    }

    /**
     * 요청 전송 (콜백 방식)
     * <p>
     * 요청 실패 또는 타임아웃 시 OnError 콜백이 호출됩니다.
     *
     * @param packet   요청 패킷
     * @param callback 응답 콜백
     */
    public void request(Packet packet, Consumer<Packet> callback) {
        if (!initialized) {
            logger.warn("Cannot request: Connector not initialized");
            return;
        }
        if (!isConnected()) {
            clientNetwork.triggerError(
                ConnectorErrorCode.DISCONNECTED.getCode(),
                ConnectorErrorCode.DISCONNECTED.getMessage()
            );
            return;
        }

        requestAsync(packet)
            .thenAccept(callback)
            .exceptionally(e -> {
                Throwable cause = e.getCause() != null ? e.getCause() : e;
                if (cause instanceof ConnectorException ce) {
                    clientNetwork.triggerError(ce.getErrorCode(), ce.getMessage());
                } else {
                    // Always trigger error callback, not just log
                    logger.error("Request failed: {}", packet.getMsgId(), e);
                    clientNetwork.triggerError(
                        ConnectorErrorCode.UNKNOWN_ERROR.getCode(),
                        "Request failed: " + cause.getMessage()
                    );
                }
                return null;
            });
    }

    /**
     * 메인 스레드에서 콜백 실행
     * <p>
     * Unity, Godot 등의 게임 엔진에서 메인 스레드에서 호출해야 합니다.
     * 일반 서버 애플리케이션에서는 필요하지 않습니다.
     */
    public void mainThreadAction() {
        if (initialized) {
            clientNetwork.mainThreadAction();
        }
    }

    /**
     * 현재 Stage ID 반환
     *
     * @return Stage ID
     */
    public long getStageId() {
        return stageId;
    }

    // ===== 콜백 설정 =====

    /**
     * 연결 콜백 설정
     *
     * @param callback 연결 결과 콜백 (성공 여부)
     */
    public void setOnConnect(Runnable callback) {
        checkInitialized();
        clientNetwork.setOnConnect(success -> callback.run());
    }

    /**
     * 메시지 수신 콜백 설정
     *
     * @param callback 수신 콜백
     */
    public void setOnReceive(Consumer<Packet> callback) {
        checkInitialized();
        clientNetwork.setOnReceive(callback);
    }

    /**
     * 에러 콜백 설정
     *
     * @param callback 에러 콜백 (에러 코드, 메시지)
     */
    public void setOnError(BiConsumer<Integer, String> callback) {
        checkInitialized();
        clientNetwork.setOnError(callback);
    }

    /**
     * 연결 해제 콜백 설정
     *
     * @param callback 연결 해제 콜백
     */
    public void setOnDisconnect(Runnable callback) {
        checkInitialized();
        clientNetwork.setOnDisconnect(callback);
    }

    // ===== AutoCloseable =====

    /**
     * 리소스 정리
     */
    @Override
    public void close() {
        if (initialized) {
            logger.info("Closing connector");
            disconnect();
            clientNetwork.shutdown();
            initialized = false;
        }
    }

    // ===== Private Methods =====

    private void checkInitialized() {
        if (!initialized) {
            throw new IllegalStateException("Connector not initialized. Call init() first.");
        }
    }

    private static long extractAuthStageId(byte[] payload) {
        int offset = 0;
        while (offset < payload.length) {
            VarintResult tag = readVarint(payload, offset);
            if (!tag.success) {
                return 0L;
            }

            offset = tag.nextOffset;
            int fieldNumber = (int) (tag.value >>> 3);
            int wireType = (int) (tag.value & 0x07);

            if (fieldNumber == 2 && wireType == 0) {
                VarintResult value = readVarint(payload, offset);
                return value.success ? value.value : 0L;
            }

            int next = skipField(payload, offset, wireType);
            if (next <= offset) {
                return 0L;
            }
            offset = next;
        }

        return 0L;
    }

    private static int skipField(byte[] data, int offset, int wireType) {
        switch (wireType) {
            case 0: {
                VarintResult value = readVarint(data, offset);
                return value.success ? value.nextOffset : offset;
            }
            case 1:
                return (offset + 8 <= data.length) ? offset + 8 : offset;
            case 2: {
                VarintResult len = readVarint(data, offset);
                if (!len.success) {
                    return offset;
                }
                int next = (int) (len.nextOffset + len.value);
                return (next <= data.length) ? next : offset;
            }
            case 5:
                return (offset + 4 <= data.length) ? offset + 4 : offset;
            default:
                return offset;
        }
    }

    private static VarintResult readVarint(byte[] data, int offset) {
        long value = 0L;
        int shift = 0;
        while (offset < data.length && shift < 64) {
            int b = data[offset++] & 0xFF;
            value |= (long) (b & 0x7F) << shift;
            if ((b & 0x80) == 0) {
                return new VarintResult(true, value, offset);
            }
            shift += 7;
        }
        return new VarintResult(false, 0L, offset);
    }

    private static final class VarintResult {
        final boolean success;
        final long value;
        final int nextOffset;

        VarintResult(boolean success, long value, int nextOffset) {
            this.success = success;
            this.value = value;
            this.nextOffset = nextOffset;
        }
    }

    private void checkConnected() {
        checkInitialized();
        if (!isConnected()) {
            throw new IllegalStateException("Not connected to server");
        }
    }

    /**
     * CompletionException을 unwrap하여 원본 예외로 변환
     * ConnectorException이 아닌 경우 새로운 ConnectorException으로 감싼다.
     *
     * @param e CompletionException
     * @return 원본 RuntimeException 또는 ConnectorException
     */
    private RuntimeException unwrapException(CompletionException e) {
        Throwable cause = e.getCause();

        // ConnectorException인 경우 그대로 반환
        if (cause instanceof ConnectorException connectorEx) {
            return connectorEx;
        }

        // RuntimeException인 경우 그대로 반환
        if (cause instanceof RuntimeException runtimeEx) {
            return runtimeEx;
        }

        // 그 외의 경우 ConnectorException으로 감싸서 반환
        return new ConnectorException(
            5000,
            "Unexpected error: " + cause.getMessage(),
            cause
        );
    }

    /**
     * 현재 설정 반환
     *
     * @return ConnectorConfig
     */
    public ConnectorConfig getConfig() {
        return config;
    }
}
