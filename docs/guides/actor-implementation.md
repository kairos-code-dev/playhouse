# Actor 구현 가이드

> 작성일: 2026-01-29
> 버전: 1.0
> 목적: PlayHouse Actor 구현 방법 및 인증 가이드

## 개요

Actor는 PlayHouse에서 개별 클라이언트(플레이어)를 나타냅니다. 각 Actor는 하나의 클라이언트 연결과 매핑되며, Stage에 입장하여 게임 로직에 참여합니다.

### Actor의 역할

- **클라이언트 인증**: 연결된 클라이언트의 신원 확인
- **사용자 식별**: AccountId를 통한 고유 사용자 식별
- **클라이언트 통신**: 개별 클라이언트와의 메시지 송수신
- **Stage 참여**: Stage에 입장하여 게임에 참여

## IActor 인터페이스

Actor를 구현하려면 `IActor` 인터페이스를 구현해야 합니다.

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

    // 생명주기 메서드들...
}
```

### IActorLink

`IActorLink`는 Actor에서 사용할 수 있는 통신 기능을 제공합니다:

- **AccountId**: Actor의 고유 계정 식별자 (필수 설정!)
- **LeaveStageAsync()**: 현재 Stage에서 나가기
- **SendToClient()**: 클라이언트에 메시지 전송
- **Reply()**: 요청에 대한 응답 전송

## Actor 생명주기

Actor는 클라이언트가 Stage에 입장할 때 생성되며, 다음과 같은 생명주기를 가집니다:

```
1. OnCreate()           → Actor 생성
2. OnAuthenticate()     → 클라이언트 인증 (AccountId 설정 필수!)
3. OnPostAuthenticate() → 인증 후 처리 (사용자 데이터 로드 등)
4. (Stage에 추가됨)
5. OnDestroy()          → Actor 종료
```

### 1. OnCreate - Actor 생성

Actor가 생성될 때 최초로 호출되며, Actor의 초기 상태를 설정합니다.

```csharp
public class GameActor : IActor
{
    private int _level = 0;
    private int _gold = 0;

    public IActorLink ActorLink { get; }

    public GameActor(IActorLink actorLink)
    {
        ActorLink = actorLink;
    }

    public Task OnCreate()
    {
        // Actor 초기 상태 설정
        _level = 1;
        _gold = 0;

        return Task.CompletedTask;
    }

    // 나머지 메서드들...
}
```

### 2. OnAuthenticate - 클라이언트 인증 (중요!)

**가장 중요한 메서드입니다!** 클라이언트의 신원을 확인하고 `AccountId`와 `StageId`를 함께 설정해야 합니다.

```csharp
public async Task<(bool result, IPacket? reply)> OnAuthenticate(IPacket authPacket)
{
    try
    {
        // 인증 요청 파싱
        var authRequest = AuthRequest.Parser.ParseFrom(authPacket.Payload.DataSpan);

        // 토큰 검증 (외부 인증 서버 또는 로컬 검증)
        bool isValid = await ValidateToken(authRequest.Token);

        if (!isValid)
        {
            // 인증 실패
            var errorReply = new AuthReply
            {
                Success = false,
                Error = "Invalid token"
            };
            return (false, CPacket.Of(errorReply));
        }

        // ⚠️ 필수: AccountId + StageId 설정
        // 둘 중 하나라도 누락되면 연결이 종료됩니다.
        ActorLink.SetAuthContext(authRequest.UserId, 1001L);

        // 인증 성공 응답
        var reply = new AuthReply
        {
            Success = true,
            AccountId = ActorLink.AccountId
        };

        return (true, CPacket.Of(reply));
    }
    catch (Exception ex)
    {
        // 인증 실패
        var errorReply = new AuthReply
        {
            Success = false,
            Error = ex.Message
        };
        return (false, CPacket.Of(errorReply));
    }
}

private async Task<bool> ValidateToken(string token)
{
    // 토큰 검증 로직
    // 예: JWT 검증, 외부 API 호출 등
    return await Task.FromResult(!string.IsNullOrEmpty(token));
}
```

**중요 사항:**
- **`ActorLink.AccountId`를 반드시 설정해야 합니다!**
- **`ActorLink.StageId`도 반드시 양수로 설정해야 합니다!**
- AccountId/StageId가 유효하지 않으면 프레임워크가 연결을 종료합니다
- `OnAuthenticate`가 `(false, reply)`를 반환하면 인증 실패로 간주됩니다
- `(true, reply)`를 반환하면 `OnPostAuthenticate`가 호출됩니다

### 3. OnPostAuthenticate - 인증 후 처리

인증이 성공한 후 호출되며, 사용자 데이터 로드 등의 추가 작업을 수행합니다.

```csharp
public async Task OnPostAuthenticate()
{
    // 외부 API에서 사용자 데이터 로드
    var userData = await LoadUserDataFromApi(ActorLink.AccountId);

    if (userData != null)
    {
        _level = userData.Level;
        _gold = userData.Gold;
    }
}

private async Task<UserData?> LoadUserDataFromApi(string accountId)
{
    // API 서버에서 사용자 데이터 조회
    // 예: HTTP 요청, 데이터베이스 쿼리 등
    return await Task.FromResult(new UserData
    {
        Level = 10,
        Gold = 1000
    });
}
```

**사용 예시:**
- 외부 API에서 사용자 프로필 로드
- 데이터베이스에서 인벤토리 조회
- 친구 목록 초기화
- 클라이언트에 초기 상태 전송

## Actor 종료 (OnDestroy)

Actor가 Stage를 떠날 때 호출됩니다. 리소스 정리, 상태 저장 등을 수행합니다.

```csharp
public async Task OnDestroy()
{
    // 사용자 데이터 저장
    await SaveUserDataToApi(ActorLink.AccountId, _level, _gold);

    // 리소스 정리
    _level = 0;
    _gold = 0;
}

private async Task SaveUserDataToApi(string accountId, int level, int gold)
{
    // API 서버에 사용자 데이터 저장
    // 예: HTTP 요청, 데이터베이스 업데이트 등
    await Task.CompletedTask;
}
```

**OnDestroy가 호출되는 경우:**
- `ActorLink.LeaveStageAsync()` 호출 시
- Stage가 종료될 때
- 인증이 실패했을 때
- 클라이언트 연결이 끊어졌을 때

## 인증 시나리오 예제

### 1. JWT 토큰 인증

```csharp
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;

public async Task<(bool result, IPacket? reply)> OnAuthenticate(IPacket authPacket)
{
    var authRequest = AuthRequest.Parser.ParseFrom(authPacket.Payload.DataSpan);

    try
    {
        // JWT 토큰 검증
        var tokenHandler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = "your-issuer",
            ValidAudience = "your-audience",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("your-secret-key"))
        };

        var principal = tokenHandler.ValidateToken(
            authRequest.Token,
            validationParameters,
            out SecurityToken validatedToken);

        // 토큰에서 사용자 ID 추출
        var userId = principal.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return (false, CPacket.Of(new AuthReply
            {
                Success = false,
                Error = "Invalid user ID in token"
            }));
        }

        // AccountId + StageId 설정 (필수!)
        ActorLink.SetAuthContext(userId, 1001L);

        return (true, CPacket.Of(new AuthReply
        {
            Success = true,
            AccountId = userId
        }));
    }
    catch (Exception ex)
    {
        return (false, CPacket.Of(new AuthReply
        {
            Success = false,
            Error = $"Token validation failed: {ex.Message}"
        }));
    }
}
```

### 2. 외부 인증 서버 호출

```csharp
public async Task<(bool result, IPacket? reply)> OnAuthenticate(IPacket authPacket)
{
    var authRequest = AuthRequest.Parser.ParseFrom(authPacket.Payload.DataSpan);

    // 외부 인증 서버에 토큰 검증 요청
    using var httpClient = new HttpClient();
    var response = await httpClient.PostAsJsonAsync(
        "https://auth-server.com/validate",
        new { token = authRequest.Token });

    if (!response.IsSuccessStatusCode)
    {
        return (false, CPacket.Of(new AuthReply
        {
            Success = false,
            Error = "Authentication server unavailable"
        }));
    }

    var authResult = await response.Content.ReadFromJsonAsync<AuthServerResponse>();

    if (authResult == null || !authResult.Valid)
    {
        return (false, CPacket.Of(new AuthReply
        {
            Success = false,
            Error = "Invalid credentials"
        }));
    }

    // AccountId + StageId 설정 (필수!)
    ActorLink.SetAuthContext(authResult.UserId, 1001L);

    return (true, CPacket.Of(new AuthReply
    {
        Success = true,
        AccountId = authResult.UserId,
        Nickname = authResult.Nickname
    }));
}
```

### 3. 간단한 토큰 검증 (개발/테스트용)

```csharp
public Task<(bool result, IPacket? reply)> OnAuthenticate(IPacket authPacket)
{
    var authRequest = AuthRequest.Parser.ParseFrom(authPacket.Payload.DataSpan);

    // 간단한 형식 검증 (개발/테스트 환경용)
    if (string.IsNullOrEmpty(authRequest.UserId) ||
        string.IsNullOrEmpty(authRequest.Token))
    {
        return Task.FromResult<(bool, IPacket?)>((false, CPacket.Of(new AuthReply
        {
            Success = false,
            Error = "Missing credentials"
        })));
    }

    // AccountId + StageId 설정 (필수!)
    ActorLink.SetAuthContext(authRequest.UserId, 1001L);

    return Task.FromResult<(bool, IPacket?)>((true, CPacket.Of(new AuthReply
    {
        Success = true,
        AccountId = authRequest.UserId
    })));
}
```

## 전체 예제

```csharp
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Shared;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

public class GameActor : IActor
{
    private readonly ILogger<GameActor> _logger;
    private int _level = 1;
    private int _gold = 0;
    private string _nickname = "";

    public IActorLink ActorLink { get; }

    public GameActor(IActorLink actorLink, ILogger<GameActor> logger)
    {
        ActorLink = actorLink;
        _logger = logger;
    }

    public Task OnCreate()
    {
        _logger.LogInformation("Actor created");
        _level = 1;
        _gold = 0;
        return Task.CompletedTask;
    }

    public async Task<(bool result, IPacket? reply)> OnAuthenticate(IPacket authPacket)
    {
        try
        {
            var authRequest = AuthRequest.Parser.ParseFrom(authPacket.Payload.DataSpan);

            _logger.LogInformation("Authentication requested for user: {UserId}", authRequest.UserId);

            // 토큰 검증
            bool isValid = await ValidateToken(authRequest.Token);

            if (!isValid)
            {
                _logger.LogWarning("Authentication failed for user: {UserId}", authRequest.UserId);

                return (false, CPacket.Of(new AuthReply
                {
                    Success = false,
                    Error = "Invalid token"
                }));
            }

            // ⚠️ 필수: AccountId + StageId 설정
            ActorLink.SetAuthContext(authRequest.UserId, 1001L);

            _logger.LogInformation("Authentication succeeded for AccountId: {AccountId}", ActorLink.AccountId);

            var reply = new AuthReply
            {
                Success = true,
                AccountId = ActorLink.AccountId
            };

            return (true, CPacket.Of(reply));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication error");

            return (false, CPacket.Of(new AuthReply
            {
                Success = false,
                Error = "Authentication error"
            }));
        }
    }

    public async Task OnPostAuthenticate()
    {
        _logger.LogInformation("Loading user data for AccountId: {AccountId}", ActorLink.AccountId);

        // 외부 API에서 사용자 데이터 로드
        var userData = await LoadUserDataFromApi(ActorLink.AccountId);

        if (userData != null)
        {
            _level = userData.Level;
            _gold = userData.Gold;
            _nickname = userData.Nickname;

            _logger.LogInformation(
                "User data loaded - Level: {Level}, Gold: {Gold}, Nickname: {Nickname}",
                _level, _gold, _nickname);

            // 클라이언트에 초기 상태 전송
            var notify = new UserDataNotify
            {
                Level = _level,
                Gold = _gold,
                Nickname = _nickname
            };
            ActorLink.SendToClient(CPacket.Of(notify));
        }
    }

    public async Task OnDestroy()
    {
        _logger.LogInformation("Actor destroyed for AccountId: {AccountId}", ActorLink.AccountId);

        // 사용자 데이터 저장
        if (!string.IsNullOrEmpty(ActorLink.AccountId))
        {
            await SaveUserDataToApi(ActorLink.AccountId, _level, _gold);
        }

        // 리소스 정리
        _level = 0;
        _gold = 0;
        _nickname = "";
    }

    private async Task<bool> ValidateToken(string token)
    {
        // 실제 토큰 검증 로직
        // JWT 검증, 외부 인증 서버 호출 등
        return await Task.FromResult(!string.IsNullOrEmpty(token));
    }

    private async Task<UserData?> LoadUserDataFromApi(string accountId)
    {
        // API 서버에서 사용자 데이터 조회
        return await Task.FromResult(new UserData
        {
            Level = 10,
            Gold = 1000,
            Nickname = $"Player_{accountId}"
        });
    }

    private async Task SaveUserDataToApi(string accountId, int level, int gold)
    {
        // API 서버에 사용자 데이터 저장
        await Task.CompletedTask;
    }

    private class UserData
    {
        public int Level { get; set; }
        public int Gold { get; set; }
        public string Nickname { get; set; } = "";
    }
}
```

## 주의사항 및 팁

### 1. AccountId 설정은 필수!

```csharp
// ❌ 잘못된 예 - AccountId를 설정하지 않음
public Task<(bool result, IPacket? reply)> OnAuthenticate(IPacket authPacket)
{
    // AccountId 설정 없이 true 반환 → 연결 종료됨!
    return Task.FromResult<(bool, IPacket?)>((true, null));
}

// ✅ 올바른 예 - AccountId 설정
public Task<(bool result, IPacket? reply)> OnAuthenticate(IPacket authPacket)
{
    var request = AuthRequest.Parser.ParseFrom(authPacket.Payload.DataSpan);

    // 필수: AccountId 설정!
    ActorLink.AccountId = request.UserId;

    return Task.FromResult<(bool, IPacket?)>((true, CPacket.Of(new AuthReply
    {
        Success = true
    })));
}
```

### 2. OnPostAuthenticate에서 시간이 오래 걸리는 작업

```csharp
public async Task OnPostAuthenticate()
{
    // 시간이 오래 걸리는 작업은 AsyncIO 사용 고려
    // (현재 OnPostAuthenticate는 Actor 전용 메서드이므로 직접 async/await 사용)

    // 여러 작업을 병렬로 수행
    var loadUserTask = LoadUserDataFromApi(ActorLink.AccountId);
    var loadInventoryTask = LoadInventoryFromApi(ActorLink.AccountId);
    var loadFriendsTask = LoadFriendsFromApi(ActorLink.AccountId);

    await Task.WhenAll(loadUserTask, loadInventoryTask, loadFriendsTask);

    // 결과 처리
    var userData = await loadUserTask;
    var inventory = await loadInventoryTask;
    var friends = await loadFriendsTask;
}
```

### 3. 인증 실패 시 로깅

```csharp
public async Task<(bool result, IPacket? reply)> OnAuthenticate(IPacket authPacket)
{
    var request = AuthRequest.Parser.ParseFrom(authPacket.Payload.DataSpan);

    // 토큰 검증
    if (!await ValidateToken(request.Token))
    {
        // 실패 원인 로깅
        _logger.LogWarning(
            "Authentication failed - UserId: {UserId}, Token: {Token}",
            request.UserId,
            request.Token.Substring(0, Math.Min(10, request.Token.Length)) + "...");

        return (false, CPacket.Of(new AuthReply
        {
            Success = false,
            Error = "Invalid token"
        }));
    }

    ActorLink.AccountId = request.UserId;
    return (true, CPacket.Of(new AuthReply { Success = true }));
}
```

## 다음 단계

- [Stage 구현 가이드](04-stage-implementation.md) - Stage 구현 방법
- [타이머 및 게임루프](06-timer-gameloop.md) - 타이머와 게임루프 사용법
