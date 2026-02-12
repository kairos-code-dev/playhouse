#nullable enable

using FluentAssertions;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Shared;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Play;
using PlayHouse.Runtime.ServerMesh.Communicator;
using PlayHouse.Runtime.ServerMesh.Discovery;
using PlayHouse.Runtime.ServerMesh.Message;
using PlayHouse.Runtime.Proto;
using NSubstitute;
using Xunit;

namespace PlayHouse.Unit.Core.Play;

/// <summary>
/// 단위 테스트: PlayDispatcher의 메시지 라우팅 및 Stage 관리 기능 검증
/// </summary>
public class PlayDispatcherTests : IDisposable
{
    #region Fake Implementations

    private class FakeStage : IStage
    {
        public IStageLink StageLink { get; }
        public bool OnCreateCalled { get; private set; }
        public int OnPostCreateCallCount { get; private set; }
        public bool OnDestroyCalled { get; private set; }
        public int OnDispatchCount { get; private set; }

        public FakeStage(IStageLink stageLink)
        {
            StageLink = stageLink;
        }

        public Task<(bool result, IPacket reply)> OnCreate(IPacket packet)
        {
            OnCreateCalled = true;
            return Task.FromResult<(bool result, IPacket reply)>((true, CPacket.Empty("CreateReply")));
        }

        public Task OnPostCreate()
        {
            OnPostCreateCallCount++;
            return Task.CompletedTask;
        }

        public Task OnDestroy()
        {
            OnDestroyCalled = true;
            return Task.CompletedTask;
        }

        public Task<bool> OnJoinStage(IActor actor) => Task.FromResult(true);
        public Task OnPostJoinStage(IActor actor) => Task.CompletedTask;
        public ValueTask OnConnectionChanged(IActor actor, bool isConnected) => ValueTask.CompletedTask;

        public Task OnDispatch(IActor actor, IPacket packet)
        {
            OnDispatchCount++;
            return Task.CompletedTask;
        }

        public Task OnDispatch(IPacket packet)
        {
            OnDispatchCount++;
            return Task.CompletedTask;
        }
    }

    private class FakeActor : IActor
    {
        public IActorLink ActorLink { get; }

        public FakeActor(IActorLink actorLink)
        {
            ActorLink = actorLink;
        }

        public Task OnCreate() => Task.CompletedTask;
        public Task OnDestroy() => Task.CompletedTask;
        public Task<(bool result, IPacket? reply)> OnAuthenticate(IPacket authPacket)
        {
            ActorLink.SetAuthContext("1", "1");
            return Task.FromResult<(bool, IPacket?)>((true, null));
        }
        public Task OnPostAuthenticate() => Task.CompletedTask;
    }

    private sealed class DisposableReplyPacket : IPacket
    {
        private readonly IPacket _inner;
        private readonly Action _onDispose;
        private bool _disposed;

        public DisposableReplyPacket(IPacket inner, Action onDispose)
        {
            _inner = inner;
            _onDispose = onDispose;
        }

        public string MsgId => _inner.MsgId;
        public IPayload Payload => _inner.Payload;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _inner.Dispose();
            _onDispose();
        }
    }

    private class DisposableReplyStage : IStage
    {
        internal static int OnPostCreateCallCount;
        internal static int ReplyDisposeCallCount;

        public static void ResetCounters()
        {
            OnPostCreateCallCount = 0;
            ReplyDisposeCallCount = 0;
        }

        public DisposableReplyStage(IStageLink stageLink)
        {
            StageLink = stageLink;
        }

        public IStageLink StageLink { get; }

        public Task<(bool result, IPacket reply)> OnCreate(IPacket packet)
        {
            var reply = new DisposableReplyPacket(
                CPacket.Empty("CreateReply"),
                () => ReplyDisposeCallCount++);
            return Task.FromResult<(bool result, IPacket reply)>((true, reply));
        }

        public Task OnPostCreate()
        {
            OnPostCreateCallCount++;
            return Task.CompletedTask;
        }

        public Task OnDestroy() => Task.CompletedTask;
        public Task<bool> OnJoinStage(IActor actor) => Task.FromResult(true);
        public Task OnPostJoinStage(IActor actor) => Task.CompletedTask;
        public ValueTask OnConnectionChanged(IActor actor, bool isConnected) => ValueTask.CompletedTask;
        public Task OnDispatch(IActor actor, IPacket packet) => Task.CompletedTask;
        public Task OnDispatch(IPacket packet) => Task.CompletedTask;
    }

    private class FailFirstPostCreateStage : IStage
    {
        private static int _shouldFailPostCreate;
        internal static int OnCreateCallCount;
        internal static int OnPostCreateCallCount;
        internal static int ReplyDisposeCallCount;

        public static void ResetCounters()
        {
            _shouldFailPostCreate = 1;
            OnCreateCallCount = 0;
            OnPostCreateCallCount = 0;
            ReplyDisposeCallCount = 0;
        }

        public FailFirstPostCreateStage(IStageLink stageLink)
        {
            StageLink = stageLink;
        }

        public IStageLink StageLink { get; }

        public Task<(bool result, IPacket reply)> OnCreate(IPacket packet)
        {
            OnCreateCallCount++;
            var reply = new DisposableReplyPacket(
                CPacket.Empty("CreateReply"),
                () => ReplyDisposeCallCount++);
            return Task.FromResult<(bool result, IPacket reply)>((true, reply));
        }

        public Task OnPostCreate()
        {
            OnPostCreateCallCount++;
            if (Interlocked.Exchange(ref _shouldFailPostCreate, 0) == 1)
            {
                throw new InvalidOperationException("Simulated OnPostCreate failure.");
            }

            return Task.CompletedTask;
        }

        public Task OnDestroy() => Task.CompletedTask;
        public Task<bool> OnJoinStage(IActor actor) => Task.FromResult(true);
        public Task OnPostJoinStage(IActor actor) => Task.CompletedTask;
        public ValueTask OnConnectionChanged(IActor actor, bool isConnected) => ValueTask.CompletedTask;
        public Task OnDispatch(IActor actor, IPacket packet) => Task.CompletedTask;
        public Task OnDispatch(IPacket packet) => Task.CompletedTask;
    }

    #endregion

    private readonly IClientCommunicator _communicator;
    private readonly RequestCache _requestCache;
    private readonly PlayProducer _producer;
    private readonly PlayDispatcher _dispatcher;

    public PlayDispatcherTests()
    {
        _communicator = Substitute.For<IClientCommunicator>();
        _requestCache = new RequestCache(NullLogger<RequestCache>.Instance);

        // Manual registration을 위한 빈 ServiceProvider 생성
        var emptyServiceProvider = new ServiceCollection().BuildServiceProvider();
        _producer = new PlayProducer(
            new Dictionary<string, Type>(),
            new Dictionary<string, Type>(),
            emptyServiceProvider);

        // Register test stage type
        _producer.Register(
            "test_stage",
            stageSender => new FakeStage(stageSender),
            actorSender => new FakeActor(actorSender));

        _producer.Register(
            "dispose_stage",
            stageSender => new DisposableReplyStage(stageSender),
            actorSender => new FakeActor(actorSender));

        _producer.Register(
            "fail_first_postcreate_stage",
            stageSender => new FailFirstPostCreateStage(stageSender),
            actorSender => new FakeActor(actorSender));

        var serverInfoCenter = Substitute.For<IServerInfoCenter>();

        _dispatcher = new PlayDispatcher(
            _producer,
            _communicator,
            _requestCache,
            serverInfoCenter,
            ServerType.Play,
            1,
            "play-1",
            null,
            NullLoggerFactory.Instance);
    }

    public void Dispose()
    {
        _dispatcher.Dispose();
    }

    [Fact(DisplayName = "StageCount - 초기값은 0이다")]
    public void StageCount_Initially_IsZero()
    {
        // Given (전제조건)
        // When (행동)
        var count = _dispatcher.StageCount;

        // Then (결과)
        count.Should().Be(0, "초기 Stage 수는 0이어야 함");
    }

    [Fact(DisplayName = "TotalActorCount - 초기값은 0이다")]
    public void TotalActorCount_Initially_IsZero()
    {
        // Given (전제조건)
        // When (행동)
        var count = _dispatcher.TotalActorCount;

        // Then (결과)
        count.Should().Be(0, "초기 Actor 수는 0이어야 함");
    }

    [Fact(DisplayName = "ActiveTimerCount - 초기값은 0이다")]
    public void ActiveTimerCount_Initially_IsZero()
    {
        // Given (전제조건)
        // When (행동)
        var count = _dispatcher.ActiveTimerCount;

        // Then (결과)
        count.Should().Be(0, "초기 타이머 수는 0이어야 함");
    }

    [Fact(DisplayName = "Post(CreateStageReq) - 새 Stage를 생성한다")]
    public async Task Post_CreateStageReq_CreatesNewStage()
    {
        // Given (전제조건)
        const string stageId = "100";
        var packet = CreateCreateStagePacket(stageId, "test_stage");

        // When (행동)
        _dispatcher.OnPost(new RouteMessage(packet));
        await Task.Delay(100); // 이벤트 루프 처리 대기

        // Then (결과)
        _dispatcher.StageCount.Should().Be(1, "Stage가 생성되어야 함");
    }

    [Fact(DisplayName = "Post(CreateStageReq) - OnPostCreate를 호출한다")]
    public async Task Post_CreateStageReq_CallsOnPostCreate()
    {
        // Given
        const string stageId = "120";
        var packet = CreateCreateStagePacket(stageId, "test_stage");

        // When
        _dispatcher.OnPost(new RouteMessage(packet));
        await Task.Delay(100);

        // Then
        var stage = _dispatcher.GetStage(stageId);
        stage.Should().NotBeNull("Stage가 생성되어야 함");
        var fakeStage = stage!.Stage as FakeStage;
        fakeStage.Should().NotBeNull("FakeStage 인스턴스여야 함");
        fakeStage!.OnCreateCalled.Should().BeTrue("OnCreate가 호출되어야 함");
        fakeStage.OnPostCreateCallCount.Should().Be(1, "OnPostCreate가 1회 호출되어야 함");
    }

    [Fact(DisplayName = "Post(CreateStageReq) - OnCreate 반환 reply 패킷을 Dispose한다")]
    public async Task Post_CreateStageReq_DisposesOnCreateReplyPacket()
    {
        // Given
        DisposableReplyStage.ResetCounters();
        const string stageId = "121";
        var packet = CreateCreateStagePacket(stageId, "dispose_stage");

        // When
        _dispatcher.OnPost(new RouteMessage(packet));
        await Task.Delay(100);

        // Then
        DisposableReplyStage.OnPostCreateCallCount.Should().Be(1, "생성 후 OnPostCreate가 호출되어야 함");
        DisposableReplyStage.ReplyDisposeCallCount.Should().Be(1, "OnCreate에서 반환된 reply 패킷은 반드시 Dispose되어야 함");
    }

    [Fact(DisplayName = "Post(CreateStageReq) - 이미 존재하는 StageId는 에러를 반환한다")]
    public async Task Post_CreateStageReq_DuplicateStageId_SendsError()
    {
        // Given (전제조건)
        const string stageId = "100";
        var packet1 = CreateCreateStagePacket(stageId, "test_stage");
        var packet2 = CreateCreateStagePacket(stageId, "test_stage");

        _dispatcher.OnPost(new RouteMessage(packet1));
        await Task.Delay(100);

        // When (행동)
        _dispatcher.OnPost(new RouteMessage(packet2));
        await Task.Delay(100);

        // Then (결과)
        _dispatcher.StageCount.Should().Be(1, "중복 Stage는 생성되지 않아야 함");
    }

    [Fact(DisplayName = "Post(CreateStageReq) - 유효하지 않은 StageType은 에러를 반환한다")]
    public async Task Post_CreateStageReq_InvalidStageType_SendsError()
    {
        // Given (전제조건)
        const string stageId = "100";
        var packet = CreateCreateStagePacket(stageId, "invalid_type");

        // When (행동)
        _dispatcher.OnPost(new RouteMessage(packet));
        await Task.Delay(100);

        // Then (결과)
        _dispatcher.StageCount.Should().Be(0, "유효하지 않은 타입의 Stage는 생성되지 않아야 함");
    }

    [Fact(DisplayName = "Post - 존재하지 않는 Stage로 메시지 전송 시 에러를 반환한다")]
    public void Post_NonExistentStage_SendsError()
    {
        // Given (전제조건)
        const string stageId = "999";
        var packet = CreateTestPacket(stageId, "TestMsg", msgSeq: 1);

        // When (행동)
        _dispatcher.OnPost(new RouteMessage(packet));

        // Then (결과)
        // 에러 응답이 전송되어야 함 (communicator.Send가 호출됨)
        _communicator.Received().Send(Arg.Any<string>(), Arg.Any<RoutePacket>());
    }

    [Fact(DisplayName = "PostDestroy - Stage를 제거한다")]
    public async Task PostDestroy_RemovesStage()
    {
        // Given (전제조건)
        const string stageId = "100";
        var createPacket = CreateCreateStagePacket(stageId, "test_stage");
        _dispatcher.OnPost(new RouteMessage(createPacket));
        await Task.Delay(100);

        _dispatcher.StageCount.Should().Be(1);

        // When (행동)
        _dispatcher.OnPost(new DestroyMessage(stageId));
        await Task.Delay(100);

        // Then (결과)
        _dispatcher.StageCount.Should().Be(0, "Stage가 제거되어야 함");
    }

    [Fact(DisplayName = "PostTimer - 타이머를 추가한다")]
    public void PostTimer_AddsTimer()
    {
        // Given (전제조건)
        var timerPacket = new TimerPacket(
            stageId: "100",
            timerId: 1,
            type: TimerMsg.Types.Type.Repeat,
            initialDelayMs: 1000,
            periodMs: 1000,
            count: 0,
            callback: () => Task.CompletedTask);

        // When (행동)
        _dispatcher.OnPost(new TimerMessage(timerPacket));

        // Then (결과)
        _dispatcher.ActiveTimerCount.Should().Be(1, "타이머가 추가되어야 함");
    }

    [Fact(DisplayName = "Dispose - 모든 Stage가 정리된다")]
    public async Task Dispose_CleansUpAllStages()
    {
        // Given (전제조건)
        var serverInfoCenter = Substitute.For<IServerInfoCenter>();
        var dispatcher = new PlayDispatcher(
            _producer,
            _communicator,
            _requestCache,
            serverInfoCenter,
            ServerType.Play,
            1,
            "play-1",
            null,
            NullLoggerFactory.Instance);

        var packet1 = CreateCreateStagePacket("100", "test_stage");
        var packet2 = CreateCreateStagePacket("101", "test_stage");

        dispatcher.OnPost(new RouteMessage(packet1));
        dispatcher.OnPost(new RouteMessage(packet2));
        await Task.Delay(100);

        dispatcher.StageCount.Should().Be(2);

        // When (행동)
        dispatcher.Dispose();
        await Task.Delay(100);

        // Then (결과)
        dispatcher.StageCount.Should().Be(0, "Dispose 후 모든 Stage가 정리되어야 함");
    }

    [Fact(DisplayName = "Post(GetOrCreateStageReq) - OnPostCreate는 최초 생성 시 한 번만 호출된다")]
    public async Task Post_GetOrCreateStageReq_CallsOnPostCreateOnlyOnce()
    {
        // Given
        const string stageId = "130";
        var packet1 = CreateGetOrCreateStagePacket(stageId, "test_stage");
        var packet2 = CreateGetOrCreateStagePacket(stageId, "test_stage");

        // When
        _dispatcher.OnPost(new RouteMessage(packet1));
        await Task.Delay(100);
        _dispatcher.OnPost(new RouteMessage(packet2));
        await Task.Delay(100);

        // Then
        var stage = _dispatcher.GetStage(stageId);
        stage.Should().NotBeNull("Stage가 존재해야 함");
        var fakeStage = stage!.Stage as FakeStage;
        fakeStage.Should().NotBeNull("FakeStage 인스턴스여야 함");
        fakeStage!.OnPostCreateCallCount.Should().Be(1, "이미 생성된 Stage에는 OnPostCreate가 다시 호출되면 안 됨");
    }

    [Fact(DisplayName = "Post(GetOrCreateStageReq) - OnCreate 반환 reply 패킷을 Dispose한다")]
    public async Task Post_GetOrCreateStageReq_DisposesOnCreateReplyPacket()
    {
        // Given
        DisposableReplyStage.ResetCounters();
        const string stageId = "131";
        var packet1 = CreateGetOrCreateStagePacket(stageId, "dispose_stage");
        var packet2 = CreateGetOrCreateStagePacket(stageId, "dispose_stage");

        // When
        _dispatcher.OnPost(new RouteMessage(packet1));
        await Task.Delay(100);
        _dispatcher.OnPost(new RouteMessage(packet2));
        await Task.Delay(100);

        // Then
        DisposableReplyStage.OnPostCreateCallCount.Should().Be(1, "최초 생성에서만 OnPostCreate가 호출되어야 함");
        DisposableReplyStage.ReplyDisposeCallCount.Should().Be(1, "최초 생성 시 반환된 reply 패킷은 Dispose되어야 함");
    }

    [Fact(DisplayName = "Post(GetOrCreateStageReq) - OnPostCreate 실패 후 재시도 시 생성 완료된다")]
    public async Task Post_GetOrCreateStageReq_OnPostCreateFailure_AllowsRetry()
    {
        // Given
        FailFirstPostCreateStage.ResetCounters();
        const string stageId = "132";
        var packet1 = CreateGetOrCreateStagePacket(stageId, "fail_first_postcreate_stage");
        var packet2 = CreateGetOrCreateStagePacket(stageId, "fail_first_postcreate_stage");

        // When
        _dispatcher.OnPost(new RouteMessage(packet1));
        await Task.Delay(100);
        var stageAfterFirstAttempt = _dispatcher.GetStage(stageId);
        var isCreatedAfterFirstAttempt = stageAfterFirstAttempt?.IsCreated;
        _dispatcher.OnPost(new RouteMessage(packet2));
        await Task.Delay(100);
        var stageAfterSecondAttempt = _dispatcher.GetStage(stageId);

        // Then
        stageAfterFirstAttempt.Should().NotBeNull();
        isCreatedAfterFirstAttempt.Should().BeFalse("OnPostCreate 실패 시 생성 상태가 복구되어야 함");
        stageAfterSecondAttempt.Should().NotBeNull();
        stageAfterSecondAttempt!.IsCreated.Should().BeTrue("재시도 시 OnPostCreate까지 완료되어야 함");
        FailFirstPostCreateStage.OnCreateCallCount.Should().Be(2, "첫 실패 후 재시도 시 OnCreate가 다시 호출되어야 함");
        FailFirstPostCreateStage.OnPostCreateCallCount.Should().Be(2, "OnPostCreate는 실패/성공 각각 1회씩 호출되어야 함");
        FailFirstPostCreateStage.ReplyDisposeCallCount.Should().Be(2, "실패/성공 경로 모두 reply 패킷을 Dispose해야 함");
    }

    [Fact(DisplayName = "Post(CreateStageReq) - OnPostCreate 실패 후 동일 StageId 재시도 시 성공한다")]
    public async Task Post_CreateStageReq_OnPostCreateFailure_AllowsRetry()
    {
        // Given
        FailFirstPostCreateStage.ResetCounters();
        const string stageId = "133";
        var packet1 = CreateCreateStagePacket(stageId, "fail_first_postcreate_stage");
        var packet2 = CreateCreateStagePacket(stageId, "fail_first_postcreate_stage");

        // When
        _dispatcher.OnPost(new RouteMessage(packet1));
        await Task.Delay(100);
        var stageAfterFirstAttempt = _dispatcher.GetStage(stageId);
        var isCreatedAfterFirstAttempt = stageAfterFirstAttempt?.IsCreated;
        _dispatcher.OnPost(new RouteMessage(packet2));
        await Task.Delay(100);
        var stageAfterSecondAttempt = _dispatcher.GetStage(stageId);

        // Then
        stageAfterFirstAttempt.Should().NotBeNull();
        isCreatedAfterFirstAttempt.Should().BeFalse("OnPostCreate 실패 시 생성 상태가 복구되어야 함");
        stageAfterSecondAttempt.Should().NotBeNull();
        stageAfterSecondAttempt!.IsCreated.Should().BeTrue("동일 StageId로 재요청 시 생성이 완료되어야 함");
        FailFirstPostCreateStage.OnCreateCallCount.Should().Be(2, "실패 후 재시도 시 OnCreate가 다시 호출되어야 함");
        FailFirstPostCreateStage.OnPostCreateCallCount.Should().Be(2, "실패/성공 경로 모두 OnPostCreate가 호출되어야 함");
    }

    [Fact(DisplayName = "여러 Stage 생성 - 각각 독립적으로 관리된다")]
    public async Task CreateMultipleStages_AllManaged()
    {
        // Given (전제조건)
        const int stageCount = 5;

        // When (행동)
        for (int i = 0; i < stageCount; i++)
        {
            var packet = CreateCreateStagePacket((100 + i).ToString(), "test_stage");
            _dispatcher.OnPost(new RouteMessage(packet));
        }
        await Task.Delay(200);

        // Then (결과)
        _dispatcher.StageCount.Should().Be(stageCount, $"{stageCount}개의 Stage가 있어야 함");
    }

    #region Helper Methods

    private static RoutePacket CreateCreateStagePacket(string stageId, string stageType)
    {
        var createReq = new CreateStageReq { StageType = stageType };
        var header = new RouteHeader
        {
            ServiceId = 1,
            MsgId = nameof(CreateStageReq),
            StageId = stageId,
            From = "test:1"
        };

        return RoutePacket.Of(header, createReq.ToByteArray());
    }

    private static RoutePacket CreateTestPacket(string stageId, string msgId, ushort msgSeq = 0)
    {
        var header = new RouteHeader
        {
            ServiceId = 1,
            MsgId = msgId,
            StageId = stageId,
            From = "test:1",
            MsgSeq = msgSeq
        };

        return RoutePacket.Of(header, Array.Empty<byte>());
    }

    private static RoutePacket CreateGetOrCreateStagePacket(string stageId, string stageType)
    {
        var req = new GetOrCreateStageReq
        {
            StageType = stageType,
            CreatePayloadId = "Init"
        };

        var header = new RouteHeader
        {
            ServiceId = 1,
            MsgId = nameof(GetOrCreateStageReq),
            StageId = stageId,
            From = "test:1"
        };

        return RoutePacket.Of(header, req.ToByteArray());
    }

    #endregion
}
