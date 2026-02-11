# 연결 및 인증 (Connection & Authentication)

> Play Server의 클라이언트 연결 및 인증 프로세스를 다룹니다.
> 전체 구조는 [개요](./overview.md)를, Stage/Actor 모델은 [Stage/Actor 개념](./stage-actor.md)을 참고하세요.

## 목차

1. [연결 프로세스 개요](#연결-프로세스-개요)
2. [클라이언트 연결 API](#클라이언트-연결-api)
3. [인증 구현](#인증-구현)
4. [연결 해제 처리](#연결-해제-처리)
5. [고급 주제](#고급-주제)

## 연결 프로세스 개요

### 전체 흐름

```
[클라이언트]              [서버]
    |                      |
    | 1. Connect           |
    |--------------------->| Stage.OnCreate (필요시)
    |                      |
    | 2. Authenticate      |
    |--------------------->| Actor.OnCreate
    |                      | Actor.OnAuthenticate ⚠️ AccountId 설정 필수
    |                      | Actor.OnPostAuthenticate
    |                      | Stage.OnJoinStage
    |                      | Stage.OnPostJoinStage
    |<---------------------|
    | 3. Request/Send      |
    |<-------------------->|
    |                      |
    | 4. Disconnect        |
    |--------------------->| Actor.OnDestroy
```

### 중요 개념

**StageId와 StageType**
- `StageId`: Stage의 고유 식별자 (long)
- `StageType`: Stage의 타입 이름 (string)
- 같은 StageType으로 여러 Stage 인스턴스 생성 가능

**AccountId**
- 개별 플레이어의 고유 식별자
- **반드시** `OnAuthenticate`에서 설정해야 함
- 설정하지 않으면 연결 종료됨

## 클라이언트 연결 API

### Connector 초기화

```csharp
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;

var connector = new PlayHouse.Connector.Connector();
connector.Init(new ConnectorConfig
{
    RequestTimeoutMs = 10000,  // 요청 타임아웃 (기본: 10초)
    HeartbeatIntervalMs = 30000  // 하트비트 간격 (기본: 30초)
});
```

### Connect (비동기 대기)

연결 완료를 기다리는 async/await 방식입니다.

```csharp
var stageId = 1L;
var stageType = "GameRoom";

var result = await connector.ConnectAsync("127.0.0.1", 12000, stageId, stageType);

if (result)
{
    Console.WriteLine("연결 성공!");
}
else
{
    Console.WriteLine("연결 실패!");
}
```

### Connect (콜백 방식)

비동기 콜백을 사용하는 방식입니다. Unity 등 메인 스레드가 중요한 환경에서 유용합니다.

```csharp
// OnConnect 콜백 등록
connector.OnConnect += (success) =>
{
    if (success)
    {
        Console.WriteLine("연결 성공!");
    }
    else
    {
        Console.WriteLine("연결 실패!");
    }
};

// 연결 시작 (블로킹 없음)
var result = await connector.ConnectAsync("127.0.0.1", 12000, stageId, stageType);

// 콜백 처리를 위해 주기적으로 MainThreadAction() 호출 필요
while (!connector.IsConnected())
{
    connector.MainThreadAction();
    await Task.Delay(50);
}
```

### 연결 상태 확인

```csharp
// 연결 여부
bool isConnected = connector.IsConnected();

// 인증 여부
bool isAuthenticated = connector.IsAuthenticated();
```

## 인증 구현

### 서버: Actor.OnAuthenticate 구현

인증은 `IActor.OnAuthenticate`에서 처리합니다. **AccountId 설정이 필수**입니다.

#### 기본 예제 (토큰 검증)

```csharp
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Shared;
using MyGame.Proto;  // Protobuf 메시지

public class GameActor : IActor
{
    public IActorLink ActorLink { get; }

    public GameActor(IActorLink actorLink)
    {
        ActorLink = actorLink;
    }

    public Task<(bool result, IPacket? reply)> OnAuthenticate(IPacket authPacket)
    {
        // 1. 인증 요청 파싱
        var authRequest = AuthenticateRequest.Parser.ParseFrom(authPacket.Payload.DataSpan);

        // 2. 토큰 검증
        if (!ValidateToken(authRequest.Token))
        {
            var errorReply = new AuthenticateReply
            {
                Success = false,
                ErrorMessage = "Invalid token"
            };
            return Task.FromResult<(bool, IPacket?)>((false, CPacket.Of(errorReply)));
        }

        // 3. ⚠️ 중요: AccountId 설정 (필수!)
        ActorLink.AccountId = authRequest.UserId;

        // 4. 성공 응답
        var reply = new AuthenticateReply
        {
            Success = true,
            AccountId = ActorLink.AccountId,
            SessionToken = GenerateSessionToken()
        };

        return Task.FromResult<(bool, IPacket?)>((true, CPacket.Of(reply)));
    }

    private bool ValidateToken(string token)
    {
        // 실제 토큰 검증 로직 (예: JWT 검증)
        return !string.IsNullOrEmpty(token);
    }

    private string GenerateSessionToken()
    {
        return Guid.NewGuid().ToString();
    }

    public Task OnPostAuthenticate()
    {
        // 유저 데이터 로딩, 초기화 등
        Console.WriteLine($"User {ActorLink.AccountId} authenticated");
        return Task.CompletedTask;
    }

    // ... 기타 IActor 메서드 구현
}
```

#### API 서버 연동 예제

외부 API 서버에서 유저 정보를 가져오는 경우:

```csharp
public class GameActor : IActor
{
    private readonly IApiClient _apiClient;
    public IActorLink ActorLink { get; }

    public GameActor(IActorLink actorLink, IApiClient apiClient)
    {
        ActorLink = actorLink;
        _apiClient = apiClient;
    }

    public async Task<(bool result, IPacket? reply)> OnAuthenticate(IPacket authPacket)
    {
        var authRequest = AuthenticateRequest.Parser.ParseFrom(authPacket.Payload.DataSpan);

        // 외부 API 서버에 인증 요청
        var apiResponse = await _apiClient.ValidateTokenAsync(authRequest.Token);
        if (!apiResponse.IsValid)
        {
            return (false, CPacket.Of(new AuthenticateReply
            {
                Success = false,
                ErrorMessage = "Authentication failed"
            }));
        }

        // AccountId + StageId 설정
        ActorLink.SetAuthContext(apiResponse.UserId, apiResponse.StageId);

        return (true, CPacket.Of(new AuthenticateReply
        {
            Success = true,
            AccountId = ActorLink.AccountId
        }));
    }
}
```

### 클라이언트: Authenticate API

#### AuthenticateAsync (권장)

```csharp
var authRequest = new AuthenticateRequest
{
    UserId = "player-123",
    Token = "jwt-token-here"
};

using var authPacket = new Packet(authRequest);
using var response = await connector.AuthenticateAsync(authPacket);

// 응답 파싱
var authReply = AuthenticateReply.Parser.ParseFrom(response.Payload.DataSpan);

if (authReply.Success)
{
    Console.WriteLine($"인증 성공! AccountId: {authReply.AccountId}");
}
else
{
    Console.WriteLine($"인증 실패: {authReply.ErrorMessage}");
}
```

#### Authenticate (콜백 방식)

```csharp
var authRequest = new AuthenticateRequest
{
    UserId = "player-123",
    Token = "jwt-token-here"
};

using var authPacket = new Packet(authRequest);

AuthenticateReply? authReply = null;

connector.Authenticate(authPacket, response =>
{
    // 콜백 내에서 파싱 (response는 콜백 후 자동 dispose)
    authReply = AuthenticateReply.Parser.ParseFrom(response.Payload.DataSpan);

    if (authReply.Success)
    {
        Console.WriteLine($"인증 성공! AccountId: {authReply.AccountId}");
    }
});

// 콜백 대기
while (authReply == null)
{
    connector.MainThreadAction();
    await Task.Delay(50);
}
```

### Proto 메시지 정의 예제

`Proto/auth.proto`:

```protobuf
syntax = "proto3";

package mygame;

message AuthenticateRequest {
    string user_id = 1;
    string token = 2;
}

message AuthenticateReply {
    bool success = 1;
    string account_id = 2;
    string session_token = 3;
    string error_message = 4;
}
```

## 연결 해제 처리

### 클라이언트 주도 연결 해제

```csharp
// 연결 해제
connector.Disconnect();

// Connector 정리
await connector.DisposeAsync();
```

### 서버 주도 연결 해제

Stage에서 Actor를 강제로 내보낼 수 있습니다.

```csharp
public async Task OnDispatch(IActor actor, IPacket packet)
{
    if (packet.MsgId == "KickRequest")
    {
        // 응답 먼저 전송
        actor.ActorLink.Reply(CPacket.Empty("KickReply"));

        // Actor를 Stage에서 제거 (연결 해제)
        await actor.ActorLink.LeaveStageAsync();
    }
}
```

### OnDisconnect 콜백

서버가 연결을 끊은 경우 클라이언트에서 알림을 받을 수 있습니다.

```csharp
connector.OnDisconnect += () =>
{
    Console.WriteLine("서버와의 연결이 끊어졌습니다.");
    // 재연결 로직 등
};

// 주기적으로 MainThreadAction() 호출 필요
connector.MainThreadAction();
```

**주의:** 클라이언트가 직접 `Disconnect()`를 호출한 경우에는 `OnDisconnect` 콜백이 **호출되지 않습니다**.

### Stage.OnConnectionChanged

Actor의 연결 상태 변경을 Stage에서 감지할 수 있습니다.

```csharp
public class GameStage : IStage
{
    public ValueTask OnConnectionChanged(IActor actor, bool isConnected)
    {
        if (isConnected)
        {
            Console.WriteLine($"{actor.ActorLink.AccountId} connected");
        }
        else
        {
            Console.WriteLine($"{actor.ActorLink.AccountId} disconnected");
            // 재연결 대기, 타임아웃 처리 등
        }

        return ValueTask.CompletedTask;
    }
}
```

## 고급 주제

### 재연결 (Reconnection)

연결이 끊어진 후 재연결하는 패턴입니다.

```csharp
public async Task<bool> ReconnectAsync(int maxRetries = 3)
{
    for (int i = 0; i < maxRetries; i++)
    {
        Console.WriteLine($"재연결 시도 {i + 1}/{maxRetries}...");

        var result = await connector.ConnectAsync("127.0.0.1", 12000);
        if (result)
        {
            // 재인증
            using var authPacket = new Packet(authRequest);
            await connector.AuthenticateAsync(authPacket);

            if (connector.IsAuthenticated())
            {
                Console.WriteLine("재연결 성공!");
                return true;
            }
        }

        await Task.Delay(1000 * (i + 1)); // 지수 백오프
    }

    Console.WriteLine("재연결 실패");
    return false;
}
```

### 여러 Stage에 연결

하나의 클라이언트가 여러 Stage(예: 로비 + 게임 룸)에 동시 연결할 수 있습니다.

```csharp
// Connector는 하나의 연결만 관리하므로 여러 개 생성 필요
var lobbyConnector = new PlayHouse.Connector.Connector();
lobbyConnector.Init(new ConnectorConfig());

var gameConnector = new PlayHouse.Connector.Connector();
gameConnector.Init(new ConnectorConfig());

// 로비 연결
await lobbyConnector.ConnectAsync("127.0.0.1", 12000);
await lobbyConnector.AuthenticateAsync(authPacket1);

// 게임 룸 연결
await gameConnector.ConnectAsync("127.0.0.1", 12000);
await gameConnector.AuthenticateAsync(authPacket2);
```

### Stage가 없으면 생성

클라이언트는 Connect 단계에서 StageId를 보내지 않습니다.
Stage 할당/선택은 OnAuthenticate 단계에서 ActorLink.SetAuthContext(accountId, stageId)로 결정됩니다.

```csharp
// 서버: Stage.OnCreate 구현
public Task<(bool result, IPacket reply)> OnCreate(IPacket packet)
{
    // packet에는 생성 정보 포함 가능
    var payload = CreateStagePayload.Parser.ParseFrom(packet.Payload.DataSpan);

    Console.WriteLine($"Creating stage {StageLink.StageId}");
    Console.WriteLine($"  - MaxPlayers: {payload.MaxPlayers}");

    // 초기화 로직...

    var reply = new CreateStageReply { Created = true };
    return Task.FromResult<(bool, IPacket)>((true, CPacket.Of(reply)));
}
```

### WebSocket 연결

TCP 대신 WebSocket을 사용할 수 있습니다 (브라우저 클라이언트 등).

**서버 설정:**
```csharp
var server = new PlayServerBootstrap()
    .Configure(options =>
    {
        options.ServerId = "game-server-1";
        options.BindEndpoint = "tcp://127.0.0.1:11200";
        options.TcpPort = null; // TCP 비활성화
    })
    .ConfigureWebSocket("/ws") // WebSocket 활성화
    .UseStage<MyStage, MyActor>("MyStage")
    .Build();
```

**클라이언트 연결:**
```csharp
// WebSocket URL
var wsUrl = "ws://127.0.0.1:8080/ws";

// PlayHouse Connector는 TCP만 지원하므로
// WebSocket 클라이언트는 별도 구현 필요
// (예: SignalR, Socket.IO 등과 통합)
```

### TLS/SSL 보안 연결

프로덕션 환경에서는 TLS로 암호화된 연결을 사용하세요.

**서버 설정:**
```csharp
var certificate = new X509Certificate2("cert.pfx", "password");

var server = new PlayServerBootstrap()
    .Configure(options => { /* ... */ })
    .ConfigureTcpWithTls(12000, certificate) // TLS 활성화
    .UseStage<MyStage, MyActor>("MyStage")
    .Build();
```

## 문제 해결

### "AccountId/StageId is invalid after authentication"

**원인:** `OnAuthenticate`에서 `ActorLink.SetAuthContext(accountId, stageId)`를 호출하지 않음

**해결:**
```csharp
public Task<(bool result, IPacket? reply)> OnAuthenticate(IPacket authPacket)
{
    // ✅ AccountId + StageId 설정 필수!
    ActorLink.SetAuthContext("user-123", 1001L);

    return Task.FromResult<(bool, IPacket?)>((true, null));
}
```

### "Connection timeout"

**원인:**
- 서버가 실행되지 않음
- 방화벽이 포트를 차단
- 네트워크 연결 불안정

**해결:**
- 서버가 실행 중인지 확인
- 포트 번호가 일치하는지 확인
- 방화벽 설정 확인

### "Authentication failed"

**원인:**
- 잘못된 토큰
- `OnAuthenticate`에서 false 반환

**해결:**
- 토큰이 유효한지 확인
- 서버 로그에서 상세 오류 확인

## 다음 단계

- [메시지 송수신](03-messaging.md): Send, Request, Push 패턴 상세 가이드

## 참고 자료

- E2E 테스트: `tests/e2e/PlayHouse.E2E/Verifiers/ConnectionVerifier.cs`
- Actor 구현 예제: `tests/e2e/PlayHouse.E2E.Shared/Infrastructure/TestActorImpl.cs`
