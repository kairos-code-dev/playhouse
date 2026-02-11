#nullable enable

using PlayHouse.Abstractions;
using PlayHouse.Core.Messaging;
using PlayHouse.Core.Play.Base;
using PlayHouse.Core.Shared;
using PlayHouse.Runtime.ServerMesh.Communicator;
using PlayHouse.Runtime.ServerMesh.Discovery;
using PlayHouse.Runtime.Proto;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Play;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using NSubstitute;
using FluentAssertions;

namespace PlayHouse.Unit.Core.Play;

/// <summary>
/// 단위 테스트: BaseStage의 이벤트 루프 및 Actor 관리 기능 검증
/// </summary>
public class BaseStageTests
{
    #region Fake Implementations

    private class FakeStage : IStage
    {
        public IStageLink StageLink { get; }
        public int OnCreateCallCount { get; private set; }
        public int OnDestroyCallCount { get; private set; }
        public int OnDispatchActorCallCount { get; private set; }
        public int OnDispatchCallCount { get; private set; }
        public List<IPacket> ReceivedPackets { get; } = new();

        public FakeStage(IStageLink stageLink)
        {
            StageLink = stageLink;
        }

        public Task<(bool result, IPacket reply)> OnCreate(IPacket packet)
        {
            OnCreateCallCount++;
            return Task.FromResult<(bool result, IPacket reply)>((true, CPacket.Empty("CreateReply")));
        }

        public Task OnPostCreate() => Task.CompletedTask;

        public Task OnDestroy()
        {
            OnDestroyCallCount++;
            return Task.CompletedTask;
        }

        public Task<bool> OnJoinStage(IActor actor) => Task.FromResult(true);
        public Task OnPostJoinStage(IActor actor) => Task.CompletedTask;
        public ValueTask OnConnectionChanged(IActor actor, bool isConnected) => ValueTask.CompletedTask;

        public Task OnDispatch(IActor actor, IPacket packet)
        {
            OnDispatchActorCallCount++;
            ReceivedPackets.Add(packet);
            return Task.CompletedTask;
        }

        public Task OnDispatch(IPacket packet)
        {
            OnDispatchCallCount++;
            ReceivedPackets.Add(packet);
            return Task.CompletedTask;
        }
    }

    private class FakeActor : IActor
    {
        public IActorLink ActorLink { get; }
        public int OnDestroyCallCount { get; private set; }

        public FakeActor(IActorLink actorLink)
        {
            ActorLink = actorLink;
        }

        public Task OnCreate() => Task.CompletedTask;
        public Task OnDestroy()
        {
            OnDestroyCallCount++;
            return Task.CompletedTask;
        }
        public Task<(bool result, IPacket? reply)> OnAuthenticate(IPacket authPacket)
        {
            ActorLink.SetAuthContext("1", 1L);
            return Task.FromResult<(bool, IPacket?)>((true, null));
        }
        public Task OnPostAuthenticate() => Task.CompletedTask;
    }

    #endregion

    private (BaseStage baseStage, FakeStage fakeStage, XStageLink stageSender) CreateTestStage()
    {
        var communicator = Substitute.For<IClientCommunicator>();
        var requestCache = new RequestCache(NullLogger<RequestCache>.Instance);
        var dispatcher = Substitute.For<IPlayDispatcher>();
        var replyRegistry = Substitute.For<IReplyPacketRegistry>();
        var serverInfoCenter = Substitute.For<IServerInfoCenter>();

        var stageSender = new XStageLink(
            communicator,
            requestCache,
            serverInfoCenter,
            ServerType.Play,
            1,
            "play-1",
            100,
            dispatcher,
            replyRegistry);

        stageSender.SetStageType("test_stage");

        var fakeStage = new FakeStage(stageSender);
        var logger = Substitute.For<ILogger>();
        var cmdHandler = new BaseStageCmdHandler(logger);
        var serviceScope = Substitute.For<IServiceScope>();
        var baseStage = new BaseStage(fakeStage, stageSender, logger, cmdHandler, serviceScope);

        return (baseStage, fakeStage, stageSender);
    }

    [Fact(DisplayName = "StageId는 StageSender에서 가져온다")]
    public void StageId_ReturnsFromStageSender()
    {
        // Given (전제조건)
        var (baseStage, _, stageSender) = CreateTestStage();

        // When (행동)
        var stageId = baseStage.StageId;

        // Then (결과)
        stageId.Should().Be(stageSender.StageId, "StageId는 StageSender에서 가져와야 함");
    }

    [Fact(DisplayName = "StageType은 StageSender에서 가져온다")]
    public void StageType_ReturnsFromStageSender()
    {
        // Given (전제조건)
        var (baseStage, _, _) = CreateTestStage();

        // When (행동)
        var stageType = baseStage.StageType;

        // Then (결과)
        stageType.Should().Be("test_stage");
    }

    [Fact(DisplayName = "IsCreated - 초기값은 false이다")]
    public void IsCreated_Initially_IsFalse()
    {
        // Given (전제조건)
        var (baseStage, _, _) = CreateTestStage();

        // When (행동)
        var isCreated = baseStage.IsCreated;

        // Then (결과)
        isCreated.Should().BeFalse("초기에는 생성되지 않은 상태여야 함");
    }

    [Fact(DisplayName = "MarkAsCreated - IsCreated를 true로 설정한다")]
    public void MarkAsCreated_SetsIsCreatedToTrue()
    {
        // Given (전제조건)
        var (baseStage, _, _) = CreateTestStage();

        // When (행동)
        baseStage.MarkAsCreated();

        // Then (결과)
        baseStage.IsCreated.Should().BeTrue("MarkAsCreated 호출 후 IsCreated는 true여야 함");
    }

    [Fact(DisplayName = "ActorCount - 초기값은 0이다")]
    public void ActorCount_Initially_IsZero()
    {
        // Given (전제조건)
        var (baseStage, _, _) = CreateTestStage();

        // When (행동)
        var count = baseStage.ActorCount;

        // Then (결과)
        count.Should().Be(0, "초기 Actor 수는 0이어야 함");
    }

    [Fact(DisplayName = "AddActor - Actor를 추가한다")]
    public void AddActor_AddsActorToStage()
    {
        // Given (전제조건)
        var (baseStage, _, _) = CreateTestStage();
        var actorSender = new XActorLink("1", 1, "1", baseStage);
        var actor = new BaseActor(new FakeActor(actorSender), actorSender);

        // When (행동)
        baseStage.AddActor(actor);

        // Then (결과)
        baseStage.ActorCount.Should().Be(1);
        baseStage.GetActor(actor.AccountId).Should().Be(actor);
    }

    [Fact(DisplayName = "RemoveActor - Actor를 제거한다")]
    public void RemoveActor_RemovesActorFromStage()
    {
        // Given (전제조건)
        var (baseStage, _, _) = CreateTestStage();
        var actorSender = new XActorLink("1", 1, "1", baseStage);
        var actor = new BaseActor(new FakeActor(actorSender), actorSender);
        baseStage.AddActor(actor);

        // When (행동)
        var result = baseStage.RemoveActor(actor.AccountId);

        // Then (결과)
        result.Should().BeTrue();
        baseStage.ActorCount.Should().Be(0);
    }

    [Fact(DisplayName = "PostDestroy - Stage의 OnDestroy가 호출된다")]
    public async Task PostDestroy_CallsStageOnDestroy()
    {
        // Given (전제조건)
        var (baseStage, fakeStage, _) = CreateTestStage();

        // When (행동)
        baseStage.PostDestroy();
        await Task.Delay(200); // 작업 완료 대기

        // Then (결과)
        fakeStage.OnDestroyCallCount.Should().Be(1, "OnDestroy가 호출되어야 함");
    }

    [Fact(DisplayName = "PostDestroy - 모든 Actor의 OnDestroy가 호출된다")]
    public async Task PostDestroy_CallsAllActorOnDestroy()
    {
        // Given (전제조건)
        var (baseStage, _, _) = CreateTestStage();
        var actorSender = new XActorLink("1", 1, "1", baseStage);
        var fakeActor = new FakeActor(actorSender);
        var actor = new BaseActor(fakeActor, actorSender);
        baseStage.AddActor(actor);

        // When (행동)
        baseStage.PostDestroy();
        await Task.Delay(200); // 작업 완료 대기

        // Then (결과)
        fakeActor.OnDestroyCallCount.Should().Be(1, "모든 Actor의 OnDestroy가 호출되어야 함");
        baseStage.ActorCount.Should().Be(0, "Stage의 Actor 목록이 비워져야 함");
    }

    [Fact(DisplayName = "Reply - StageSender의 Reply를 호출한다")]
    public void Reply_CallsStageSenderReply()
    {
        // Given (전제조건)
        var (baseStage, _, stageSender) = CreateTestStage();
        
        // Reply requires a current header context
        var header = new RouteHeader { MsgSeq = 1, From = "test" };
        stageSender.SetCurrentHeader(header);

        // When (행동)
        var act = () => baseStage.Reply(0);

        // Then (결과)
        act.Should().NotThrow();
    }
}