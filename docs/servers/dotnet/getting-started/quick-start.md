# 빠른 시작 (Quick Start)

5분 안에 PlayHouse 게임 서버를 구축하고 실행하는 방법을 다룹니다.

## 목차

1. [프로젝트 설정](#프로젝트-설정)
2. [Stage와 Actor 구현](#stage와-actor-구현)
3. [서버 시작](#서버-시작)
4. [클라이언트 연결](#클라이언트-연결)

## 프로젝트 설정

### 1. 프로젝트 생성

```bash
dotnet new console -n MyGameServer
cd MyGameServer
```

### 2. PlayHouse 패키지 설치

```bash
dotnet add package PlayHouse
dotnet add package Google.Protobuf  # 메시지 정의용
```

### 3. 디렉토리 구조

```
MyGameServer/
├── MyGameServer.csproj
├── Program.cs
├── Proto/
│   └── messages.proto      # Protobuf 메시지 정의
├── MyStage.cs              # Stage 구현
└── MyActor.cs              # Actor 구현
```

## Stage와 Actor 구현

### Stage 구현 (MyStage.cs)

Stage는 게임 룸, 로비, 배틀 필드 등 플레이어들이 상호작용하는 공간을 나타냅니다.

```csharp
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;

public class MyStage : IStage
{
    public IStageLink StageLink { get; }

    public MyStage(IStageLink stageLink)
    {
        StageLink = stageLink;
    }

    // Stage 생성 시 호출
    public Task<(bool result, IPacket reply)> OnCreate(IPacket packet)
    {
        Console.WriteLine($"Stage {StageLink.StageId} created");

        // 성공 응답 반환
        var reply = Packet.Empty("CreateStageReply");
        return Task.FromResult<(bool, IPacket)>((true, reply));
    }

    public Task OnPostCreate()
    {
        // 타이머 설정, 초기 데이터 로딩 등
        return Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        Console.WriteLine($"Stage {StageLink.StageId} destroyed");
        return Task.CompletedTask;
    }

    // Actor가 Stage에 참가할 때 호출
    public Task<bool> OnJoinStage(IActor actor)
    {
        Console.WriteLine($"Actor {actor.ActorLink.AccountId} joined");
        return Task.FromResult(true); // true: 참가 허용
    }

    public Task OnPostJoinStage(IActor actor)
    {
        return Task.CompletedTask;
    }

    public ValueTask OnConnectionChanged(IActor actor, bool isConnected)
    {
        Console.WriteLine($"Actor {actor.ActorLink.AccountId} connection: {isConnected}");
        return ValueTask.CompletedTask;
    }

    // 클라이언트로부터 메시지 수신 시 호출
    public Task OnDispatch(IActor actor, IPacket packet)
    {
        Console.WriteLine($"Received {packet.MsgId} from {actor.ActorLink.AccountId}");

        // Echo 응답
        var reply = Packet.Empty(packet.MsgId + "Reply");
        actor.ActorLink.Reply(reply);

        return Task.CompletedTask;
    }

    // 서버간 메시지 수신 시 호출
    public Task OnDispatch(IPacket packet)
    {
        return Task.CompletedTask;
    }
}
```

### Actor 구현 (MyActor.cs)

Actor는 개별 클라이언트(플레이어)를 나타냅니다.

```csharp
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;

public class MyActor : IActor
{
    public IActorLink ActorLink { get; }

    public MyActor(IActorLink actorLink)
    {
        ActorLink = actorLink;
    }

    public Task OnCreate()
    {
        Console.WriteLine("Actor created");
        return Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        Console.WriteLine($"Actor {ActorLink.AccountId} destroyed");
        return Task.CompletedTask;
    }

    // 인증 처리 (필수!)
    public Task<(bool result, IPacket? reply)> OnAuthenticate(IPacket authPacket)
    {
        // ⚠️ 중요: AccountId + StageId를 반드시 함께 설정해야 함
        var accountId = Guid.NewGuid().ToString();
        var stageId = 1L;
        ActorLink.SetAuthContext(accountId, stageId);

        Console.WriteLine($"Actor authenticated: {accountId}");

        // 성공 응답
        var reply = Packet.Empty("AuthenticateReply");
        return Task.FromResult<(bool, IPacket?)>((true, reply));
    }

    public Task OnPostAuthenticate()
    {
        // 유저 데이터 로딩 등
        return Task.CompletedTask;
    }
}
```

## 서버 시작

### Bootstrap을 사용한 서버 시작 (Program.cs)

PlayHouse는 간편한 서버 구성을 위한 Bootstrap 시스템을 제공합니다.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlayHouse.Core.Play.Bootstrap;

var server = new PlayServerBootstrap()
    .Configure(options =>
    {
        options.ServerId = "game-server-1";
        options.BindEndpoint = "tcp://127.0.0.1:11200";  // 서버간 통신용
        options.TcpPort = 12000;                          // 클라이언트 연결용
        options.AuthenticateMessageId = "AuthenticateRequest";
        options.DefaultStageType = "MyStage";
    })
    .UseStage<MyStage, MyActor>("MyStage")
    .UseLoggerFactory(LoggerFactory.Create(builder =>
    {
        builder.AddConsole();
        builder.SetMinimumLevel(LogLevel.Information);
    }))
    .Build();

await server.StartAsync();
Console.WriteLine("Server started. Press Ctrl+C to stop.");

// 종료 시그널 대기
await Task.Delay(-1);
```

### DI를 사용한 서버 시작 (선택사항)

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PlayHouse.Extensions;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        // 커스텀 서비스 등록
        services.AddSingleton<IMyService, MyService>();

        // PlayServer 등록
        services.AddPlayServer(options =>
        {
            options.ServerId = "game-server-1";
            options.BindEndpoint = "tcp://127.0.0.1:11200";
            options.TcpPort = 12000;
            options.AuthenticateMessageId = "AuthenticateRequest";
            options.DefaultStageType = "MyStage";
        })
        .UseStage<MyStage, MyActor>("MyStage");
    })
    .Build();

await host.RunAsync();
```

### 서버 실행

```bash
dotnet run
```

출력:
```
Server started on TCP port 12000
Stage MyStage registered
Press Ctrl+C to stop.
```

## 클라이언트 연결

### C# 클라이언트 예제

```csharp
using PlayHouse.Connector;
using PlayHouse.Connector.Protocol;

// Connector 생성
var connector = new PlayHouse.Connector.Connector();
connector.Init(new ConnectorConfig
{
    RequestTimeoutMs = 10000
});

// 연결
var connected = await connector.ConnectAsync("127.0.0.1", 12000);
if (!connected)
{
    Console.WriteLine("Connection failed");
    return;
}

Console.WriteLine("Connected!");

// 인증
using var authPacket = Packet.Empty("AuthenticateRequest");
using var authResponse = await connector.AuthenticateAsync(authPacket);
Console.WriteLine($"Authenticated: {connector.IsAuthenticated()}");

// 메시지 전송 (Request-Response)
using var request = Packet.Empty("HelloRequest");
using var response = await connector.RequestAsync(request);
Console.WriteLine($"Received: {response.MsgId}");

// 연결 해제
connector.Disconnect();
await connector.DisposeAsync();
```

### 클라이언트 실행 흐름

```
1. Connect    → 서버 연결
2. Authenticate → Actor 생성, 인증, Stage 할당
3. Request/Send → 메시지 송수신
4. Disconnect → 연결 종료
```

## 핵심 개념 요약

### Stage
- 게임 룸, 로비, 전투 필드 등의 논리적 공간
- 하나의 StageId로 식별
- 여러 Actor를 포함

### Actor
- 개별 클라이언트(플레이어)를 나타냄
- AccountId와 StageId로 바인딩 (인증 시 설정 필수)
- Stage에 속해야 메시지 송수신 가능

### 생명주기

```
[클라이언트 연결]
    ↓
Connect → Stage.OnCreate (Stage가 없으면 생성)
    ↓
Authenticate → Actor.OnCreate → Actor.OnAuthenticate
    ↓
[메시지 송수신]
    ↓
Client Request → Stage.OnDispatch → Actor.ActorLink.Reply
    ↓
[연결 해제]
    ↓
Disconnect → Actor.OnDestroy
```

## 다음 단계

- [연결 및 인증](02-connection-auth.md): 연결과 인증 프로세스 상세 가이드
- [메시지 송수신](03-messaging.md): Send, Request, Push 패턴 상세 가이드

## 문제 해결

### "AccountId/StageId is invalid after authentication"

**원인:** `OnAuthenticate`에서 `ActorLink.SetAuthContext(accountId, stageId)`를 호출하지 않음

**해결:**
```csharp
public Task<(bool result, IPacket? reply)> OnAuthenticate(IPacket authPacket)
{
    // ✅ AccountId + StageId 설정 필수!
    ActorLink.SetAuthContext("user-123", 1001L);

    return Task.FromResult<(bool, IPacket?)>((true, Packet.Empty("AuthReply")));
}
```

### "Connection refused"

**원인:** 서버가 시작되지 않았거나 포트가 다름

**해결:**
- 서버가 실행 중인지 확인
- 클라이언트의 포트 번호가 서버의 `TcpPort`와 일치하는지 확인

### "Stage type not found"

**원인:** Stage 타입이 등록되지 않음

**해결:**
```csharp
.UseStage<MyStage, MyActor>("MyStage")  // Stage 타입 등록
```

## 추가 예제

전체 작동 예제는 다음 파일을 참조하세요:
- `tests/e2e/PlayHouse.E2E.Shared/Infrastructure/TestStageImpl.cs`
- `tests/e2e/PlayHouse.E2E.Shared/Infrastructure/TestActorImpl.cs`
- `tests/e2e/PlayHouse.E2E/Verifiers/ConnectionVerifier.cs`
