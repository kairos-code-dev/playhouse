#nullable enable

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions.Play;
using PlayHouse.Runtime.Proto;

// Alias to avoid conflict with System.Threading.TimerCallback
using TimerCallbackDelegate = PlayHouse.Abstractions.Play.TimerCallback;

namespace PlayHouse.Core.Play;

/// <summary>
/// Manages timers for all Stages in a Play server.
/// </summary>
/// <remarks>
/// TimerManager uses System.Threading.Timer for scheduling and dispatches
/// callbacks to the appropriate Stage's event loop for thread-safe execution.
/// </remarks>
internal sealed class TimerManager : IDisposable
{
    private readonly ConcurrentDictionary<long, TimerEntry> _timers = new();
    private readonly Action<string, long, TimerCallbackDelegate> _dispatchCallback;
    private readonly ILogger _logger;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TimerManager"/> class.
    /// </summary>
    /// <param name="dispatchCallback">
    /// Callback to dispatch timer events to Stage event loops.
    /// Parameters: stageId, timerId, callback
    /// </param>
    /// <param name="logger">Logger instance.</param>
    public TimerManager(Action<string, long, TimerCallbackDelegate> dispatchCallback, ILogger logger)
    {
        _dispatchCallback = dispatchCallback;
        _logger = logger;
    }

    /// <summary>
    /// Processes a timer packet (add/cancel).
    /// </summary>
    /// <param name="timerPacket">Timer packet.</param>
    public void ProcessTimer(TimerPacket timerPacket)
    {
        if (_disposed) return;

        switch (timerPacket.Type)
        {
            case TimerMsg.Types.Type.Repeat:
                AddRepeatTimer(timerPacket);
                break;
            case TimerMsg.Types.Type.Count:
                AddCountTimer(timerPacket);
                break;
            case TimerMsg.Types.Type.Cancel:
                CancelTimer(timerPacket.TimerId);
                break;
        }
    }

    private void AddRepeatTimer(TimerPacket timerPacket)
    {
        var entry = new TimerEntry(
            timerPacket.TimerId,
            timerPacket.StageId,
            TimerType.Repeat,
            timerPacket.Callback,
            -1 // Infinite
        );

        var timer = new System.Threading.Timer(
            _ => OnTimerTick(timerPacket.TimerId),
            null,
            TimeSpan.FromMilliseconds(timerPacket.InitialDelayMs),
            TimeSpan.FromMilliseconds(timerPacket.PeriodMs));

        entry.SetSystemTimer(timer);

        if (!_timers.TryAdd(timerPacket.TimerId, entry))
        {
            timer.Dispose();
            _logger.LogWarning("Timer {TimerId} already exists", timerPacket.TimerId);
        }
    }

    private void AddCountTimer(TimerPacket timerPacket)
    {
        var entry = new TimerEntry(
            timerPacket.TimerId,
            timerPacket.StageId,
            TimerType.Count,
            timerPacket.Callback,
            timerPacket.Count
        );

        var timer = new System.Threading.Timer(
            _ => OnTimerTick(timerPacket.TimerId),
            null,
            TimeSpan.FromMilliseconds(timerPacket.InitialDelayMs),
            TimeSpan.FromMilliseconds(timerPacket.PeriodMs));

        entry.SetSystemTimer(timer);

        if (!_timers.TryAdd(timerPacket.TimerId, entry))
        {
            timer.Dispose();
            _logger.LogWarning("Timer {TimerId} already exists", timerPacket.TimerId);
        }
    }

    private void CancelTimer(long timerId)
    {
        if (_timers.TryRemove(timerId, out var entry))
        {
            entry.Dispose();
        }
    }

    private void OnTimerTick(long timerId)
    {
        if (_disposed) return;

        if (!_timers.TryGetValue(timerId, out var entry))
        {
            return;
        }

        // Dispatch to Stage event loop
        _dispatchCallback(entry.StageId, timerId, entry.Callback);

        // Handle count timer expiration
        if (entry.Type == TimerType.Count)
        {
            var remaining = entry.DecrementCount();
            if (remaining <= 0)
            {
                CancelTimer(timerId);
            }
        }
    }

    /// <summary>
    /// Cancels all timers for a specific Stage.
    /// </summary>
    /// <param name="stageId">Stage ID.</param>
    public void CancelAllForStage(string stageId)
    {
        var timerIds = _timers
            .Where(kvp => kvp.Value.StageId == stageId)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var timerId in timerIds)
        {
            CancelTimer(timerId);
        }
    }

    /// <summary>
    /// Gets the count of active timers.
    /// </summary>
    public int ActiveTimerCount => _timers.Count;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var entry in _timers.Values)
        {
            entry.Dispose();
        }
        _timers.Clear();
    }
}

/// <summary>
/// Timer type enumeration.
/// </summary>
internal enum TimerType
{
    /// <summary>
    /// Infinite repeat timer.
    /// </summary>
    Repeat,

    /// <summary>
    /// Limited count timer.
    /// </summary>
    Count
}

/// <summary>
/// Internal timer entry tracking.
/// </summary>
internal sealed class TimerEntry : IDisposable
{
    public long TimerId { get; }
    public string StageId { get; }
    public TimerType Type { get; }
    public TimerCallbackDelegate Callback { get; }

    private System.Threading.Timer? _timer;
    private int _remainingCount;
    private bool _disposed;

    public TimerEntry(
        long timerId,
        string stageId,
        TimerType type,
        TimerCallbackDelegate callback,
        int count)
    {
        TimerId = timerId;
        StageId = stageId;
        Type = type;
        Callback = callback;
        _remainingCount = count;
    }

    public void SetSystemTimer(Timer timer)
    {
        _timer = timer;
    }

    public int DecrementCount()
    {
        return Interlocked.Decrement(ref _remainingCount);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Dispose();
    }
}
