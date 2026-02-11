# PlayHouse-NET

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

**PlayHouse-NET**은 실시간 멀티플레이 게임을 위한 고성능 서버 프레임워크입니다.

> 극장(PlayHouse)에서 무대(Stage) 위의 배우(Actor)들이 연기하듯,
> 게임 서버에서 게임방(Stage)의 플레이어(Actor)들이 게임을 합니다.

```
🎭 PlayHouse = 극장 (프레임워크)
    └── 🎬 Play Server = 연극 (실시간 게임 서버)
            └── 🎪 Stage = 무대 (게임방)
                    └── 🎭 Actor = 배우 (플레이어)
```

---

## 특징

- **Stage/Actor 모델** - 게임방과 플레이어를 직관적으로 모델링
- **API Server + Play Server** - 아웃게임(로비, 매칭)과 인게임(실시간 게임) 분리
- **손쉬운 서버 간 통신** - Sender API로 복잡한 네트워크 코드 제거
- **고해상도 GameLoop** - 60fps+ 고정 타임스텝 게임루프 지원
- **Lock-Free 동시성** - Stage 단위 메시지 큐로 락 없는 안전한 처리
- **Protocol Buffers** - 효율적인 메시지 직렬화

---

## 아키텍처

```
┌───────────────────────────────────────────────────────────────┐
│                          클라이언트                            │
└──────────────────────────────┬────────────────────────────────┘
                               │
               ┌───────────────┴───────────────┐
               │ ① HTTP (로비/매칭)             │
               ▼                               │
┌──────────────────────────┐                   │
│       API Server         │                   │
│      (아웃게임 로직)       │                   │
│                          │                   │
│  - 로비/매칭 서비스        │                   │
│  - DB 조회/저장           │                   │
│  - 랭킹, 상점, 결제        │                   │
│                          │                   │
│  CreateStage() ─────────────────▶ Stage 생성  │
└──────────────────────────┘                   │
                                               │
┌──────────────────────────┐                   │
│       Play Server        │◀──────────────────┘
│       (인게임 로직)        │    ② TCP (게임 플레이)
│                          │
│  ┌────────────────────┐  │
│  │      Stage         │  │
│  │     (게임방)        │  │
│  │                    │  │
│  │  ┌──────┐ ┌──────┐ │  │
│  │  │Actor │ │Actor │ │  │
│  │  └──────┘ └──────┘ │  │
│  └────────────────────┘  │
└──────────────────────────┘
```

**핵심 흐름:**
1. 클라이언트가 API Server에 HTTP로 매칭/방 생성 요청
2. API Server가 Play Server에 Stage 생성
3. 클라이언트가 반환받은 정보로 Play Server에 TCP 접속
4. Stage 안에서 실시간 게임 진행

---

## 빠른 시작

### 설치

```bash
dotnet add package PlayHouse
```

### Play Server (게임 로직)

```csharp
// Stage 구현 - 게임방
public class GameRoom : IStage
{
    public IStageSender StageSender { get; private set; } = null!;

    public Task<(bool, IPacket)> OnCreate(IPacket packet)
    {
        Console.WriteLine("Game room created!");
        return Task.FromResult((true, CPacket.Empty));
    }

    public Task OnDispatch(IActor actor, IPacket packet)
    {
        // 클라이언트 메시지 처리
        var request = ChatRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        // 모든 플레이어에게 브로드캐스트
        BroadcastToAll(CPacket.Of(new ChatResponse {
            Sender = actor.ActorSender.AccountId,
            Message = request.Message
        }));

        return Task.CompletedTask;
    }

    // ... 기타 메서드
}

// Actor 구현 - 플레이어
public class Player : IActor
{
    public IActorSender ActorSender { get; private set; } = null!;

    public Task<(bool, IPacket?)> OnAuthenticate(IPacket authPacket)
    {
        var req = AuthRequest.Parser.ParseFrom(authPacket.Payload.DataSpan);
        ActorSender.SetAuthContext(req.UserId, 1001L);

        return Task.FromResult<(bool, IPacket?)>((true, CPacket.Of(new AuthResponse { Success = true })));
    }
}
```

### API Server (로비/매칭)

```csharp
public class LobbyController : IApiController
{
    public void Handles(IHandlerRegister register)
    {
        register.Add<CreateRoomRequest>(nameof(HandleCreateRoom));
    }

    private async Task HandleCreateRoom(IPacket packet, IApiSender sender)
    {
        var request = CreateRoomRequest.Parser.ParseFrom(packet.Payload.DataSpan);

        // Play Server에 Stage 생성
        var result = await sender.CreateStage(
            playNid: "play-server-1",
            stageType: "GameRoom",
            stageId: GenerateStageId(),
            packet: CPacket.Of(new CreateRoomPayload { RoomName = request.RoomName })
        );

        if (result.Result)
        {
            sender.Reply(CPacket.Of(new CreateRoomResponse {
                ServerAddress = "127.0.0.1",
                Port = 12000,
                StageId = stageId,
                StageType = "GameRoom"
            }));
        }
    }
}
```

### 서버 간 통신

```csharp
// Stage에서 API Server로 요청
var response = await StageSender.RequestToApiService(
    serviceId: "ranking-service",
    CPacket.Of(new GetRankRequest { PlayerId = playerId })
);

// API Server에서 Stage로 알림
ApiSender.SendToStage(playServerId, stageId, CPacket.Of(new Notification { ... }));

// Stage 간 통신
StageSender.SendToStage(targetServerId, targetStageId, CPacket.Of(message));
```

---

## 문서

| 카테고리 | 설명 | 링크 |
|---------|------|------|
| **시작하기** | 설치 및 첫 서버 구동 | [Quick Start](docs/getting-started/quick-start.md) |
| **핵심 개념** | Stage/Actor 모델, 메시징 | [Concepts](docs/concepts/) |
| **구현 가이드** | Stage, Actor, API Controller 구현 | [Guides](docs/guides/) |
| **튜토리얼** | 채팅방, 실시간 게임, 매칭 시스템 | [Tutorials](docs/tutorials/) |
| **내부 구현** | 아키텍처, 이벤트 루프, 패킷 구조 | [Internals](docs/internals/) |

### 추천 읽기 순서

1. [Overview](docs/concepts/overview.md) - 전체 구조 이해
2. [Stage/Actor](docs/concepts/stage-actor.md) - 핵심 모델
3. [Chat Room Tutorial](docs/tutorials/chat-room.md) - 실습

---

## 예제 프로젝트

| 예제 | 설명 | 문서 |
|-----|------|------|
| **채팅방** | 실시간 채팅 시스템 | [Tutorial](docs/tutorials/chat-room.md) |
| **실시간 게임** | 60fps GameLoop 기반 게임 | [Tutorial](docs/tutorials/realtime-game.md) |
| **로비 & 매칭** | 매칭 시스템 구현 | [Tutorial](docs/tutorials/lobby-matching.md) |

---

## 요구 사항

- .NET 8.0+
- Protocol Buffers

---

## 라이선스

MIT License - 자세한 내용은 [LICENSE](LICENSE) 파일을 참조하세요.

---

## 기여

버그 리포트, 기능 제안, PR을 환영합니다!

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request
