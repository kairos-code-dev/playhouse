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

        public Task OnPostCreate() => Task.CompletedTask;

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
            ActorLink.SetAuthContext("1", 1L);
            return Task.FromResult<(bool, IPacket?)>((true, null));
        }
        public Task OnPostAuthenticate() => Task.CompletedTask;
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
        const long stageId = 100;
        var packet = CreateCreateStagePacket(stageId, "test_stage");

        // When (행동)
        _dispatcher.OnPost(new RouteMessage(packet));
        await Task.Delay(100); // 이벤트 루프 처리 대기

        // Then (결과)
        _dispatcher.StageCount.Should().Be(1, "Stage가 생성되어야 함");
    }

    [Fact(DisplayName = "Post(CreateStageReq) - 이미 존재하는 StageId는 에러를 반환한다")]
    public async Task Post_CreateStageReq_DuplicateStageId_SendsError()
    {
        // Given (전제조건)
        const long stageId = 100;
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
        const long stageId = 100;
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
        const long stageId = 999;
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
        const long stageId = 100;
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
            stageId: 100,
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

        var packet1 = CreateCreateStagePacket(100, "test_stage");
        var packet2 = CreateCreateStagePacket(101, "test_stage");

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

    [Fact(DisplayName = "여러 Stage 생성 - 각각 독립적으로 관리된다")]
    public async Task CreateMultipleStages_AllManaged()
    {
        // Given (전제조건)
        const int stageCount = 5;

        // When (행동)
        for (int i = 0; i < stageCount; i++)
        {
            var packet = CreateCreateStagePacket(100 + i, "test_stage");
            _dispatcher.OnPost(new RouteMessage(packet));
        }
        await Task.Delay(200);

        // Then (결과)
        _dispatcher.StageCount.Should().Be(stageCount, $"{stageCount}개의 Stage가 있어야 함");
    }

    #region Helper Methods

    private static RoutePacket CreateCreateStagePacket(long stageId, string stageType)
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

    private static RoutePacket CreateTestPacket(long stageId, string msgId, ushort msgSeq = 0)
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

    #endregion
}
