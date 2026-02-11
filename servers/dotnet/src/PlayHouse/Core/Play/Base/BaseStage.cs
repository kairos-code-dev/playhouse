#nullable enable

using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Shared;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.Proto;

// Alias to avoid conflict with System.Threading.TimerCallback
using TimerCallbackDelegate = PlayHouse.Abstractions.Play.TimerCallback;

namespace PlayHouse.Core.Play.Base;

/// <summary>
/// Base class that manages Stage lifecycle, DI scope, and mailbox-based scheduling using ThreadPool.
/// </summary>
internal sealed class BaseStage(
    IStage stage,
    XStageLink stageLink,
    ILogger logger,
    BaseStageCmdHandler cmdHandler,
    IServiceScope serviceScope) : IReplyPacketRegistry
{
    private readonly IServiceScope _serviceScope = serviceScope;
    
    private readonly Dictionary<string, BaseActor> _actors = new();
    private readonly ConcurrentQueue<StageMessage> _mailbox = new();
    private readonly List<IDisposable> _pendingReplyPackets = new();
    private int _isScheduled;

    // AsyncLocal to track current Stage context
    private static readonly AsyncLocal<BaseStage?> CurrentStage = new();

    /// <summary>
    /// Gets the current Stage being processed.
    /// </summary>
    internal static BaseStage? Current => CurrentStage.Value;

    public IStage Stage { get; } = stage;
    public XStageLink StageLink { get; } = stageLink;
    public bool IsCreated { get; private set; }
    public long StageId => StageLink.StageId;
    public string StageType => StageLink.StageType;

    public void RegisterReplyForDisposal(IDisposable packet) => _pendingReplyPackets.Add(packet);

    private void DisposePendingReplies()
    {
        foreach (var packet in _pendingReplyPackets)
        {
            try { packet.Dispose(); }
            catch (Exception ex) { logger.LogError(ex, "Error disposing pending reply packet"); }
        }
        _pendingReplyPackets.Clear();
    }

    /// <summary>
    /// ThreadPool에서 실행될 핵심 로직.
    /// 메일박스의 메시지를 순차적으로 배치 처리합니다.
    /// </summary>
    private async Task ExecuteAsync()
    {
        CurrentStage.Value = this;
        try
        {
            int processed = 0;
            while (processed < 100 && _mailbox.TryDequeue(out var message))
            {
                try
                {
                    var task = message.ExecuteAsync();
                    if (task.IsCompleted) task.GetAwaiter().GetResult();
                    else await task;
                }
                catch (Exception ex) 
                {
                    logger.LogError(ex, "Error executing message in Stage {StageId}", StageId); 
                }
                finally
                {
                    message.Dispose();
                    DisposePendingReplies();
                }
                processed++;
            }
        }
        finally
        {
            CurrentStage.Value = null;
            Interlocked.Exchange(ref _isScheduled, 0);
            if (!_mailbox.IsEmpty) ScheduleExecution();
        }
    }

    private void ScheduleExecution()
    {
        if (Interlocked.CompareExchange(ref _isScheduled, 1, 0) == 0)
        {
            // ThreadPool.QueueUserWorkItem을 사용하여 ExecutionContext/AsyncLocal 유지
            ThreadPool.QueueUserWorkItem(_ => _ = ExecuteAsync());
        }
    }

    #region Post Methods

    public void Post(RoutePacket packet)
    {
        _mailbox.Enqueue(new StageMessage.RouteMessage(packet) { Stage = this });
        ScheduleExecution();
    }

    internal void PostTimerCallback(long timerId, TimerCallbackDelegate callback)
    {
        _mailbox.Enqueue(new StageMessage.TimerMessage(timerId, callback) { Stage = this });
        ScheduleExecution();
    }

    internal void PostAsyncBlock(AsyncBlockPacket asyncPacket)
    {
        _mailbox.Enqueue(new StageMessage.AsyncMessage(asyncPacket) { Stage = this });
        ScheduleExecution();
    }

    internal void PostReplyCallback(ReplyCallback callback, ushort errorCode, IPacket? packet)
    {
        _mailbox.Enqueue(new StageMessage.ReplyCallbackMessage(callback, errorCode, packet) { Stage = this });
        ScheduleExecution();
    }

    internal void PostClientRoute(BaseActor actor, string accountId, string msgId, ushort msgSeq, long sid, IPayload payload)
    {
        var message = StageMessage.ClientRouteMessagePool.Get();
        message.Update(actor, accountId, msgId, msgSeq, sid, payload);
        message.Stage = this;
        _mailbox.Enqueue(message);
        ScheduleExecution();
    }

    internal void PostClientRoute(string accountId, string msgId, ushort msgSeq, long sid, IPayload payload)
    {
        if (_actors.TryGetValue(accountId, out var actor))
        {
            var message = StageMessage.ClientRouteMessagePool.Get();
            message.Update(actor, accountId, msgId, msgSeq, sid, payload);
            message.Stage = this;
            _mailbox.Enqueue(message);
            ScheduleExecution();
        }
        else
        {
            logger.LogWarning("Actor {AccountId} not found for client message {MsgId}", accountId, msgId);
            payload.Dispose();
        }
    }

    internal void PostJoinActor(StageMessage.JoinActorMessage message)
    {
        message.Stage = this;
        _mailbox.Enqueue(message);
        ScheduleExecution();
    }

    internal void PostDisconnect(string accountId)
    {
        _mailbox.Enqueue(new StageMessage.DisconnectMessage(accountId) { Stage = this });
        ScheduleExecution();
    }

    /// <summary>
    /// 비동기 Continuation을 메일박스에 게시하여 순차성을 보장합니다.
    /// </summary>
    internal void PostContinuation(SendOrPostCallback callback, object? state)
    {
        var msg = StageMessage.ContinuationMessagePool.Get();
        msg.Update(callback, state);
        msg.Stage = this;
        _mailbox.Enqueue(msg);
        ScheduleExecution();
    }

    /// <summary>
    /// Posts a game loop tick to the Stage mailbox for sequential execution.
    /// Called from the GameLoopTimer's dedicated background thread.
    /// </summary>
    internal void PostGameLoopTick(GameLoopCallback callback, TimeSpan deltaTime, TimeSpan totalElapsed)
    {
        var msg = StageMessage.GameLoopTickMessagePool.Get();
        msg.Update(callback, deltaTime, totalElapsed);
        msg.Stage = this;
        _mailbox.Enqueue(msg);
        ScheduleExecution();
    }

    internal static IDisposable SetCurrentContext(BaseStage stage)
    {
        CurrentStage.Value = stage;
        return new ContextScope();
    }

    private sealed class ContextScope : IDisposable
    {
        public void Dispose() => CurrentStage.Value = null;
    }

    #endregion

    #region Message Handlers

    internal Task DispatchRoutePacketAsync(RoutePacket packet)
    {
        StageLink.SetCurrentHeader(packet.Header);
        try
        {
            var msgId = packet.MsgId;
            var accountIdString = packet.AccountId.ToString();
            Task task;
            if (IsSystemMessage(msgId)) task = HandleSystemMessageAsync(msgId, packet);
            else if (packet.AccountId != 0 && _actors.TryGetValue(accountIdString, out var baseActor))
            {
                var contentPacket = CreateContentPacket(packet);
                task = Stage.OnDispatch(baseActor.Actor, contentPacket);
            }
            else
            {
                var contentPacket = CreateContentPacket(packet);
                task = Stage.OnDispatch(contentPacket);
            }

            if (!task.IsCompleted)
            {
                return task.ContinueWith(t => {
                    StageLink.ClearCurrentHeader();
                    DisposePendingReplies();
                    if (t.IsFaulted) throw t.Exception!;
                }, TaskScheduler.Default);
            }
            StageLink.ClearCurrentHeader();
            return task;
        }
        catch { StageLink.ClearCurrentHeader(); throw; }
    }

    private static bool IsSystemMessage(string msgId) =>
        msgId.StartsWith("PlayHouse.Runtime.Proto.") ||
        msgId == nameof(CreateStageReq) || msgId == nameof(GetOrCreateStageReq) ||
        msgId == nameof(DestroyStageReq) || msgId == nameof(DisconnectNoticeMsg) || msgId == nameof(ReconnectMsg);

    private async Task HandleSystemMessageAsync(string msgId, RoutePacket packet)
    {
        await cmdHandler.HandleAsync(msgId, packet, this);
    }

    internal async Task HandleDestroyAsync()
    {
        // Destroy all actors and dispose their scopes
        foreach (var baseActor in _actors.Values.ToList())
        {
            await baseActor.Actor.OnDestroy();
            baseActor.Dispose();  // Dispose Actor's DI scope
        }
        _actors.Clear();

        // Destroy Stage
        try { await Stage.OnDestroy(); }
        catch (Exception ex) { logger?.LogError(ex, "Error destroying Stage {StageId}", StageId); }

        // Dispose Stage's DI scope
        _serviceScope.Dispose();
    }

    private static IPacket CreateContentPacket(RoutePacket packet) => CPacket.Of(packet.MsgId, packet.Payload);

    internal Task ProcessClientRouteAsync(StageMessage.ClientRouteMessage message)
    {
        var baseActor = message.Actor;
        if (baseActor == null) _actors.TryGetValue(message.AccountId, out baseActor);

        if (baseActor != null)
        {
            var header = new RouteHeader { MsgSeq = message.MsgSeq, ServiceId = 1, MsgId = message.MsgId, From = "client", StageId = StageId, AccountId = 0, Sid = message.Sid };
            StageLink.SetCurrentHeader(header);
            try
            {
                var payload = message.TakePayload();
                if (payload != null)
                {
                    using var packet = CPacket.Of(message.MsgId, payload);
                    var task = Stage.OnDispatch(baseActor.Actor, packet);
                    if (!task.IsCompleted)
                    {
                        return task.ContinueWith(t => {
                            StageLink.ClearCurrentHeader();
                            DisposePendingReplies();
                            if (t.IsFaulted) throw t.Exception!;
                        }, TaskScheduler.Default);
                    }
                    StageLink.ClearCurrentHeader();
                    return task;
                }
            }
            catch { StageLink.ClearCurrentHeader(); throw; }
        }
        return Task.CompletedTask;
    }

    internal async Task ProcessJoinActorAsync(StageMessage.JoinActorMessage message)
    {
        var actor = message.Actor;
        var accountId = actor.AccountId;
        ushort errorCode = (ushort)ErrorCode.Success;
        try
        {
            if (_actors.TryGetValue(accountId, out var existingActor))
            {
                await actor.Actor.OnDestroy();
                actor.Dispose();  // Dispose new Actor's DI scope (keeping existing)
                existingActor.ActorLink.Update(actor.ActorLink.SessionNid, actor.ActorLink.Sid, actor.ActorLink.ApiNid);
                await Stage.OnConnectionChanged(existingActor.Actor, true);
            }
            else
            {
                var joinResult = await Stage.OnJoinStage(actor.Actor);
                if (!joinResult)
                {
                    errorCode = (ushort)ErrorCode.JoinStageFailed;
                    await actor.Actor.OnDestroy();
                    actor.Dispose();  // Dispose Actor's DI scope on join failure
                }
                else
                {
                    AddActor(actor);
                    await Stage.OnPostJoinStage(actor.Actor);
                }
            }
        }
        catch (Exception ex) { logger?.LogError(ex, "Error in ProcessJoinActorAsync"); errorCode = (ushort)ErrorCode.InternalError; }
        finally
        {
            // Use auth reply packet from OnAuthenticate if available
            if (message.AuthReplyPacket != null)
            {
                message.Session.SendResponse(message.AuthReplyMsgId, message.MsgSeq, StageId, errorCode, message.AuthReplyPacket.Payload.DataSpan);
            }
            else
            {
                message.Session.SendResponse(message.AuthReplyMsgId, message.MsgSeq, StageId, errorCode, ReadOnlySpan<byte>.Empty);
            }
        }
    }

    internal async Task ProcessDisconnectAsync(string accountId)
    {
        if (_actors.TryGetValue(accountId, out var baseActor)) await Stage.OnConnectionChanged(baseActor.Actor, false);
    }

    #endregion

    #region Actor Management
    public void AddActor(BaseActor baseActor) => _actors[baseActor.AccountId] = baseActor;
    public bool RemoveActor(string accountId) => _actors.Remove(accountId);
    public BaseActor? GetActor(string accountId) => _actors.GetValueOrDefault(accountId);
    public int ActorCount => _actors.Count;
    public async Task LeaveStage(string accountId, string sessionNid, long sid)
    {
        if (_actors.Remove(accountId, out var baseActor))
        {
            await baseActor.Actor.OnDestroy();
            baseActor.Dispose();  // Dispose Actor's DI scope
        }
    }
    #endregion

    #region Reply / Helpers
    public void Reply(ushort errorCode) => StageLink.Reply(errorCode);
    public void Reply(IPacket packet) => StageLink.Reply(packet);

    public async Task<(bool success, IPacket? reply)> CreateStage(string stageType, IPacket packet)
    {
        StageLink.SetStageType(stageType);
        var (result, replyPacket) = await Stage.OnCreate(packet);
        if (result) IsCreated = true;
        return (result, replyPacket);
    }

    public async Task<(bool success, ushort errorCode, BaseActor? actor, IPacket? authReply)> JoinActor(string sessionNid, long sid, string apiNid, IPacket authPacket, PlayProducer producer)
    {
        var actorSender = new XActorLink(sessionNid, sid, apiNid, this);
        IActor actor;
        IServiceScope? actorScope;
        try
        {
            (actor, actorScope) = producer.GetActorWithScope(StageType, actorSender);
        }
        catch
        {
            return (false, (ushort)ErrorCode.InvalidStageType, null, null);
        }

        await actor.OnCreate();

        var (authResult, authReply) = await actor.OnAuthenticate(authPacket);
        if (!authResult)
        {
            await actor.OnDestroy();
            actorScope?.Dispose();
            authReply?.Dispose();
            return (false, (ushort)ErrorCode.AuthenticationFailed, null, null);
        }

        if (string.IsNullOrEmpty(actorSender.AccountId))
        {
            await actor.OnDestroy();
            actorScope?.Dispose();
            authReply?.Dispose();
            return (false, (ushort)ErrorCode.InvalidAccountId, null, null);
        }

        var checkedStageId = await actor.OnCheckStage();
        if (checkedStageId <= 0 || checkedStageId != StageId)
        {
            await actor.OnDestroy();
            actorScope?.Dispose();
            authReply?.Dispose();
            return (false, (ushort)ErrorCode.StageNotFound, null, null);
        }

        await actor.OnPostAuthenticate();
        return (true, (ushort)ErrorCode.Success, new BaseActor(actor, actorSender, actorScope), authReply);
    }

    public async Task OnPostCreate() => await Stage.OnPostCreate();
    public void MarkAsCreated() => IsCreated = true;
    internal void PostDestroy()
    {
        _mailbox.Enqueue(new StageMessage.DestroyMessage() { Stage = this });
        ScheduleExecution();
    }
    #endregion
}
