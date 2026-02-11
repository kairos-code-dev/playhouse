# 트러블슈팅 가이드 (Troubleshooting)

PlayHouse-NET 사용 중 발생할 수 있는 일반적인 문제와 해결 방법을 다룹니다.

## 목차

1. [연결 문제](#연결-문제)
2. [인증 문제](#인증-문제)
3. [Stage/Actor 문제](#stageactor-문제)
4. [메시지 문제](#메시지-문제)
5. [타이머/게임루프 문제](#타이머게임루프-문제)
6. [서버 간 통신 문제](#서버-간-통신-문제)
7. [성능 문제](#성능-문제)
8. [디버깅 팁](#디버깅-팁)

## 연결 문제

### "Connection refused" 에러

**증상:**
```
System.Net.Sockets.SocketException: Connection refused
```

**원인:**
- 서버가 실행되지 않음
- 잘못된 IP 주소 또는 포트
- 방화벽이 포트를 차단

**해결 방법:**

```csharp
// 1. 서버가 실행 중인지 확인
// 서버 로그에 다음 메시지가 있어야 함:
// "Server started on TCP port 12000"

// 2. IP와 포트가 정확한지 확인
var connector = new PlayHouse.Connector.Connector();
connector.Init(new ConnectorConfig());

// ✅ 로컬 테스트: 127.0.0.1
await connector.ConnectAsync("127.0.0.1", 12000, stageId, stageType);

// ✅ 원격 서버: 실제 IP 주소 사용
await connector.ConnectAsync("192.168.1.100", 12000, stageId, stageType);

// 3. 서버 설정 확인
var server = new PlayServerBootstrap()
    .Configure(options =>
    {
        options.TcpPort = 12000;  // 클라이언트 포트와 일치해야 함
    })
    .Build();
```

**방화벽 확인:**
```bash
# Linux: 포트가 열려있는지 확인
netstat -tuln | grep 12000

# Windows: 방화벽 규칙 추가
netsh advfirewall firewall add rule name="PlayHouse Server" dir=in action=allow protocol=TCP localport=12000
```

### Connection timeout (타임아웃)

**증상:**
```
ConnectorException: Connection timeout
```

**원인:**
- 네트워크 지연
- 서버 응답 없음
- 타임아웃 설정이 너무 짧음

**해결 방법:**

```csharp
// 타임아웃 증가
var connector = new PlayHouse.Connector.Connector();
connector.Init(new ConnectorConfig
{
    RequestTimeoutMs = 30000  // 30초로 증가 (기본: 10초)
});
```

### WebSocket 연결 실패

**증상:**
```
WebSocket connection failed
```

**원인:**
- WebSocket이 서버에서 활성화되지 않음
- 잘못된 WebSocket 경로
- CORS 설정 문제 (브라우저)

**해결 방법:**

```csharp
// 서버: WebSocket 활성화
var server = new PlayServerBootstrap()
    .Configure(options =>
    {
        options.ServerId = "game-server-1";
        options.BindEndpoint = "tcp://127.0.0.1:11200";
    })
    .ConfigureWebSocket("/ws")  // ✅ WebSocket 경로 설정
    .UseStage<MyStage, MyActor>("MyStage")
    .Build();

// 클라이언트: 올바른 URL 사용
// ws://localhost:8080/ws (HTTP)
// wss://example.com/ws (HTTPS)
```

## 인증 문제

### "AccountId/StageId is invalid after authentication"

**증상:**
```
InvalidOperationException: AccountId/StageId must be valid after authentication
클라이언트 연결이 즉시 끊김
```

**원인:**
`OnAuthenticate`에서 `ActorLink.SetAuthContext(accountId, stageId)`를 호출하지 않음

**해결 방법:**

```csharp
public class GameActor : IActor
{
    public IActorLink ActorLink { get; }

    public Task<(bool result, IPacket? reply)> OnAuthenticate(IPacket authPacket)
    {
        var authRequest = AuthenticateRequest.Parser.ParseFrom(authPacket.Payload.DataSpan);

        // ❌ 잘못된 코드 - 인증 컨텍스트 설정 안 함
        // return Task.FromResult<(bool, IPacket?)>((true, null));

        // ✅ 올바른 코드 - AccountId + StageId 설정 필수!
        ActorLink.SetAuthContext(authRequest.UserId, 1001L);

        var reply = new AuthenticateReply { Success = true };
        return Task.FromResult<(bool, IPacket?)>((true, CPacket.Of(reply)));
    }
}
```

### Authentication failed (인증 실패)

**증상:**
```
클라이언트: Authentication failed
서버: OnAuthenticate returned false
```

**원인:**
- 잘못된 토큰/자격 증명
- `OnAuthenticate`에서 `false` 반환

**해결 방법:**

```csharp
public async Task<(bool result, IPacket? reply)> OnAuthenticate(IPacket authPacket)
{
    var authRequest = AuthenticateRequest.Parser.ParseFrom(authPacket.Payload.DataSpan);

    // 토큰 검증
    if (string.IsNullOrEmpty(authRequest.Token))
    {
        Console.WriteLine($"Authentication failed: Empty token");

        // ❌ false 반환 시 연결 종료
        var errorReply = new AuthenticateReply
        {
            Success = false,
            ErrorMessage = "Invalid token"
        };
        return (false, CPacket.Of(errorReply));
    }

    // 외부 API 검증
    var isValid = await ValidateTokenAsync(authRequest.Token);
    if (!isValid)
    {
        Console.WriteLine($"Authentication failed: Invalid token");
        return (false, CPacket.Of(new AuthenticateReply
        {
            Success = false,
            ErrorMessage = "Token validation failed"
        }));
    }

    // ✅ 성공 시 AccountId + StageId 설정
    ActorLink.SetAuthContext(authRequest.UserId, 1001L);
    return (true, CPacket.Of(new AuthenticateReply { Success = true }));
}
```

### Session expired (세션 만료)

**증상:**
- 서버에서 갑자기 연결 해제
- `OnDisconnect` 콜백 호출됨

**원인:**
- 세션 타임아웃
- 하트비트 실패

**해결 방법:**

```csharp
// 클라이언트: 하트비트 간격 설정
var connector = new PlayHouse.Connector.Connector();
connector.Init(new ConnectorConfig
{
    HeartbeatIntervalMs = 30000  // 30초마다 하트비트 전송
});

// 재연결 로직 구현
connector.OnDisconnect += async () =>
{
    Console.WriteLine("Connection lost. Reconnecting...");

    for (int retry = 0; retry < 3; retry++)
    {
        var connected = await connector.ConnectAsync("127.0.0.1", 12000, stageId, stageType);
        if (connected)
        {
            await connector.AuthenticateAsync(authPacket);
            if (connector.IsAuthenticated())
            {
                Console.WriteLine("Reconnected successfully");
                return;
            }
        }

        await Task.Delay(1000 * (retry + 1)); // 지수 백오프
    }

    Console.WriteLine("Reconnection failed");
};
```

## Stage/Actor 문제

### "Stage type not found" 에러

**증상:**
```
KeyNotFoundException: Stage type 'GameRoom' is not registered
```

**원인:**
Stage 타입이 서버에 등록되지 않음

**해결 방법:**

```csharp
// ❌ 잘못된 코드 - Stage 미등록
var server = new PlayServerBootstrap()
    .Configure(options =>
    {
        options.DefaultStageType = "GameRoom";  // 등록 안 됨!
    })
    .Build();

// ✅ 올바른 코드 - Stage 등록
var server = new PlayServerBootstrap()
    .Configure(options =>
    {
        options.DefaultStageType = "GameRoom";
    })
    .UseStage<GameRoomStage, GameRoomActor>("GameRoom")  // ✅ 등록!
    .Build();

// 여러 Stage 타입 등록 가능
var server = new PlayServerBootstrap()
    .UseStage<LobbyStage, LobbyActor>("Lobby")
    .UseStage<GameRoomStage, GameRoomActor>("GameRoom")
    .UseStage<BattleStage, BattleActor>("Battle")
    .Build();
```

### Actor 생성 실패

**증상:**
```
InvalidOperationException: Failed to create Actor instance
```

**원인:**
- Actor 생성자가 잘못됨
- DI 의존성 주입 실패

**해결 방법:**

```csharp
// ✅ 올바른 Actor 생성자
public class GameActor : IActor
{
    public IActorLink ActorLink { get; }

    // IActorLink는 필수 파라미터
    public GameActor(IActorLink actorLink)
    {
        ActorLink = actorLink;
    }

    // DI 서비스도 주입 가능
    // public GameActor(IActorLink actorLink, IMyService myService)
}

// DI 서비스 등록
var server = new PlayServerBootstrap()
    .Configure(options => { /* ... */ })
    .UseServiceProvider(services =>
    {
        services.AddSingleton<IMyService, MyService>();  // ✅ 서비스 등록
    })
    .UseStage<MyStage, MyActor>("MyStage")
    .Build();
```

### OnDispatch가 호출되지 않음

**증상:**
- 클라이언트가 메시지를 전송했지만 서버에서 받지 못함
- `Stage.OnDispatch`가 호출되지 않음

**원인:**
- 인증되지 않은 상태에서 메시지 전송
- Stage에 Join하지 않음

**해결 방법:**

```csharp
// ✅ 올바른 순서: Connect → Authenticate → 메시지 전송
var connector = new PlayHouse.Connector.Connector();
connector.Init(new ConnectorConfig());

// 1. 연결
await connector.ConnectAsync("127.0.0.1", 12000, stageId, stageType);
if (!connector.IsConnected())
{
    Console.WriteLine("Connection failed");
    return;
}

// 2. 인증 (필수!)
using var authPacket = new Packet(authRequest);
await connector.AuthenticateAsync(authPacket);
if (!connector.IsAuthenticated())
{
    Console.WriteLine("Authentication failed");
    return;
}

// 3. 메시지 전송 - 이제 OnDispatch 호출됨
using var packet = new Packet(request);
using var response = await connector.RequestAsync(packet);
```

## 메시지 문제

### Request 타임아웃

**증상:**
```
ConnectorException: Request timeout
ErrorCode: RequestTimeout
```

**원인:**
- 서버가 `Reply`를 호출하지 않음
- 서버 처리 시간이 너무 오래 걸림
- 네트워크 지연

**해결 방법:**

```csharp
// 클라이언트: 타임아웃 증가
var connector = new PlayHouse.Connector.Connector();
connector.Init(new ConnectorConfig
{
    RequestTimeoutMs = 30000  // 30초
});

// 서버: 반드시 Reply 호출!
public async Task OnDispatch(IActor actor, IPacket packet)
{
    switch (packet.MsgId)
    {
        case "SlowRequest":
            // ❌ 잘못된 코드 - Reply 안 함
            // await ProcessSlowOperation();
            // return;  // Reply 없이 리턴 → 클라이언트 타임아웃!

            // ✅ 올바른 코드 - Reply 필수!
            await ProcessSlowOperation();
            actor.ActorLink.Reply(CPacket.Of(new SlowReply()));
            break;
    }
}

// AsyncIO 사용 (외부 API 호출 시)
public async Task OnDispatch(IActor actor, IPacket packet)
{
    if (packet.MsgId == "GetPlayerData")
    {
        // ✅ AsyncIO로 외부 API 호출
        var playerData = await actor.ActorLink.AsyncIO(async () =>
        {
            return await _httpClient.GetAsync("https://api.example.com/player");
        });

        actor.ActorLink.Reply(CPacket.Of(playerData));
    }
}
```

### Reply가 전달되지 않음

**증상:**
- 서버에서 `Reply`를 호출했지만 클라이언트가 받지 못함

**원인:**
- Request 컨텍스트가 없는 상태에서 `Reply` 호출
- 타이머 콜백에서 `Reply` 호출 (잘못된 패턴)

**해결 방법:**

```csharp
// ❌ 잘못된 코드 - 타이머에서 Reply 호출
public async Task OnDispatch(IActor actor, IPacket packet)
{
    if (packet.MsgId == "StartTimer")
    {
        StageLink.AddCountTimer(
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(1),
            10,
            async () =>
            {
                // ❌ 에러: 타이머 콜백에서는 Reply 불가!
                // actor.ActorLink.Reply(CPacket.Empty("TimerTick"));
            }
        );

        // Reply는 OnDispatch에서 즉시 호출해야 함
        actor.ActorLink.Reply(CPacket.Empty("StartTimerReply"));
    }
}

// ✅ 올바른 패턴 - 타이머에서는 Push 사용
public async Task OnDispatch(IActor actor, IPacket packet)
{
    if (packet.MsgId == "StartTimer")
    {
        // 즉시 응답
        actor.ActorLink.Reply(CPacket.Empty("StartTimerReply"));

        // 타이머에서는 Push로 알림
        StageLink.AddCountTimer(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            10,
            async () =>
            {
                // ✅ Push 메시지 전송
                actor.ActorLink.SendToClient(CPacket.Empty("TimerTick"));
            }
        );
    }
}
```

### 메시지 직렬화 에러 (Protobuf)

**증상:**
```
Google.Protobuf.InvalidProtocolBufferException: Protocol message contained an invalid tag (zero)
```

**원인:**
- Protobuf 메시지 파싱 실패
- MsgId와 실제 메시지 타입 불일치

**해결 방법:**

```csharp
// ✅ 올바른 패턴 - MsgId와 타입 일치
// Proto 정의
message EchoRequest {
    string content = 1;
}

// 클라이언트: Packet 생성
var request = new EchoRequest { Content = "Hello" };
using var packet = new Packet(request);
// packet.MsgId == "EchoRequest" (자동 설정)

// 서버: 파싱 시 MsgId 확인
public async Task OnDispatch(IActor actor, IPacket packet)
{
    if (packet.MsgId == "EchoRequest")
    {
        // ✅ 올바른 타입으로 파싱
        var request = EchoRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        // ❌ 잘못된 타입으로 파싱 → 에러!
        // var request = WrongRequest.Parser.ParseFrom(packet.Payload.DataSpan);
    }
}

// 예외 처리
public async Task OnDispatch(IActor actor, IPacket packet)
{
    try
    {
        var request = EchoRequest.Parser.ParseFrom(packet.Payload.DataSpan);
        // 처리...
    }
    catch (InvalidProtocolBufferException ex)
    {
        Console.WriteLine($"Failed to parse {packet.MsgId}: {ex.Message}");
        actor.ActorLink.Reply(500); // 에러 응답
    }
}
```

### "Packet already disposed" 에러

**증상:**
```
ObjectDisposedException: Cannot access a disposed object
```

**원인:**
- 이미 Dispose된 Packet을 사용 시도
- 콜백 후 Packet 참조 유지

**해결 방법:**

```csharp
// ❌ 잘못된 코드 - Packet 참조 저장
IPacket savedPacket = null;
connector.Request(packet, response =>
{
    savedPacket = response; // ❌ 콜백 후 자동 dispose됨!
});
// savedPacket 사용 시 에러 발생!

// ✅ 올바른 코드 - 데이터 복사
byte[] savedPayload = null;
connector.Request(packet, response =>
{
    savedPayload = response.Payload.DataSpan.ToArray(); // ✅ 데이터 복사
});

// ✅ 또는 콜백 내에서 처리
var result = new EchoReply();
connector.Request(packet, response =>
{
    result = EchoReply.Parser.ParseFrom(response.Payload.DataSpan);
});

// ✅ using 사용
using var packet = new Packet(request);
using var response = await connector.RequestAsync(packet);
// 스코프 종료 시 자동 dispose
```

## 타이머/게임루프 문제

### 타이머가 실행되지 않음

**증상:**
- `AddRepeatTimer` 호출했지만 콜백이 실행되지 않음

**원인:**
- 타이머를 추가했지만 Stage가 즉시 종료됨
- 잘못된 시간 설정

**해결 방법:**

```csharp
// ✅ 올바른 타이머 설정
public class GameStage : IStage
{
    private long _timerId;

    public Task OnPostCreate()
    {
        // 타이머 시작
        _timerId = StageLink.AddRepeatTimer(
            initialDelay: TimeSpan.FromSeconds(5),  // 5초 후 시작
            period: TimeSpan.FromSeconds(10),       // 10초마다 반복
            callback: async () =>
            {
                Console.WriteLine("Timer tick!");
                await DoSomething();
            }
        );

        Console.WriteLine($"Timer started: {_timerId}");
        return Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        // ✅ 정리: 타이머 취소
        if (StageLink.HasTimer(_timerId))
        {
            StageLink.CancelTimer(_timerId);
        }
        return Task.CompletedTask;
    }
}
```

### GameLoop 성능 이슈

**증상:**
- 게임루프가 느려짐
- CPU 사용률 높음
- "Spiral of Death" (누적 지연)

**원인:**
- 타임스텝이 너무 짧음
- 게임루프 콜백이 너무 무거움

**해결 방법:**

```csharp
// ✅ 적절한 타임스텝 선택
public Task OnPostCreate()
{
    // 60 FPS는 액션 게임에 적합하지만 CPU 부하가 높음
    // StageLink.StartGameLoop(TimeSpan.FromMilliseconds(16), OnTick); // ~60 Hz

    // ✅ 20 FPS면 대부분의 게임에 충분
    StageLink.StartGameLoop(TimeSpan.FromMilliseconds(50), OnTick); // 20 Hz

    return Task.CompletedTask;
}

// ✅ 게임루프 콜백 최적화
private Task OnGameTick(TimeSpan deltaTime, TimeSpan totalElapsed)
{
    // ❌ 무거운 작업은 피하기
    // await _httpClient.GetAsync("https://...");  // 외부 API 호출 X
    // Thread.Sleep(100);  // 블로킹 작업 X

    // ✅ 가벼운 게임 로직만 수행
    UpdatePhysics(deltaTime);
    CheckCollisions();

    // ✅ 브로드캐스트는 일정 간격으로
    _tickCount++;
    if (_tickCount % 4 == 0) // 200ms마다 (50ms × 4)
    {
        BroadcastGameState();
    }

    return Task.CompletedTask;
}

// ✅ MaxAccumulatorCap 설정으로 "Spiral of Death" 방지
public Task OnPostCreate()
{
    var config = new GameLoopConfig
    {
        FixedTimestep = TimeSpan.FromMilliseconds(50),
        MaxAccumulatorCap = TimeSpan.FromMilliseconds(200) // 최대 4 tick 누적
    };

    StageLink.StartGameLoop(config, OnGameTick);
    return Task.CompletedTask;
}
```

### 타이머 취소 후에도 콜백 호출됨

**증상:**
- `CancelTimer` 호출 후에도 타이머 콜백이 실행됨

**원인:**
- 타이머 취소와 콜백 실행 사이의 Race Condition
- 이미 스케줄된 콜백은 실행됨

**해결 방법:**

```csharp
// ✅ 취소 플래그 사용
public class GameStage : IStage
{
    private long _timerId;
    private bool _timerCancelled = false;

    public Task OnPostCreate()
    {
        _timerId = StageLink.AddRepeatTimer(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1),
            async () =>
            {
                // ✅ 취소 플래그 확인
                if (_timerCancelled)
                {
                    return;
                }

                await DoSomething();
            }
        );

        return Task.CompletedTask;
    }

    private void StopTimer()
    {
        _timerCancelled = true; // ✅ 플래그 먼저 설정
        if (StageLink.HasTimer(_timerId))
        {
            StageLink.CancelTimer(_timerId);
        }
    }

    public Task OnDestroy()
    {
        StopTimer();
        return Task.CompletedTask;
    }
}
```

## 서버 간 통신 문제

### SendToApi가 동작하지 않음

**증상:**
- `SendToApi` 호출했지만 API 서버가 메시지를 받지 못함

**원인:**
- API 서버가 등록되지 않음
- SystemController 설정 누락
- 서버간 네트워크 문제

**해결 방법:**

```csharp
// Play 서버 설정
var playServer = new PlayServerBootstrap()
    .Configure(options =>
    {
        options.ServerId = "play-1";
        options.BindEndpoint = "tcp://127.0.0.1:11200";  // Play 서버 바인드
    })
    .UseSystemController<StaticSystemController>(new[]
    {
        "api-1=tcp://127.0.0.1:11100"  // ✅ API 서버 주소 등록
    })
    .UseStage<MyStage, MyActor>("MyStage")
    .Build();

// API 서버 설정
var apiServer = new ApiServerBootstrap()
    .Configure(options =>
    {
        options.ServerId = "api-1";
        options.BindEndpoint = "tcp://127.0.0.1:11100";  // API 서버 바인드
    })
    .UseSystemController<StaticSystemController>(new[]
    {
        "play-1=tcp://127.0.0.1:11200"  // Play 서버 주소 등록
    })
    .UseControllers()
    .Build();

// Stage에서 API 서버로 전송
public async Task OnDispatch(IActor actor, IPacket packet)
{
    // ✅ SendToApi 사용
    StageLink.SendToApi("api-1", CPacket.Of(request));

    // ✅ RequestToApi 사용 (응답 대기)
    try
    {
        var response = await StageLink.RequestToApi("api-1", CPacket.Of(request));
        var data = Response.Parser.ParseFrom(response.Payload.DataSpan);
    }
    catch (InvalidOperationException ex)
    {
        Console.WriteLine($"API server not available: {ex.Message}");
    }
}
```

### RequestToStage 타임아웃

**증상:**
```
TimeoutException: RequestToStage timeout
```

**원인:**
- 대상 Stage가 응답하지 않음
- Stage가 존재하지 않음
- 네트워크 지연

**해결 방법:**

```csharp
// 클라이언트: Stage 존재 여부 확인
public async Task OnDispatch(IActor actor, IPacket packet)
{
    var targetStageId = 100L;
    var targetStageType = "GameRoom";

    try
    {
        // RequestToStage 호출
        var response = await StageLink.RequestToStage(
            targetStageType,
            targetStageId,
            CPacket.Of(request)
        );

        var data = Response.Parser.ParseFrom(response.Payload.DataSpan);
    }
    catch (TimeoutException ex)
    {
        Console.WriteLine($"Stage {targetStageId} timeout: {ex.Message}");
        actor.ActorLink.Reply(504); // Gateway Timeout
    }
}

// 대상 Stage: 반드시 응답 필요
public class TargetStage : IStage
{
    public async Task OnDispatch(IPacket packet)
    {
        if (packet.MsgId == "CrossStageRequest")
        {
            var request = CrossStageRequest.Parser.ParseFrom(packet.Payload.DataSpan);

            // ✅ 반드시 Reply 호출!
            StageLink.ReplyToStage(CPacket.Of(new CrossStageReply
            {
                Success = true
            }));
        }
    }
}
```

### 서버 연결 끊김

**증상:**
- 서버간 통신이 갑자기 끊김
- `InvalidOperationException: No available server`

**원인:**
- 대상 서버가 다운됨
- 네트워크 단절
- 하트비트 실패

**해결 방법:**

```csharp
// 에러 처리 및 재시도
public async Task OnDispatch(IActor actor, IPacket packet)
{
    var maxRetries = 3;
    for (int i = 0; i < maxRetries; i++)
    {
        try
        {
            var response = await StageLink.RequestToApi("api-1", CPacket.Of(request));
            // 성공
            break;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("No available server"))
        {
            Console.WriteLine($"API server unavailable, retry {i + 1}/{maxRetries}");

            if (i == maxRetries - 1)
            {
                // 최종 실패
                actor.ActorLink.Reply(503); // Service Unavailable
                return;
            }

            await Task.Delay(1000 * (i + 1)); // 지수 백오프
        }
    }
}

// ServiceId 기반 통신으로 장애 허용
public async Task OnDispatch(IActor actor, IPacket packet)
{
    ushort serviceId = 100;

    // ✅ ServiceId 사용 - 여러 서버 중 하나라도 살아있으면 성공
    try
    {
        var response = await StageLink.RequestToApiService(
            serviceId,
            CPacket.Of(request),
            ServerSelectionPolicy.RoundRobin
        );
        // 성공
    }
    catch (InvalidOperationException ex)
    {
        Console.WriteLine($"All servers in service {serviceId} are down");
        actor.ActorLink.Reply(503);
    }
}
```

## 성능 문제

### 메모리 누수 패턴

**증상:**
- 메모리 사용량이 계속 증가
- GC가 메모리를 회수하지 못함

**원인:**
- Packet을 Dispose하지 않음
- 이벤트 핸들러 등록 후 해제하지 않음
- 타이머를 취소하지 않음

**해결 방법:**

```csharp
// ✅ Packet은 항상 using으로 사용
public async Task OnDispatch(IActor actor, IPacket packet)
{
    // ❌ 잘못된 패턴
    // var request = new EchoRequest { Content = "Hello" };
    // var sendPacket = new Packet(request);
    // actor.ActorLink.SendToClient(sendPacket);
    // // Dispose 누락 → 메모리 누수!

    // ✅ 올바른 패턴 - using 사용
    var request = new EchoRequest { Content = "Hello" };
    using var sendPacket = new Packet(request);
    actor.ActorLink.SendToClient(sendPacket);
    // 자동으로 Dispose됨
}

// ✅ 타이머 정리
private readonly List<long> _timerIds = new();

public Task OnPostCreate()
{
    var timerId = StageLink.AddRepeatTimer(
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(1),
        async () => { await OnTick(); }
    );

    _timerIds.Add(timerId); // 기록
    return Task.CompletedTask;
}

public Task OnDestroy()
{
    // ✅ 모든 타이머 취소
    foreach (var timerId in _timerIds)
    {
        if (StageLink.HasTimer(timerId))
        {
            StageLink.CancelTimer(timerId);
        }
    }

    _timerIds.Clear();
    return Task.CompletedTask;
}

// ✅ 게임루프 정리
public Task OnDestroy()
{
    if (StageLink.IsGameLoopRunning)
    {
        StageLink.StopGameLoop();
    }

    return Task.CompletedTask;
}
```

### GC 압박 줄이기

**증상:**
- 빈번한 GC 발생
- GC Pause로 인한 성능 저하

**해결 방법:**

```csharp
// ✅ 객체 재사용 (Object Pooling)
public class GameStage : IStage
{
    // ❌ 매번 새 리스트 생성
    // private List<IActor> GetNearbyActors()
    // {
    //     return _actors.Where(a => IsNearby(a)).ToList(); // 매번 할당!
    // }

    // ✅ 리스트 재사용
    private readonly List<IActor> _nearbyActors = new();

    private List<IActor> GetNearbyActors()
    {
        _nearbyActors.Clear(); // 기존 리스트 재사용
        foreach (var actor in _actors.Values)
        {
            if (IsNearby(actor))
            {
                _nearbyActors.Add(actor);
            }
        }
        return _nearbyActors;
    }
}

// ✅ LINQ 할당 최소화
public class GameStage : IStage
{
    // ❌ LINQ로 인한 할당
    // var count = _actors.Where(a => a.IsActive).Count();

    // ✅ 직접 카운트
    private int GetActiveActorCount()
    {
        int count = 0;
        foreach (var actor in _actors.Values)
        {
            if (IsActive(actor))
            {
                count++;
            }
        }
        return count;
    }
}

// ✅ StringBuilder 사용
public class GameStage : IStage
{
    private readonly StringBuilder _sb = new StringBuilder();

    private string BuildMessage()
    {
        // ❌ 문자열 연결 (매번 할당)
        // return "Player " + accountId + " joined " + stageId;

        // ✅ StringBuilder 재사용
        _sb.Clear();
        _sb.Append("Player ");
        _sb.Append(accountId);
        _sb.Append(" joined ");
        _sb.Append(stageId);
        return _sb.ToString();
    }
}
```

### 병목 지점 찾기

**증상:**
- 응답 시간이 느림
- 특정 요청이 서버를 느리게 만듦

**해결 방법:**

```csharp
// ✅ 성능 측정 추가
public async Task OnDispatch(IActor actor, IPacket packet)
{
    var sw = System.Diagnostics.Stopwatch.StartNew();

    try
    {
        switch (packet.MsgId)
        {
            case "SlowRequest":
                await HandleSlowRequest(actor, packet);
                break;
        }
    }
    finally
    {
        sw.Stop();
        if (sw.ElapsedMilliseconds > 100)
        {
            Console.WriteLine($"SLOW: {packet.MsgId} took {sw.ElapsedMilliseconds}ms");
        }
    }
}

// ✅ AsyncIO로 블로킹 작업 분리
public async Task HandleSlowRequest(IActor actor, IPacket packet)
{
    var request = SlowRequest.Parser.ParseFrom(packet.Payload.DataSpan);

    // ❌ 블로킹 작업 - Stage 이벤트 루프를 막음
    // var data = _httpClient.GetAsync("https://...").Result;

    // ✅ AsyncIO 사용 - 별도 스레드에서 실행
    var data = await actor.ActorLink.AsyncIO(async () =>
    {
        return await _httpClient.GetAsync("https://api.example.com/data");
    });

    actor.ActorLink.Reply(CPacket.Of(new SlowReply { Data = data }));
}
```

## 디버깅 팁

### 로깅 설정

```csharp
// Bootstrap에서 로깅 설정
var server = new PlayServerBootstrap()
    .Configure(options => { /* ... */ })
    .UseLoggerFactory(LoggerFactory.Create(builder =>
    {
        // 콘솔 로깅 추가
        builder.AddConsole();

        // 로그 레벨 설정
        builder.SetMinimumLevel(LogLevel.Debug); // Debug, Information, Warning, Error

        // 특정 카테고리만 필터링
        builder.AddFilter("PlayHouse.Core.Play", LogLevel.Debug);
        builder.AddFilter("Microsoft", LogLevel.Warning);
    }))
    .UseStage<MyStage, MyActor>("MyStage")
    .Build();

// 커스텀 로깅
public class GameStage : IStage
{
    private readonly ILogger<GameStage> _logger;

    public GameStage(IStageLink stageLink, ILogger<GameStage> logger)
    {
        StageLink = stageLink;
        _logger = logger;
    }

    public async Task OnDispatch(IActor actor, IPacket packet)
    {
        _logger.LogDebug("Received {MsgId} from {AccountId}", packet.MsgId, actor.ActorLink.AccountId);

        try
        {
            // 처리...
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle {MsgId}", packet.MsgId);
        }
    }
}
```

### 메시지 추적

```csharp
// 메시지 흐름 추적
public class GameStage : IStage
{
    public async Task OnDispatch(IActor actor, IPacket packet)
    {
        // 메시지 시작
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] → {packet.MsgId} from {actor.ActorLink.AccountId}");

        try
        {
            await HandleMessage(actor, packet);

            // 메시지 종료
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ✓ {packet.MsgId}");
        }
        catch (Exception ex)
        {
            // 메시지 실패
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] ✗ {packet.MsgId}: {ex.Message}");
        }
    }
}

// 출력 예:
// [14:23:15.123] → EchoRequest from player-1
// [14:23:15.125] ✓ EchoRequest
// [14:23:16.456] → SlowRequest from player-2
// [14:23:17.890] ✓ SlowRequest
```

### 상태 덤프

```csharp
// Stage 상태 디버깅
public class GameStage : IStage
{
    private readonly Dictionary<string, IActor> _actors = new();
    private int _tickCount = 0;

    // 디버그 명령 처리
    public async Task OnDispatch(IActor actor, IPacket packet)
    {
        if (packet.MsgId == "DebugDump")
        {
            DumpState();
            actor.ActorLink.Reply(CPacket.Empty("DebugDumpReply"));
            return;
        }

        // 일반 처리...
    }

    private void DumpState()
    {
        Console.WriteLine("=== Stage State Dump ===");
        Console.WriteLine($"StageId: {StageLink.StageId}");
        Console.WriteLine($"StageType: {StageLink.StageType}");
        Console.WriteLine($"ActorCount: {_actors.Count}");
        Console.WriteLine($"TickCount: {_tickCount}");
        Console.WriteLine($"IsGameLoopRunning: {StageLink.IsGameLoopRunning}");

        Console.WriteLine("\nActors:");
        foreach (var (accountId, actor) in _actors)
        {
            Console.WriteLine($"  - {accountId}");
        }

        Console.WriteLine("=======================");
    }
}
```

### 클라이언트 디버깅

```csharp
// 클라이언트 연결 디버깅
var connector = new PlayHouse.Connector.Connector();
connector.Init(new ConnectorConfig());

// 모든 콜백 등록
connector.OnConnect += success =>
{
    Console.WriteLine($"[OnConnect] Success={success}");
};

connector.OnDisconnect += () =>
{
    Console.WriteLine($"[OnDisconnect] Disconnected from server");
};

connector.OnReceive += (stageId, stageType, packet) =>
{
    Console.WriteLine($"[OnReceive] {packet.MsgId} from Stage {stageId}");
};

connector.OnError += (stageId, stageType, errorCode, request) =>
{
    Console.WriteLine($"[OnError] ErrorCode={errorCode}, Request={request.MsgId}");
};

// 연결
await connector.ConnectAsync("127.0.0.1", 12000, stageId, stageType);

// 메시지 전송 추적
using var packet = new Packet(request);
Console.WriteLine($"[Send] {packet.MsgId}");
using var response = await connector.RequestAsync(packet);
Console.WriteLine($"[Recv] {response.MsgId}");
```

## 추가 리소스

- [빠른 시작 가이드](01-quick-start.md)
- [연결 및 인증 가이드](02-connection-auth.md)
- [메시지 송수신 가이드](03-messaging.md)
- [타이머 및 게임루프 가이드](06-timer-gameloop.md)

## FAQ

**Q: "AccountId/StageId is invalid" 에러가 계속 발생해요**

A: `OnAuthenticate`에서 `ActorLink.SetAuthContext("user-id", 1001L)`를 반드시 호출해야 합니다. 이 설정이 누락되면 연결이 즉시 종료됩니다.

**Q: Reply를 호출했는데 클라이언트가 타임아웃이 발생해요**

A: `Reply`는 Request 컨텍스트 내에서만 호출할 수 있습니다. 타이머 콜백이나 비동기 작업 후에는 `SendToClient` (Push)를 사용하세요.

**Q: 게임루프가 느려요**

A: 타임스텝을 늘리거나 (예: 16ms → 50ms), 게임루프 콜백을 최적화하세요. 외부 API 호출 같은 무거운 작업은 `AsyncIO`로 분리하세요.

**Q: 메모리 사용량이 계속 증가해요**

A: Packet은 항상 `using`으로 사용하고, 타이머는 `OnDestroy`에서 취소하세요. 게임루프도 종료 시 중지하세요.

**Q: SendToApi가 동작하지 않아요**

A: `UseSystemController`로 API 서버를 등록했는지 확인하세요. SystemController는 서버간 통신을 위한 필수 설정입니다.

**Q: Stage가 자꾸 종료되어요**

A: Stage는 모든 Actor가 나가면 자동으로 종료됩니다. 영구 Stage가 필요하면 `CloseStage()`를 명시적으로 호출할 때까지 유지되도록 로직을 구현하세요.
