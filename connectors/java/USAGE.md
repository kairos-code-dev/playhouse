# PlayHouse Java Connector 사용 가이드

## 기본 사용법

### 1. Connector 초기화 및 연결

```java
import com.playhouse.connector.*;

public class Example {
    public static void main(String[] args) {
        // 설정 생성
        ConnectorConfig config = ConnectorConfig.builder()
            .requestTimeoutMs(10000)
            .heartbeatIntervalMs(5000)
            .build();

        // Connector 생성 및 초기화
        try (Connector connector = new Connector()) {
            connector.init(config);

            // 콜백 설정
            connector.setOnConnect(() -> {
                System.out.println("Connected to server!");
            });

            connector.setOnReceive(packet -> {
                System.out.println("Received: " + packet.getMsgId());
                System.out.println("Payload size: " + packet.getPayload().length);
            });

            connector.setOnError((errorCode, message) -> {
                System.err.println("Error " + errorCode + ": " + message);
            });

            connector.setOnDisconnect(() -> {
                System.out.println("Disconnected from server");
            });

            // 서버 연결
            connector.connectAsync("localhost", 34001).join();

            System.out.println("Connected: " + connector.isConnected());

            // 여기서 메시지 송수신...

        } catch (Exception e) {
            e.printStackTrace();
        }
    }
}
```

### 2. 인증

```java
// 간편 인증 API
boolean authenticated = connector.authenticateAsync("game", "user123", new byte[0]).join();
System.out.println("Authenticated: " + authenticated);

// 또는 커스텀 인증 패킷 사용
Packet authPacket = Packet.builder("AuthRequest")
    .payload(authData)
    .build();

Packet authResponse = connector.requestAsync(authPacket).join();
if (authResponse.getErrorCode() == 0) {
    System.out.println("Authentication successful!");
}
```

### 3. 메시지 전송 (단방향)

```java
// 빈 메시지
Packet emptyPacket = Packet.empty("Ping");
connector.send(emptyPacket);

// 바이트 배열 페이로드
byte[] data = "Hello".getBytes(StandardCharsets.UTF_8);
Packet packet = Packet.fromBytes("ChatMessage", data);
connector.send(packet);

// Builder 사용
Packet packet = Packet.builder("GameAction")
    .payload(actionData)
    .build();
connector.send(packet);
```

### 4. 요청-응답 (양방향)

```java
// CompletableFuture 방식 (권장)
Packet request = Packet.fromBytes("EchoRequest", "Hello".getBytes());

connector.requestAsync(request)
    .thenAccept(response -> {
        System.out.println("Response received: " + response.getMsgId());
        System.out.println("Error code: " + response.getErrorCode());

        String content = new String(response.getPayload(), StandardCharsets.UTF_8);
        System.out.println("Content: " + content);
    })
    .exceptionally(e -> {
        System.err.println("Request failed: " + e.getMessage());
        return null;
    });

// 동기 방식 (블로킹)
try {
    Packet response = connector.requestAsync(request).join();
    System.out.println("Response: " + response.getMsgId());
} catch (Exception e) {
    System.err.println("Request failed: " + e.getMessage());
}

// 콜백 방식
connector.request(request, response -> {
    System.out.println("Response: " + response.getMsgId());
});
```

### 5. 타임아웃 처리

```java
import java.util.concurrent.TimeUnit;
import java.util.concurrent.TimeoutException;

Packet request = Packet.fromBytes("SlowOperation", data);

connector.requestAsync(request)
    .orTimeout(5, TimeUnit.SECONDS)
    .thenAccept(response -> {
        System.out.println("Response: " + response.getMsgId());
    })
    .exceptionally(e -> {
        if (e.getCause() instanceof TimeoutException) {
            System.err.println("Request timed out after 5 seconds");
        } else {
            System.err.println("Request failed: " + e.getMessage());
        }
        return null;
    });
```

### 6. 에러 처리

```java
Packet request = Packet.fromBytes("TestRequest", data);

connector.requestAsync(request)
    .thenAccept(response -> {
        if (response.hasError()) {
            System.err.println("Server error: " + response.getErrorCode());
            // 에러 처리
        } else {
            // 성공 처리
            System.out.println("Success!");
        }
    })
    .exceptionally(e -> {
        if (e instanceof ConnectorException) {
            ConnectorException ce = (ConnectorException) e;
            System.err.println("Connector error: " + ce.getErrorCode());
        }
        return null;
    });
```

### 7. 리소스 정리

```java
// try-with-resources 사용 (권장)
try (Connector connector = new Connector()) {
    connector.init();
    // 사용...
} // 자동으로 close() 호출됨

// 수동 정리
Connector connector = new Connector();
try {
    connector.init();
    // 사용...
} finally {
    connector.close();
}

// 또는 명시적 연결 해제
connector.disconnect();
```

## 고급 사용법

### 패킷 정보 확인

```java
Packet packet = ...;

System.out.println("Message ID: " + packet.getMsgId());
System.out.println("Message Seq: " + packet.getMsgSeq());
System.out.println("Stage ID: " + packet.getStageId());
System.out.println("Error Code: " + packet.getErrorCode());
System.out.println("Original Size: " + packet.getOriginalSize());
System.out.println("Is Compressed: " + packet.isCompressed());
System.out.println("Has Error: " + packet.hasError());

byte[] payload = packet.getPayload();
ByteBuffer buffer = packet.getPayloadBuffer(); // Little-endian
```

### Stage ID 관리

```java
// Stage ID는 인증 성공 시 Connector 내부에 자동 반영됨
long currentStageId = connector.getStageId();
```

### 설정 옵션

```java
ConnectorConfig config = ConnectorConfig.builder()
    .sendBufferSize(65536)          // 송신 버퍼: 64KB
    .receiveBufferSize(262144)      // 수신 버퍼: 256KB
    .heartbeatIntervalMs(10000)     // Heartbeat: 10초
    .requestTimeoutMs(30000)        // 요청 타임아웃: 30초
    .enableReconnect(false)         // 자동 재연결 비활성화
    .reconnectIntervalMs(5000)      // 재연결 간격: 5초
    .build();

connector.init(config);
```

## 테스트 예제

```java
import org.junit.jupiter.api.Test;
import static org.assertj.core.api.Assertions.*;

class ConnectorIntegrationTest {

    @Test
    void testEchoRequest() throws Exception {
        ConnectorConfig config = ConnectorConfig.builder()
            .requestTimeoutMs(5000)
            .build();

        try (Connector connector = new Connector()) {
            connector.init(config);
            connector.connectAsync("localhost", 34001).join();

            // 인증
            connector.authenticateAsync("game", "testuser", new byte[0]).join();

            // Echo 요청
            Packet request = Packet.fromBytes("EchoRequest", "Hello".getBytes());
            Packet response = connector.requestAsync(request).join();

            assertThat(response.getErrorCode()).isZero();
            assertThat(response.getMsgId()).isEqualTo("EchoReply");
        }
    }
}
```

## 주의사항

1. **초기화**: 연결 전에 반드시 `init()` 호출
2. **리소스 정리**: `try-with-resources` 또는 `close()` 호출로 리소스 정리
3. **스레드 안전성**: Connector는 스레드 안전하게 설계됨
4. **에러 처리**: 항상 응답의 `errorCode` 확인
5. **타임아웃**: 적절한 타임아웃 설정으로 리소스 누수 방지

## 게임 엔진 통합

Unity, Godot 등에서 메인 스레드에서 콜백을 실행하려면:

```java
// 게임 루프에서 매 프레임 호출
public void update() {
    connector.mainThreadAction();
}
```

일반 서버 애플리케이션에서는 필요하지 않습니다.
