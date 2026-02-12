#nullable enable

using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Play;
using PlayHouse.Runtime.Proto;
using Xunit;

// Alias to avoid conflict with System.Threading.TimerCallback
using TimerCallbackDelegate = PlayHouse.Abstractions.Play.TimerCallback;

namespace PlayHouse.Unit.Core.Play;

/// <summary>
/// 단위 테스트: TimerManager의 타이머 관리 기능 검증
/// </summary>
public class TimerManagerTests : IDisposable
{
    private readonly List<(string stageId, long timerId, TimerCallbackDelegate callback)> _dispatchedTimers = new();
    private readonly TimerManager _timerManager;

    public TimerManagerTests()
    {
        var logger = Substitute.For<ILogger>();
        _timerManager = new TimerManager((stageId, timerId, callback) =>
        {
            lock (_dispatchedTimers)
            {
                _dispatchedTimers.Add((stageId, timerId, callback));
            }
        }, logger);
    }

    public void Dispose()
    {
        _timerManager.Dispose();
    }

    [Fact(DisplayName = "ActiveTimerCount - 초기값은 0이다")]
    public void ActiveTimerCount_Initially_IsZero()
    {
        // Given (전제조건)
        // When (행동)
        var count = _timerManager.ActiveTimerCount;

        // Then (결과)
        count.Should().Be(0, "초기 활성 타이머 수는 0이어야 함");
    }

    [Fact(DisplayName = "ProcessTimer(Repeat) - 반복 타이머를 추가한다")]
    public void ProcessTimer_RepeatType_AddsTimer()
    {
        // Given (전제조건)
        var timerPacket = CreateTimerPacket(
            stageId: 1,
            timerId: 100,
            type: TimerMsg.Types.Type.Repeat,
            initialDelayMs: 100,
            periodMs: 100,
            count: 0);

        // When (행동)
        _timerManager.ProcessTimer(timerPacket);

        // Then (결과)
        _timerManager.ActiveTimerCount.Should().Be(1, "타이머가 추가되어야 함");
    }

    [Fact(DisplayName = "ProcessTimer(Count) - 횟수 제한 타이머를 추가한다")]
    public void ProcessTimer_CountType_AddsTimer()
    {
        // Given (전제조건)
        var timerPacket = CreateTimerPacket(
            stageId: 1,
            timerId: 100,
            type: TimerMsg.Types.Type.Count,
            initialDelayMs: 100,
            periodMs: 100,
            count: 3);

        // When (행동)
        _timerManager.ProcessTimer(timerPacket);

        // Then (결과)
        _timerManager.ActiveTimerCount.Should().Be(1, "타이머가 추가되어야 함");
    }

    [Fact(DisplayName = "ProcessTimer(Cancel) - 타이머를 취소한다")]
    public void ProcessTimer_CancelType_RemovesTimer()
    {
        // Given (전제조건)
        const long timerId = 100;
        var addPacket = CreateTimerPacket(
            stageId: 1,
            timerId: timerId,
            type: TimerMsg.Types.Type.Repeat,
            initialDelayMs: 1000,
            periodMs: 1000,
            count: 0);

        var cancelPacket = CreateTimerPacket(
            stageId: 1,
            timerId: timerId,
            type: TimerMsg.Types.Type.Cancel,
            initialDelayMs: 0,
            periodMs: 0,
            count: 0);

        _timerManager.ProcessTimer(addPacket);

        // When (행동)
        _timerManager.ProcessTimer(cancelPacket);

        // Then (결과)
        _timerManager.ActiveTimerCount.Should().Be(0, "타이머가 취소되어야 함");
    }

    [Fact(DisplayName = "반복 타이머 - 콜백이 주기적으로 디스패치된다")]
    public async Task RepeatTimer_DispatchesCallbacksPeriodically()
    {
        // Given (전제조건)
        const long stageId = 1;
        const long timerId = 100;
        var timerPacket = CreateTimerPacket(
            stageId: stageId,
            timerId: timerId,
            type: TimerMsg.Types.Type.Repeat,
            initialDelayMs: 50,
            periodMs: 50,
            count: 0);

        // When (행동)
        _timerManager.ProcessTimer(timerPacket);
        await Task.Delay(200); // 충분히 대기

        // Then (결과)
        lock (_dispatchedTimers)
        {
            _dispatchedTimers.Count.Should().BeGreaterOrEqualTo(2, "최소 2번 이상 콜백이 디스패치되어야 함");
            _dispatchedTimers.All(t => t.stageId == stageId.ToString() && t.timerId == timerId)
                .Should().BeTrue("모든 콜백이 올바른 StageId와 TimerId를 가져야 함");
        }
    }

    [Fact(DisplayName = "횟수 제한 타이머 - 지정된 횟수만큼 실행 후 자동 취소된다")]
    public async Task CountTimer_AutoCancelsAfterCount()
    {
        // Given (전제조건)
        const long stageId = 1;
        const long timerId = 100;
        const int count = 3;
        var timerPacket = CreateTimerPacket(
            stageId: stageId,
            timerId: timerId,
            type: TimerMsg.Types.Type.Count,
            initialDelayMs: 20,
            periodMs: 20,
            count: count);

        // When (행동)
        _timerManager.ProcessTimer(timerPacket);
        await Task.Delay(200); // 충분히 대기 (3회 실행에 충분한 시간)

        // Then (결과)
        lock (_dispatchedTimers)
        {
            _dispatchedTimers.Count.Should().Be(count, $"정확히 {count}번 실행되어야 함");
        }
        _timerManager.ActiveTimerCount.Should().Be(0, "횟수 완료 후 자동 취소되어야 함");
    }

    [Fact(DisplayName = "CancelAllForStage - 특정 Stage의 모든 타이머를 취소한다")]
    public void CancelAllForStage_RemovesAllTimersForStage()
    {
        // Given (전제조건)
        const long targetStageId = 1;
        const long otherStageId = 2;

        var packet1 = CreateTimerPacket(targetStageId, 100, TimerMsg.Types.Type.Repeat, 1000, 1000, 0);
        var packet2 = CreateTimerPacket(targetStageId, 101, TimerMsg.Types.Type.Repeat, 1000, 1000, 0);
        var packet3 = CreateTimerPacket(otherStageId, 200, TimerMsg.Types.Type.Repeat, 1000, 1000, 0);

        _timerManager.ProcessTimer(packet1);
        _timerManager.ProcessTimer(packet2);
        _timerManager.ProcessTimer(packet3);

        _timerManager.ActiveTimerCount.Should().Be(3, "3개의 타이머가 추가되어야 함");

        // When (행동)
        _timerManager.CancelAllForStage(targetStageId.ToString());

        // Then (결과)
        _timerManager.ActiveTimerCount.Should().Be(1, "대상 Stage의 타이머만 취소되어야 함");
    }

    [Fact(DisplayName = "중복 TimerId - 동일한 TimerId로 추가 시 기존 타이머가 유지된다")]
    public void ProcessTimer_DuplicateTimerId_KeepsExistingTimer()
    {
        // Given (전제조건)
        const long timerId = 100;
        var packet1 = CreateTimerPacket(1, timerId, TimerMsg.Types.Type.Repeat, 1000, 1000, 0);
        var packet2 = CreateTimerPacket(2, timerId, TimerMsg.Types.Type.Repeat, 1000, 1000, 0);

        _timerManager.ProcessTimer(packet1);

        // When (행동)
        _timerManager.ProcessTimer(packet2);

        // Then (결과)
        _timerManager.ActiveTimerCount.Should().Be(1, "중복 타이머는 추가되지 않아야 함");
    }

    [Fact(DisplayName = "Dispose - 모든 타이머가 정리된다")]
    public void Dispose_ClearsAllTimers()
    {
        // Given (전제조건)
        var logger = Substitute.For<ILogger>();
        var manager = new TimerManager((_, _, _) => { }, logger);
        var packet1 = CreateTimerPacket(1, 100, TimerMsg.Types.Type.Repeat, 1000, 1000, 0);
        var packet2 = CreateTimerPacket(1, 101, TimerMsg.Types.Type.Repeat, 1000, 1000, 0);

        manager.ProcessTimer(packet1);
        manager.ProcessTimer(packet2);

        // When (행동)
        manager.Dispose();

        // Then (결과)
        manager.ActiveTimerCount.Should().Be(0, "Dispose 후 모든 타이머가 정리되어야 함");
    }

    [Fact(DisplayName = "Dispose 후 ProcessTimer 호출 - 무시된다")]
    public void ProcessTimer_AfterDispose_IsIgnored()
    {
        // Given (전제조건)
        var logger = Substitute.For<ILogger>();
        var manager = new TimerManager((_, _, _) => { }, logger);
        manager.Dispose();

        var packet = CreateTimerPacket(1, 100, TimerMsg.Types.Type.Repeat, 1000, 1000, 0);

        // When (행동)
        manager.ProcessTimer(packet);

        // Then (결과)
        manager.ActiveTimerCount.Should().Be(0, "Dispose 후에는 타이머가 추가되지 않아야 함");
    }

    #region Helper Methods

    private static TimerPacket CreateTimerPacket(
        long stageId,
        long timerId,
        TimerMsg.Types.Type type,
        long initialDelayMs,
        long periodMs,
        int count)
    {
        return new TimerPacket(
            stageId.ToString(),
            timerId,
            type,
            initialDelayMs,
            periodMs,
            count,
            () => Task.CompletedTask);
    }

    #endregion
}
