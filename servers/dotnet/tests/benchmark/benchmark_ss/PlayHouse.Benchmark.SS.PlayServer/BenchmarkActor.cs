using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;

namespace PlayHouse.Benchmark.SS.PlayServer;

/// <summary>
/// 벤치마크용 Actor 구현 (Server-to-Server 벤치마크)
/// </summary>
public class BenchmarkActor : IActor
{
    private static long _accountIdCounter;
    private readonly ILogger<BenchmarkActor> _logger;

    public BenchmarkActor(IActorLink actorLink, ILogger<BenchmarkActor>? logger = null)
    {
        ActorLink = actorLink;
        _logger = logger ?? NullLogger<BenchmarkActor>.Instance;
    }

    public IActorLink ActorLink { get; }

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
        // 간단한 인증 처리: 순차적으로 AccountId 할당
        var accountId = Interlocked.Increment(ref _accountIdCounter);
        ActorLink.AccountId = accountId.ToString();

        // 벤치마크에서는 reply packet 없이 간단히 true 반환
        return Task.FromResult<(bool, IPacket?)>((true, null));
    }

    public Task<long> OnCheckStage() => Task.FromResult(0L);

    public Task OnPostAuthenticate()
    {
        return Task.CompletedTask;
    }
}
