#nullable enable

using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Shared;
using PlayHouse.Extensions.Proto;
using PlayHouse.TestServer.Proto;
using PlayHouse.TestServer.Shared;

namespace PlayHouse.TestServer.Play;

/// <summary>
/// Test Server용 Actor 구현.
/// 클라이언트 커넥터 인증 처리를 담당합니다.
/// </summary>
public class TestActor : IActor
{
    private readonly ILogger<TestActor> _logger;
    private static long _accountIdCounter;
    private string _userId = string.Empty;

    public IActorLink ActorLink { get; }

    public TestActor(IActorLink actorLink, ILogger<TestActor>? logger = null)
    {
        ActorLink = actorLink;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<TestActor>.Instance;
    }

    public Task OnCreate()
    {
        return Task.CompletedTask;
    }

    public Task OnDestroy()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 클라이언트 인증 요청 처리.
    /// CRITICAL: ActorLink.AccountId를 반드시 설정해야 합니다.
    /// </summary>
    public Task<(bool result, IPacket? reply)> OnAuthenticate(IPacket packet)
    {
        try
        {
            var authRequest = AuthenticateRequest.Parser.ParseFrom(packet.Payload.DataSpan);
            _userId = authRequest.UserId;

            // CRITICAL: AccountId 설정 (필수!)
            // 자동 증가 카운터 사용 또는 UserId 사용 가능
            var accountId = Interlocked.Increment(ref _accountIdCounter);
            ActorLink.AccountId = accountId.ToString();

            _logger.LogInformation(
                "Authenticating user: AccountId={AccountId}, UserId={UserId}, Token={Token}",
                ActorLink.AccountId,
                authRequest.UserId,
                authRequest.Token);

            // 간단한 토큰 검증 (테스트용)
            // 빈 UserId나 빈 토큰도 실패 처리
            var isValidUserId = !string.IsNullOrEmpty(authRequest.UserId);
            var isValidToken = !string.IsNullOrEmpty(authRequest.Token) && IsValidToken(authRequest.Token);

            if (!isValidUserId || !isValidToken)
            {
                _logger.LogWarning("Authentication failed: UserId={UserId}, Token={Token}, ValidUserId={ValidUserId}, ValidToken={ValidToken}",
                    authRequest.UserId, authRequest.Token, isValidUserId, isValidToken);

                // 실패 응답도 AuthenticateReply로 반환 (테스트에서 응답 내용 검증 필요)
                var failReply = new AuthenticateReply
                {
                    AccountId = "",
                    Success = false,
                    ReceivedUserId = authRequest.UserId,
                    ReceivedToken = authRequest.Token
                };
                return Task.FromResult<(bool, IPacket?)>((false, ProtoCPacketExtensions.OfProto(failReply)));
            }

            // 인증 성공 응답
            var authReply = new AuthenticateReply
            {
                AccountId = ActorLink.AccountId,
                Success = true,
                ReceivedUserId = authRequest.UserId,
                ReceivedToken = authRequest.Token
            };

            return Task.FromResult<(bool, IPacket?)>((true, ProtoCPacketExtensions.OfProto(authReply)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication failed");
            return Task.FromResult<(bool, IPacket?)>((false, null));
        }
    }

    public Task<long> OnCheckStage()
    {
        if (StageAssignmentStore.TryGet(_userId, out var stageId))
        {
            return Task.FromResult(stageId);
        }

        return Task.FromResult(0L);
    }

    public Task OnPostAuthenticate()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 간단한 테스트용 토큰 검증.
    /// </summary>
    private bool IsValidToken(string token)
    {
        // 테스트용: "invalid" 문자열이 포함되지 않으면 유효
        return !token.Contains("invalid", StringComparison.OrdinalIgnoreCase);
    }
}
