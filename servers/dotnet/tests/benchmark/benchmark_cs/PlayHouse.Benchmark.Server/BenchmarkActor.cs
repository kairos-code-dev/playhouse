using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Benchmark.Shared.Proto;
using PlayHouse.Core.Shared;

namespace PlayHouse.Benchmark.Server;

/// <summary>
/// 벤치마크용 Actor 구현
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
        var accountId = Interlocked.Increment(ref _accountIdCounter);
        ActorLink.SetAuthContext(accountId.ToString(), accountId);

        // 벤치마크에서는 reply packet 없이 간단히 true 반환
        return Task.FromResult<(bool, IPacket?)>((true, null));
    }

    public Task OnPostAuthenticate()
    {
        return Task.CompletedTask;
    }
}
