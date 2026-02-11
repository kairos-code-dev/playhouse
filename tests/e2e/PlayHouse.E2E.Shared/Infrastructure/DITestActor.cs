#nullable enable

using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Shared;
using PlayHouse.E2E.Shared.Proto;
using PlayHouse.Extensions.Proto;

namespace PlayHouse.E2E.Shared.Infrastructure;

/// <summary>
/// DI 통합 검증용 Actor 구현 (Client Response Only).
/// IActorSender와 ITestService를 모두 DI로 주입받아 DI 통합을 검증합니다.
/// </summary>
/// <remarks>
/// Client Response Only 원칙:
/// - ❌ Static collections 금지
/// - ✅ DI로 주입받은 서비스를 사용하여 인증 처리
/// </remarks>
public class DITestActor : IActor
{
    private readonly ILogger<DITestActor> _logger;
    public IActorLink ActorLink { get; }
    private readonly ITestService _testService;

    private static long _accountIdCounter;

    /// <summary>
    /// DI 컨테이너가 IActorSender, ITestService, ILogger를 모두 주입합니다.
    /// </summary>
    public DITestActor(IActorLink actorLink, ITestService testService, ILogger<DITestActor>? logger = null)
    {
        ActorLink = actorLink;
        _testService = testService;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DITestActor>.Instance;
        _logger.LogDebug("DITestActor created");
    }

    public Task OnCreate()
    {
        return Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        return Task.CompletedTask;
    }

    public Task<(bool result, IPacket? reply)> OnAuthenticate(IPacket authPacket)
    {
        // 인증 시 AccountId 설정 (필수)
        var accountId = Interlocked.Increment(ref _accountIdCounter);
        ActorLink.AccountId = accountId.ToString();

        // authPacket 파싱하여 E2E 검증 가능하도록 echo
        string receivedUserId = "";
        string receivedToken = "";
        if (authPacket.Payload.Length > 0)
        {
            try
            {
                var request = AuthenticateRequest.Parser.ParseFrom(authPacket.Payload.DataSpan);
                receivedUserId = request.UserId;
                receivedToken = request.Token;
            }
            catch
            {
                // 빈 packet이거나 파싱 실패 시 무시
            }
        }

        // Reply packet 생성 (클라이언트에 전달됨)
        var reply = new AuthenticateReply
        {
            AccountId = ActorLink.AccountId,
            Success = true,
            ReceivedUserId = receivedUserId,
            ReceivedToken = receivedToken
        };

        return Task.FromResult<(bool, IPacket?)>((true, ProtoCPacketExtensions.OfProto(reply)));
    }

    public Task<long> OnCheckStage() => Task.FromResult(0L);

    public Task OnPostAuthenticate()
    {
        return Task.CompletedTask;
    }
}
