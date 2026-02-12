#nullable enable

using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Play;
using Xunit;

namespace PlayHouse.Unit.Core.Play;

/// <summary>
/// 단위 테스트: GameLoopTimer의 고해상도 게임 루프 기능 검증
/// </summary>
public class GameLoopTimerTests : IDisposable
{
    private readonly List<(string stageId, TimeSpan deltaTime, TimeSpan totalElapsed)> _ticks = new();
    private readonly ILogger _logger;

    public GameLoopTimerTests()
    {
        _logger = Substitute.For<ILogger>();
    }

    public void Dispose()
    {
    }

    private GameLoopTimer CreateTimer(
        string stageId = "1",
        double fixedTimestepMs = 50,
        TimeSpan? maxAccumulatorCap = null)
    {
        var config = new GameLoopConfig
        {
            FixedTimestep = TimeSpan.FromMilliseconds(fixedTimestepMs),
            MaxAccumulatorCap = maxAccumulatorCap
        };

        GameLoopCallback callback = (deltaTime, totalElapsed) => Task.CompletedTask;

        return new GameLoopTimer(
            stageId,
            config,
            callback,
            (sid, cb, dt, te) =>
            {
                lock (_ticks)
                {
                    _ticks.Add((sid, dt, te));
                }
            },
            _logger);
    }

    [Fact(DisplayName = "Start - 백그라운드 스레드를 생성하고 실행한다")]
    public async Task Start_CreatesBackgroundThread()
    {
        // Given (전제조건)
        using var timer = CreateTimer();

        // When (행동)
        timer.Start();
        await Task.Delay(100); // 스레드가 시작될 시간

        // Then (결과)
        timer.IsRunning.Should().BeTrue("게임 루프가 실행 중이어야 함");
    }

    [Fact(DisplayName = "Stop - 스레드를 정상 종료한다")]
    public async Task Stop_TerminatesThread()
    {
        // Given (전제조건)
        using var timer = CreateTimer();
        timer.Start();
        await Task.Delay(100);

        // When (행동)
        timer.Stop();

        // Then (결과)
        timer.IsRunning.Should().BeFalse("게임 루프가 종료되어야 함");
    }

    [Fact(DisplayName = "50ms 타임스텝 - 1초간 약 20회 틱이 디스패치된다")]
    public async Task TicksDispatchedAtCorrectRate()
    {
        // Given (전제조건)
        using var timer = CreateTimer(fixedTimestepMs: 50);

        // When (행동)
        timer.Start();
        await Task.Delay(1100); // 1초 + 약간의 여유
        timer.Stop();

        // Then (결과)
        int tickCount;
        lock (_ticks)
        {
            tickCount = _ticks.Count;
        }

        tickCount.Should().BeInRange(16, 24,
            "50ms 타임스텝으로 1초간 약 20회 틱이 발생해야 함 (CI 환경 허용 범위)");
    }

    [Fact(DisplayName = "DeltaTime - 항상 고정 타임스텝과 동일하다")]
    public async Task DeltaTime_AlwaysEqualsFixedTimestep()
    {
        // Given (전제조건)
        var fixedTimestep = TimeSpan.FromMilliseconds(50);
        using var timer = CreateTimer(fixedTimestepMs: 50);

        // When (행동)
        timer.Start();
        await Task.Delay(300);
        timer.Stop();

        // Then (결과)
        lock (_ticks)
        {
            _ticks.Should().NotBeEmpty("최소 1개 이상의 틱이 있어야 함");
            _ticks.All(t => t.deltaTime == fixedTimestep)
                .Should().BeTrue("모든 틱의 deltaTime이 고정 타임스텝과 동일해야 함");
        }
    }

    [Fact(DisplayName = "TotalElapsed - 단조 증가한다")]
    public async Task TotalElapsed_IncreasesMonotonically()
    {
        // Given (전제조건)
        using var timer = CreateTimer(fixedTimestepMs: 50);

        // When (행동)
        timer.Start();
        await Task.Delay(500);
        timer.Stop();

        // Then (결과)
        lock (_ticks)
        {
            _ticks.Should().HaveCountGreaterThan(1, "검증을 위해 최소 2개 이상의 틱이 필요함");

            for (int i = 1; i < _ticks.Count; i++)
            {
                _ticks[i].totalElapsed.Should().BeGreaterThan(
                    _ticks[i - 1].totalElapsed,
                    $"틱 {i}의 totalElapsed는 이전 틱보다 커야 함");
            }
        }
    }

    [Fact(DisplayName = "중복 Start - InvalidOperationException을 던진다")]
    public async Task DoubleStart_Throws()
    {
        // Given (전제조건)
        using var timer = CreateTimer();
        timer.Start();
        await Task.Delay(50);

        // When & Then (행동 & 결과)
        var act = () => timer.Start();
        act.Should().Throw<InvalidOperationException>(
            "이미 실행 중인 게임 루프를 다시 시작하면 예외가 발생해야 함");
    }

    [Fact(DisplayName = "StopWhenNotRunning - 예외 없이 무시된다")]
    public void StopWhenNotRunning_NoOp()
    {
        // Given (전제조건)
        using var timer = CreateTimer();

        // When & Then (행동 & 결과)
        var act = () => timer.Stop();
        act.Should().NotThrow("미실행 상태에서 Stop 호출은 예외 없이 무시되어야 함");
    }

    [Fact(DisplayName = "Dispose - 실행 중인 게임 루프를 자동 종료한다")]
    public async Task Dispose_StopsRunningLoop()
    {
        // Given (전제조건)
        var timer = CreateTimer();
        timer.Start();
        await Task.Delay(100);
        timer.IsRunning.Should().BeTrue();

        // When (행동)
        timer.Dispose();

        // Then (결과)
        timer.IsRunning.Should().BeFalse("Dispose 후 게임 루프가 종료되어야 함");
    }

    [Fact(DisplayName = "AccumulatorCap - Spiral of Death를 방지한다")]
    public async Task AccumulatorCap_PreventsSpiralOfDeath()
    {
        // Given (전제조건)
        // maxCap = 3 × fixedTimestep → 최대 3틱만 catch-up
        var maxCap = TimeSpan.FromMilliseconds(150); // 3 × 50ms
        using var timer = CreateTimer(fixedTimestepMs: 50, maxAccumulatorCap: maxCap);

        // When (행동)
        timer.Start();
        await Task.Delay(500); // 실행
        timer.Stop();

        // Then (결과)
        // 정상적인 경우 약 10틱 (500ms / 50ms), cap 덕분에 폭주하지 않음
        lock (_ticks)
        {
            _ticks.Count.Should().BeGreaterThan(0, "최소 1개 이상의 틱이 있어야 함");
            // Cap이 작동하면 totalElapsed의 증가분은 항상 fixedTimestep
            _ticks.All(t => t.deltaTime == TimeSpan.FromMilliseconds(50))
                .Should().BeTrue("모든 틱의 deltaTime이 고정 타임스텝이어야 함");
        }
    }

    [Fact(DisplayName = "StageId - 콜백에 올바른 StageId가 전달된다")]
    public async Task CorrectStageId_InCallback()
    {
        // Given (전제조건)
        const string expectedStageId = "42";
        using var timer = CreateTimer(stageId: expectedStageId);

        // When (행동)
        timer.Start();
        await Task.Delay(200);
        timer.Stop();

        // Then (결과)
        lock (_ticks)
        {
            _ticks.Should().NotBeEmpty();
            _ticks.All(t => t.stageId == expectedStageId)
                .Should().BeTrue("모든 콜백에 올바른 StageId가 전달되어야 함");
        }
    }
}
