#nullable enable

using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Shared;
using PlayHouse.E2E.Shared.Proto;
using PlayHouse.Extensions.Proto;

namespace PlayHouse.E2E.Shared.Infrastructure;

/// <summary>
/// E2E 검증용 Actor 구현 (Client Response Only).
/// 상태 기록 없이 순수하게 인증 및 응답만 처리.
/// </summary>
/// <remarks>
/// Client Response Only 원칙:
/// - ❌ Static collections, instance tracking 금지
/// - ❌ OnCreateCalled, OnAuthenticateCalled 같은 상태 기록 금지
/// - ✅ OnAuthenticate에서 AccountId 생성 및 AuthenticateReply 반환만 수행
/// </remarks>
public class TestActorImpl : IActor
{
    private readonly ILogger<TestActorImpl> _logger;
    private static long _accountIdCounter;

    public IActorLink ActorLink { get; }

    public TestActorImpl(IActorLink actorLink, ILogger<TestActorImpl>? logger = null)
    {
        ActorLink = actorLink;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<TestActorImpl>.Instance;
        _logger.LogDebug("TestActorImpl created");
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

        if (TryParseMultiStageId(receivedToken, out var multiStageId))
        {
            var accountId = string.IsNullOrWhiteSpace(receivedUserId)
                ? Interlocked.Increment(ref _accountIdCounter).ToString()
                : receivedUserId;
            ActorLink.SetAuthContext(accountId, multiStageId.ToString());
        }
        else
        {
            var singleAccountId = string.IsNullOrWhiteSpace(receivedUserId)
                ? $"single-{Interlocked.Increment(ref _accountIdCounter)}"
                : receivedUserId;
            ActorLink.SetAuthSingleContext(singleAccountId, "SingleStage");
        }

        _logger.LogInformation("OnAuthenticate called for AccountId={AccountId}", ActorLink.AccountId);

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

    public Task OnPostAuthenticate()
    {
        return Task.CompletedTask;
    }

    private static bool TryParseMultiStageId(string token, out long stageId)
    {
        const string prefix = "multi-stage:";
        stageId = 0;

        if (!token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var raw = token[prefix.Length..];
        return long.TryParse(raw, out stageId) && stageId > 0;
    }
}
