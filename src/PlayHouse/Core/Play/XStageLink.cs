#nullable enable

using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Play.Base;
using PlayHouse.Core.Shared;
using PlayHouse.Core.Shared.TaskPool;
using PlayHouse.Runtime.ServerMesh.Communicator;
using PlayHouse.Runtime.ServerMesh.Discovery;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.Proto;

// Alias to avoid conflict with System.Threading.TimerCallback
using TimerCallbackDelegate = PlayHouse.Abstractions.Play.TimerCallback;

namespace PlayHouse.Core.Play;

/// <summary>
/// Internal implementation of IStageLink.
/// </summary>
/// <remarks>
/// XStageLink extends XLink with Stage-specific functionality:
/// - Timer management (repeat, count, cancel)
/// - AsyncBlock for external I/O operations
/// - CloseStage for stage lifecycle management
/// - SendToClient with StageId context
/// </remarks>
internal sealed class XStageLink : XLink, IStageLink
{
    private readonly IPlayDispatcher _dispatcher;
    private readonly IClientCommunicator _communicator;
    private readonly IClientReplyHandler? _clientReplyHandler;
    private IReplyPacketRegistry _replyPacketRegistry;
    private readonly HashSet<long> _timerIds = new();
    private long _timerIdCounter;
    private BaseStage? _baseStage;

    /// <inheritdoc/>
    public string StageId { get; }

    /// <inheritdoc/>
    public string StageType { get; private set; } = "";

    /// <summary>
    /// Gets the ServerId of this stage sender.
    /// </summary>
    public new string ServerId => base.ServerId;

    /// <summary>
    /// Initializes a new instance of the <see cref="XStageLink"/> class.
    /// </summary>
    public XStageLink(
        IClientCommunicator communicator,
        RequestCache requestCache,
        IServerInfoCenter serverInfoCenter,
        ServerType serverType,
        ushort serviceId,
        string serverId,
        string stageId,
        IPlayDispatcher dispatcher,
        IReplyPacketRegistry replyPacketRegistry,
        IClientReplyHandler? clientReplyHandler = null,
        int requestTimeoutMs = 30000)
        : base(communicator, requestCache, serverInfoCenter, serverType, serviceId, serverId, requestTimeoutMs)
    {
        StageId = stageId;
        _dispatcher = dispatcher;
        _communicator = communicator;
        _clientReplyHandler = clientReplyHandler;
        _replyPacketRegistry = replyPacketRegistry;
    }

    /// <summary>
    /// Sets the stage type identifier.
    /// </summary>
    /// <param name="stageType">Stage type.</param>
    internal void SetStageType(string stageType)
    {
        StageType = stageType;
    }

    /// <summary>
    /// Sets the BaseStage instance for callback queueing.
    /// </summary>
    /// <param name="baseStage">BaseStage instance.</param>
    internal void SetBaseStage(BaseStage baseStage)
    {
        _baseStage = baseStage;
    }

    /// <summary>
    /// Sets the reply packet registry (called after BaseStage creation).
    /// </summary>
    /// <param name="registry">Reply packet registry.</param>
    internal void SetReplyPacketRegistry(IReplyPacketRegistry registry)
    {
        _replyPacketRegistry = registry;
    }

    /// <summary>
    /// Gets the sender's Stage ID for Stage-to-Stage communication.
    /// </summary>
    /// <returns>This Stage's ID.</returns>
    protected override string GetSenderStageId()
    {
        return StageId;
    }

    /// <summary>
    /// Gets the delegate for posting callbacks to Stage event loop.
    /// </summary>
    /// <returns>Callback delegate that posts to BaseStage or executes directly.</returns>
    protected override Action<ReplyCallback, ushort, IPacket?>? GetPostToStageCallback()
    {
        var baseStage = _baseStage;
        if (baseStage == null) return null;

        return (callback, errorCode, packet) =>
        {
            // If we're already in the same Stage's event loop, execute directly
            // This prevents deadlock when Stage code awaits callback completion
            if (BaseStage.Current == baseStage)
            {
                // Execute directly with disposal (we're already in Stage context)
                if (packet is IDisposable disposable)
                {
                    baseStage.RegisterReplyForDisposal(disposable);
                }
                callback(errorCode, packet);
                // Note: DisposePendingReplies() will be called after DispatchMessageAsync completes
            }
            else
            {
                // Queue for later execution (callback arrived from network thread)
                baseStage.PostReplyCallback(callback, errorCode, packet);
            }
        };
    }

    #region Timer Management

    /// <inheritdoc/>
    public long AddRepeatTimer(TimeSpan initialDelay, TimeSpan period, TimerCallbackDelegate callback)
    {
        return AddTimer(TimerMsg.Types.Type.Repeat, initialDelay, period, 0, callback);
    }

    /// <inheritdoc/>
    public long AddCountTimer(TimeSpan initialDelay, TimeSpan period, int count, TimerCallbackDelegate callback)
    {
        return AddTimer(TimerMsg.Types.Type.Count, initialDelay, period, count, callback);
    }

    /// <inheritdoc/>
    public void CancelTimer(long timerId)
    {
        if (!_timerIds.Remove(timerId)) return;

        var timerPacket = CreateTimerPacket(
            TimerMsg.Types.Type.Cancel,
            timerId,
            TimeSpan.Zero,
            TimeSpan.Zero,
            0,
            () => Task.CompletedTask);

        _dispatcher.OnPost(new TimerMessage(timerPacket));
    }

    /// <inheritdoc/>
    public bool HasTimer(long timerId)
    {
        return _timerIds.Contains(timerId);
    }

    private long GenerateTimerId()
    {
        return Interlocked.Increment(ref _timerIdCounter);
    }

    private long AddTimer(
        TimerMsg.Types.Type type,
        TimeSpan initialDelay,
        TimeSpan period,
        int count,
        TimerCallbackDelegate callback)
    {
        var timerId = GenerateTimerId();
        var timerPacket = CreateTimerPacket(type, timerId, initialDelay, period, count, callback);
        _dispatcher.OnPost(new TimerMessage(timerPacket));
        _timerIds.Add(timerId);
        return timerId;
    }

    private TimerPacket CreateTimerPacket(
        TimerMsg.Types.Type type,
        long timerId,
        TimeSpan initialDelay,
        TimeSpan period,
        int count,
        TimerCallbackDelegate callback)
    {
        return new TimerPacket(
            StageId,
            timerId,
            type,
            (long)initialDelay.TotalMilliseconds,
            (long)period.TotalMilliseconds,
            count,
            callback);
    }

    #endregion

    #region Stage Management

    /// <inheritdoc/>
    public void CloseStage()
    {
        // Cancel all active timers
        foreach (var timerId in _timerIds.ToList())
        {
            CancelTimer(timerId);
        }
        _timerIds.Clear();

        // Stop game loop if running
        StopGameLoop();

        // Post stage destroy request
        _dispatcher.OnPost(new DestroyMessage(StageId));
    }

    #endregion

    #region Game Loop

    /// <inheritdoc/>
    public void StartGameLoop(TimeSpan fixedTimestep, GameLoopCallback callback)
    {
        StartGameLoop(new GameLoopConfig { FixedTimestep = fixedTimestep }, callback);
    }

    /// <inheritdoc/>
    public void StartGameLoop(GameLoopConfig config, GameLoopCallback callback)
    {
        ValidateTimestep(config.FixedTimestep);
        _dispatcher.StartGameLoop(StageId, config, callback);
    }

    /// <inheritdoc/>
    public void StopGameLoop()
    {
        _dispatcher.StopGameLoop(StageId);
    }

    /// <inheritdoc/>
    public bool IsGameLoopRunning => _dispatcher.IsGameLoopRunning(StageId);

    private static void ValidateTimestep(TimeSpan fixedTimestep)
    {
        if (fixedTimestep.TotalMilliseconds < 1 || fixedTimestep.TotalMilliseconds > 1000)
            throw new ArgumentOutOfRangeException(nameof(fixedTimestep),
                $"Fixed timestep must be between 1ms and 1000ms, got {fixedTimestep.TotalMilliseconds}ms.");
    }

    #endregion

    #region Async Operations

    /// <inheritdoc/>
    public void AsyncCompute(AsyncPreCallback preCallback, AsyncPostCallback? postCallback = null)
    {
        // Use ComputeTaskPool for CPU-bound work (limited to CPU core count)
        _dispatcher.ComputePool.Post(new AsyncWorkItem(this, preCallback, postCallback));
    }

    /// <inheritdoc/>
    public void AsyncIO(AsyncPreCallback preCallback, AsyncPostCallback? postCallback = null)
    {
        // Use IoTaskPool for I/O-bound work (higher concurrency)
        _dispatcher.IoPool.Post(new AsyncWorkItem(this, preCallback, postCallback));
    }

    /// <summary>
    /// Internal work item to execute async operations on task pools.
    /// </summary>
    private sealed class AsyncWorkItem(XStageLink link, AsyncPreCallback pre, AsyncPostCallback? post) : ITaskPoolWorkItem
    {
        public async Task ExecuteAsync()
        {
            try
            {
                var result = await pre.Invoke();
                if (post != null)
                {
                    // Dispatch post-callback back to the Stage event loop
                    link._dispatcher.OnPost(new AsyncMessage(new AsyncBlockPacket(link.StageId, post, result)));
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[AsyncOperation] Error in PreCallback: {ex.Message}");
            }
        }
    }

    #endregion

    #region Client Communication

    /// <inheritdoc/>
    public void SendToClient(string sessionServerId, long sid, IPacket packet)
    {
        // For directly connected clients, use transport handler
        if (sid > 0 && _clientReplyHandler != null)
        {
            // Client push message - msgSeq = 0 (not a response to a request)
            _ = _clientReplyHandler.SendClientReplyAsync(
                sid,
                packet.MsgId,
                0,  // msgSeq = 0 for push messages
                StageId,
                0,  // errorCode = 0
                packet.Payload);
            return;
        }

        // Server-to-server communication (e.g., through API server)
        var header = new RouteHeader
        {
            ServiceId = ServiceId,
            MsgId = packet.MsgId,
            From = ServerId,
            StageId = StageId,
            Sid = sid
        };

        // Note: ProtoPayload now serializes eagerly in constructor (no lazy serialization).
        // Zero-copy: RoutePacket references the payload without copying
        // RoutePacket.Of(header, IPayload) sets ownsPayload=false, original packet retains ownership
        var routePacket = RoutePacket.Of(header, packet.Payload);
        _communicator.Send(sessionServerId, routePacket);
    }

    #endregion

    #region Client Reply Override

    /// <summary>
    /// Overrides Reply to detect client requests and route through transport.
    /// </summary>
    public new void Reply(ushort errorCode)
    {
        if (CurrentHeader?.Sid > 0 && _clientReplyHandler != null)
        {
            // Client request - route through transport
            _ = _clientReplyHandler.SendClientReplyAsync(
                CurrentHeader.Sid,
                CurrentHeader.MsgId,
                (ushort)CurrentHeader.MsgSeq,
                StageId,
                errorCode,
                Abstractions.EmptyPayload.Instance);
        }
        else
        {
            // Server-to-server request - use base implementation
            base.Reply(errorCode);
        }
    }

    /// <summary>
    /// Overrides Reply to detect client requests and route through transport.
    /// </summary>
    public new void Reply(IPacket reply)
    {
        if (CurrentHeader?.Sid > 0 && _clientReplyHandler != null)
        {
            // Capture header values before async execution (CurrentHeader may be cleared during await)
            var sid = CurrentHeader.Sid;
            var msgSeq = (ushort)CurrentHeader.MsgSeq;

            // Client request - route through transport with fire-and-forget disposal
            _ = ReplyAndDisposeAsync(reply, sid, msgSeq);
        }
        else
        {
            // Server-to-server request - use base implementation
            base.Reply(reply);
        }
    }

    /// <summary>
    /// Sends client reply and ensures packet disposal after transmission.
    /// </summary>
    private async Task ReplyAndDisposeAsync(IPacket reply, long sid, ushort msgSeq)
    {
        try
        {
            await _clientReplyHandler!.SendClientReplyAsync(
                sid,
                reply.MsgId,
                msgSeq,
                StageId,
                0,
                reply.Payload);
        }
        finally
        {
            reply.Dispose();  // ArrayPool.Return is called here
        }
    }

    #endregion

    #region Request Overrides with Reply Registration

    /// <summary>
    /// Overrides RequestToApi to register reply packet for disposal.
    /// </summary>
    public new async Task<IPacket> RequestToApi(string apiServerId, IPacket packet)
    {
        var reply = await base.RequestToApi(apiServerId, packet);
        _replyPacketRegistry.RegisterReplyForDisposal(reply);
        return reply;
    }

    /// <summary>
    /// Overrides RequestToStage to register reply packet for disposal.
    /// </summary>
    public new async Task<IPacket> RequestToStage(string playServerId, string stageId, IPacket packet)
    {
        var reply = await base.RequestToStage(playServerId, stageId, packet);
        _replyPacketRegistry.RegisterReplyForDisposal(reply);
        return reply;
    }

    #endregion

    /// <summary>
    /// Called when a timer is removed externally (e.g., timer expired).
    /// </summary>
    internal void OnTimerRemoved(long timerId)
    {
        _timerIds.Remove(timerId);
    }
}

/// <summary>
/// Packet for timer operations.
/// </summary>
internal sealed class TimerPacket
{
    public string StageId { get; }
    public long TimerId { get; }
    public TimerMsg.Types.Type Type { get; }
    public long InitialDelayMs { get; }
    public long PeriodMs { get; }
    public int Count { get; }
    public TimerCallbackDelegate Callback { get; }

    public TimerPacket(
        string stageId,
        long timerId,
        TimerMsg.Types.Type type,
        long initialDelayMs,
        long periodMs,
        int count,
        TimerCallbackDelegate callback)
    {
        StageId = stageId;
        TimerId = timerId;
        Type = type;
        InitialDelayMs = initialDelayMs;
        PeriodMs = periodMs;
        Count = count;
        Callback = callback;
    }
}

/// <summary>
/// Packet for AsyncBlock post-callback.
/// </summary>
internal sealed class AsyncBlockPacket
{
    public string StageId { get; }
    public AsyncPostCallback PostCallback { get; }
    public object? Result { get; }

    public AsyncBlockPacket(string stageId, AsyncPostCallback postCallback, object? result)
    {
        StageId = stageId;
        PostCallback = postCallback;
        Result = result;
    }
}
