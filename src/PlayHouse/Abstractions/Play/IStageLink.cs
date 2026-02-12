#nullable enable

namespace PlayHouse.Abstractions.Play;

/// <summary>
/// Delegate for asynchronous pre-callback in AsyncCompute/AsyncIO.
/// Executed on a dedicated thread pool (outside the event loop).
/// </summary>
/// <returns>Result to be passed to the post-callback.</returns>
public delegate Task<object?> AsyncPreCallback();

/// <summary>
/// Delegate for asynchronous post-callback in AsyncCompute/AsyncIO.
/// Executed on the Stage event loop (safe to access Stage state).
/// </summary>
/// <param name="result">Result from the pre-callback.</param>
public delegate Task AsyncPostCallback(object? result);

/// <summary>
/// Delegate for timer callbacks.
/// </summary>
public delegate Task TimerCallback();

/// <summary>
/// Delegate for game loop tick callbacks.
/// </summary>
/// <param name="deltaTime">Fixed timestep value (always equals the configured fixedTimestep).</param>
/// <param name="totalElapsed">Total elapsed simulation time since the game loop started.</param>
public delegate Task GameLoopCallback(TimeSpan deltaTime, TimeSpan totalElapsed);

/// <summary>
/// Provides Stage-specific communication and management capabilities.
/// </summary>
/// <remarks>
/// IStageLink extends ILink with:
/// - Timer management (repeat, count, cancel)
/// - Stage lifecycle management (close)
/// - AsyncCompute/AsyncIO for safe external operations
/// - Client messaging with StageId context
/// </remarks>
public interface IStageLink : ILink
{
    /// <summary>
    /// Gets the unique identifier for this Stage.
    /// </summary>
    string StageId { get; }

    /// <summary>
    /// Gets the type identifier for this Stage.
    /// </summary>
    string StageType { get; }

    #region Timer Management

    /// <summary>
    /// Adds a repeating timer that fires indefinitely.
    /// </summary>
    /// <param name="initialDelay">Time before the first callback.</param>
    /// <param name="period">Interval between subsequent callbacks.</param>
    /// <param name="callback">The callback to execute on each timer tick.</param>
    /// <returns>A unique timer ID that can be used to cancel the timer.</returns>
    long AddRepeatTimer(TimeSpan initialDelay, TimeSpan period, TimerCallback callback);

    /// <summary>
    /// Adds a timer that fires a specified number of times.
    /// </summary>
    /// <param name="initialDelay">Time before the first callback.</param>
    /// <param name="period">Interval between subsequent callbacks.</param>
    /// <param name="count">Number of times to fire before auto-canceling.</param>
    /// <param name="callback">The callback to execute on each timer tick.</param>
    /// <returns>A unique timer ID that can be used to cancel the timer.</returns>
    long AddCountTimer(TimeSpan initialDelay, TimeSpan period, int count, TimerCallback callback);

    /// <summary>
    /// Cancels an active timer.
    /// </summary>
    /// <param name="timerId">The ID of the timer to cancel.</param>
    void CancelTimer(long timerId);

    /// <summary>
    /// Checks if a timer is still active.
    /// </summary>
    /// <param name="timerId">The ID of the timer to check.</param>
    /// <returns>True if the timer exists and is active; otherwise, false.</returns>
    bool HasTimer(long timerId);

    #endregion

    #region Stage Management

    /// <summary>
    /// Closes this Stage, canceling all timers and triggering cleanup.
    /// </summary>
    /// <remarks>
    /// This method:
    /// 1. Cancels all active timers
    /// 2. Sends a destroy message to the dispatcher
    /// 3. Triggers IStage.OnDestroy()
    /// </remarks>
    void CloseStage();

    #endregion

    #region Async Operations

    /// <summary>
    /// Executes a CPU-bound operation on a dedicated compute thread pool,
    /// then optionally processes the result back on the event loop.
    /// </summary>
    /// <param name="preCallback">
    /// Executed on ComputeTaskPool. Use for CPU-intensive calculations.
    /// The pool size is limited to CPU core count.
    /// </param>
    /// <param name="postCallback">
    /// Executed on the Stage event loop after preCallback completes.
    /// Safe to access Stage state here.
    /// </param>
    /// <remarks>
    /// ComputeTaskPool is optimized for CPU-bound work:
    /// - Limited concurrency (CPU core count)
    /// - Prevents CPU starvation
    /// </remarks>
    void AsyncCompute(AsyncPreCallback preCallback, AsyncPostCallback? postCallback = null);

    /// <summary>
    /// Executes an I/O-bound operation on a dedicated I/O thread pool,
    /// then optionally processes the result back on the event loop.
    /// </summary>
    /// <param name="preCallback">
    /// Executed on IoTaskPool. Use for database queries, HTTP calls, file I/O, etc.
    /// The pool allows higher concurrency for I/O wait times.
    /// </param>
    /// <param name="postCallback">
    /// Executed on the Stage event loop after preCallback completes.
    /// Safe to access Stage state here.
    /// </param>
    /// <remarks>
    /// IoTaskPool is optimized for I/O-bound work:
    /// - Higher concurrency (default 100)
    /// - Handles I/O wait efficiently
    /// </remarks>
    void AsyncIO(AsyncPreCallback preCallback, AsyncPostCallback? postCallback = null);

    #endregion

    #region Client Communication

    /// <summary>
    /// Sends a message to a specific client.
    /// </summary>
    /// <param name="sessionServerId">The session server ID.</param>
    /// <param name="sid">The session ID.</param>
    /// <param name="packet">The packet to send.</param>
    void SendToClient(string sessionServerId, long sid, IPacket packet);

    #endregion

    #region Game Loop

    /// <summary>
    /// Starts a high-resolution game loop with the specified fixed timestep.
    /// Only one game loop per Stage is allowed.
    /// </summary>
    /// <param name="fixedTimestep">Fixed timestep interval (valid range: 1ms ~ 1000ms).</param>
    /// <param name="callback">Callback invoked on each tick with deltaTime and totalElapsed.</param>
    /// <exception cref="InvalidOperationException">Thrown if a game loop is already running.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if fixedTimestep is out of valid range.</exception>
    void StartGameLoop(TimeSpan fixedTimestep, GameLoopCallback callback);

    /// <summary>
    /// Starts a high-resolution game loop with the specified configuration.
    /// Only one game loop per Stage is allowed.
    /// </summary>
    /// <param name="config">Game loop configuration.</param>
    /// <param name="callback">Callback invoked on each tick with deltaTime and totalElapsed.</param>
    /// <exception cref="InvalidOperationException">Thrown if a game loop is already running.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if fixedTimestep is out of valid range.</exception>
    void StartGameLoop(GameLoopConfig config, GameLoopCallback callback);

    /// <summary>
    /// Stops the running game loop.
    /// No-op if no game loop is running.
    /// </summary>
    void StopGameLoop();

    /// <summary>
    /// Gets whether a game loop is currently running for this Stage.
    /// </summary>
    bool IsGameLoopRunning { get; }

    #endregion
}
