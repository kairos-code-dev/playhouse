#nullable enable

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions.Play;

namespace PlayHouse.Core.Play;

/// <summary>
/// High-resolution game loop timer using a dedicated background thread.
/// Implements the "Fix Your Timestep" pattern for deterministic simulation.
/// </summary>
/// <remarks>
/// Key design decisions:
/// - Stopwatch.GetTimestamp(): Nanosecond precision (Windows QPC / Linux clock_gettime)
/// - Per-Stage dedicated thread: Avoids ThreadPool jitter
/// - Hybrid sleep: Thread.Sleep for most of interval + SpinWait for sub-millisecond precision
/// - Fixed timestep accumulator: Deterministic simulation ticks
/// </remarks>
internal sealed class GameLoopTimer : IDisposable
{
    private readonly string _stageId;
    private readonly GameLoopConfig _config;
    private readonly GameLoopCallback _callback;
    private readonly Action<string, GameLoopCallback, TimeSpan, TimeSpan> _dispatchCallback;
    private readonly ILogger _logger;

    private Thread? _thread;
    private volatile bool _running;
    private bool _disposed;

    /// <summary>
    /// Gets whether the game loop is currently running.
    /// </summary>
    public bool IsRunning => _running;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameLoopTimer"/> class.
    /// </summary>
    /// <param name="stageId">The Stage ID this timer belongs to.</param>
    /// <param name="config">Game loop configuration.</param>
    /// <param name="callback">The callback to invoke on each tick.</param>
    /// <param name="dispatchCallback">
    /// Callback to dispatch ticks to the Stage event loop.
    /// Parameters: stageId, gameLoopCallback, deltaTime, totalElapsed
    /// </param>
    /// <param name="logger">Logger instance.</param>
    public GameLoopTimer(
        string stageId,
        GameLoopConfig config,
        GameLoopCallback callback,
        Action<string, GameLoopCallback, TimeSpan, TimeSpan> dispatchCallback,
        ILogger logger)
    {
        _stageId = stageId;
        _config = config;
        _callback = callback;
        _dispatchCallback = dispatchCallback;
        _logger = logger;
    }

    /// <summary>
    /// Starts the game loop on a dedicated background thread.
    /// </summary>
    public void Start()
    {
        if (_running)
            throw new InvalidOperationException($"Game loop for Stage {_stageId} is already running.");

        _running = true;
        _thread = new Thread(RunLoop)
        {
            Name = $"GameLoop-Stage-{_stageId}",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        _thread.Start();
    }

    /// <summary>
    /// Stops the game loop and waits for the thread to terminate.
    /// </summary>
    public void Stop()
    {
        if (!_running) return;

        _running = false;

        // Skip Join if called from the game loop thread itself
        // (e.g., when OnGameLoopTick detects a destroyed Stage and calls StopGameLoop)
        if (_thread != null && Thread.CurrentThread != _thread)
        {
            _thread.Join(TimeSpan.FromSeconds(2));
        }
        _thread = null;
    }

    /// <summary>
    /// Core game loop running on a dedicated thread.
    /// Uses Stopwatch for high-resolution timing and a fixed timestep accumulator.
    /// </summary>
    private void RunLoop()
    {
        var fixedDtTicks = _config.FixedTimestep.Ticks;
        var maxCapTicks = _config.EffectiveMaxAccumulatorCap.Ticks;
        long accumulatorTicks = 0;
        long totalElapsedTicks = 0;
        var lastTimestamp = Stopwatch.GetTimestamp();

        _logger.LogInformation(
            "Game loop started for Stage {StageId} (fixedTimestep={FixedTimestepMs}ms, maxCap={MaxCapMs}ms)",
            _stageId, _config.FixedTimestep.TotalMilliseconds, _config.EffectiveMaxAccumulatorCap.TotalMilliseconds);

        try
        {
            while (_running)
            {
                var now = Stopwatch.GetTimestamp();
                var elapsedTicks = ((now - lastTimestamp) * TimeSpan.TicksPerSecond) / Stopwatch.Frequency;
                lastTimestamp = now;

                accumulatorTicks += elapsedTicks;

                // Spiral of Death prevention: cap the accumulator
                if (accumulatorTicks > maxCapTicks)
                {
                    accumulatorTicks = maxCapTicks;
                }

                // Dispatch fixed timestep ticks
                while (accumulatorTicks >= fixedDtTicks)
                {
                    totalElapsedTicks += fixedDtTicks;
                    var deltaTime = _config.FixedTimestep;
                    var totalElapsed = TimeSpan.FromTicks(totalElapsedTicks);

                    _dispatchCallback(_stageId, _callback, deltaTime, totalElapsed);
                    accumulatorTicks -= fixedDtTicks;
                }

                // Hybrid sleep: Sleep for most of remaining time, SpinWait for precision
                var remainingTicks = fixedDtTicks - accumulatorTicks;
                var remainingMs = (int)(remainingTicks / TimeSpan.TicksPerMillisecond) - 2;

                if (remainingMs > 1)
                {
                    Thread.Sleep(remainingMs);
                }
                else
                {
                    Thread.SpinWait(100);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Game loop error for Stage {StageId}", _stageId);
        }
        finally
        {
            _logger.LogInformation("Game loop stopped for Stage {StageId}", _stageId);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
