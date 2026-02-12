#nullable enable
#pragma warning disable CS0618 // Type or member is obsolete - 테스트에서 의도적으로 obsolete 메서드 테스트

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Core.Shared;
using Xunit;
using NSubstitute;

namespace PlayHouse.Unit.Abstractions.Play;

/// <summary>
/// 단위 테스트: PlayProducer의 Stage/Actor 팩토리 기능 검증
/// </summary>
public class PlayProducerTests
{
    /// <summary>
    /// Factory registration 테스트를 위한 PlayProducer 생성.
    /// DI를 사용하지 않는 경우에도 ServiceProvider는 필수이므로 빈 ServiceProvider를 전달.
    /// </summary>
    private static PlayProducer CreateProducerForManualRegistration()
    {
        var emptyServiceProvider = new ServiceCollection().BuildServiceProvider();
        return new PlayProducer(
            new Dictionary<string, Type>(),
            new Dictionary<string, Type>(),
            emptyServiceProvider);
    }

    #region Fake Implementations

    private class FakeStage : IStage
    {
        public IStageLink StageLink { get; }
        public string StageType { get; }

        public FakeStage(IStageLink stageLink, string stageType = "test")
        {
            StageLink = stageLink;
            StageType = stageType;
        }

        public Task<(bool result, IPacket reply)> OnCreate(IPacket packet)
            => Task.FromResult<(bool result, IPacket reply)>((true, CPacket.Empty("CreateReply")));

        public Task OnPostCreate() => Task.CompletedTask;
        public Task OnDestroy() => Task.CompletedTask;
        public Task<bool> OnJoinStage(IActor actor) => Task.FromResult(true);
        public Task OnPostJoinStage(IActor actor) => Task.CompletedTask;
        public ValueTask OnConnectionChanged(IActor actor, bool isConnected) => ValueTask.CompletedTask;
        public Task OnDispatch(IActor actor, IPacket packet) => Task.CompletedTask;
        public Task OnDispatch(IPacket packet) => Task.CompletedTask;
    }

    private class FakeActor : IActor
    {
        public IActorLink ActorLink { get; }
        public string ActorType { get; }

        public FakeActor(IActorLink actorLink, string actorType = "test")
        {
            ActorLink = actorLink;
            ActorType = actorType;
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

    #endregion

    [Fact(DisplayName = "Register - Stage와 Actor 팩토리를 등록할 수 있다")]
    public void Register_StageAndActorFactories_SuccessfullyRegistered()
    {
        // Given (전제조건)
        var producer = CreateProducerForManualRegistration();
        const string stageType = "game_room";

        // When (행동)
        producer.Register(
            stageType,
            stageSender => new FakeStage(stageSender, stageType),
            actorSender => new FakeActor(actorSender, stageType));

        // Then (결과)
        producer.IsValidType(stageType).Should().BeTrue("등록된 타입은 유효해야 함");
    }

    [Fact(DisplayName = "Register - 동일한 타입 중복 등록 시 예외를 발생한다")]
    public void Register_DuplicateType_ThrowsException()
    {
        // Given (전제조건)
        var producer = CreateProducerForManualRegistration();
        const string stageType = "game_room";
        producer.Register(
            stageType,
            stageSender => new FakeStage(stageSender),
            actorSender => new FakeActor(actorSender));

        // When (행동)
        var action = () => producer.Register(
            stageType,
            stageSender => new FakeStage(stageSender),
            actorSender => new FakeActor(actorSender));

        // Then (결과)
        action.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{stageType}*", "중복 등록 시 예외 메시지에 타입명이 포함되어야 함");
    }

    [Fact(DisplayName = "IsValidType - 등록되지 않은 타입은 false를 반환한다")]
    public void IsValidType_UnregisteredType_ReturnsFalse()
    {
        // Given (전제조건)
        var producer = CreateProducerForManualRegistration();
        const string stageType = "non_existent";

        // When (행동)
        var result = producer.IsValidType(stageType);

        // Then (결과)
        result.Should().BeFalse("등록되지 않은 타입은 유효하지 않음");
    }

    [Fact(DisplayName = "GetStage - 등록된 팩토리로 Stage 인스턴스를 생성한다")]
    public void GetStage_RegisteredType_CreatesInstance()
    {
        // Given (전제조건)
        var producer = CreateProducerForManualRegistration();
        const string stageType = "game_room";
        var stageSender = Substitute.For<IStageLink>();

        producer.Register(
            stageType,
            sender => new FakeStage(sender, stageType),
            actorSender => new FakeActor(actorSender));

        // When (행동)
        var stage = producer.GetStage(stageType, stageSender);

        // Then (결과)
        stage.Should().NotBeNull("Stage 인스턴스가 생성되어야 함");
        stage.Should().BeOfType<FakeStage>();
        (stage as FakeStage)!.StageType.Should().Be(stageType);
        stage.StageLink.Should().BeSameAs(stageSender);
    }

    [Fact(DisplayName = "GetStage - 등록되지 않은 타입은 예외를 발생한다")]
    public void GetStage_UnregisteredType_ThrowsException()
    {
        // Given (전제조건)
        var producer = CreateProducerForManualRegistration();
        var stageSender = Substitute.For<IStageLink>();
        const string stageType = "non_existent";

        // When (행동)
        var action = () => producer.GetStage(stageType, stageSender);

        // Then (결과)
        action.Should().Throw<KeyNotFoundException>()
            .WithMessage($"*{stageType}*");
    }

    [Fact(DisplayName = "GetActor - 등록된 팩토리로 Actor 인스턴스를 생성한다")]
    public void GetActor_RegisteredType_CreatesInstance()
    {
        // Given (전제조건)
        var producer = CreateProducerForManualRegistration();
        const string stageType = "game_room";
        var actorSender = Substitute.For<IActorLink>();

        producer.Register(
            stageType,
            stageSender => new FakeStage(stageSender),
            sender => new FakeActor(sender, stageType));

        // When (행동)
        var actor = producer.GetActor(stageType, actorSender);

        // Then (결과)
        actor.Should().NotBeNull("Actor 인스턴스가 생성되어야 함");
        actor.Should().BeOfType<FakeActor>();
        (actor as FakeActor)!.ActorType.Should().Be(stageType);
        actor.ActorLink.Should().BeSameAs(actorSender);
    }

    [Fact(DisplayName = "GetActor - 등록되지 않은 타입은 예외를 발생한다")]
    public void GetActor_UnregisteredType_ThrowsException()
    {
        // Given (전제조건)
        var producer = CreateProducerForManualRegistration();
        var actorSender = Substitute.For<IActorLink>();
        const string stageType = "non_existent";

        // When (행동)
        var action = () => producer.GetActor(stageType, actorSender);

        // Then (결과)
        action.Should().Throw<KeyNotFoundException>()
            .WithMessage($"*{stageType}*");
    }

    [Fact(DisplayName = "여러 타입 등록 - 각각 독립적으로 인스턴스를 생성한다")]
    public void Register_MultipleTypes_CreatesSeparateInstances()
    {
        // Given (전제조건)
        var producer = CreateProducerForManualRegistration();
        const string type1 = "game_room";
        const string type2 = "lobby";

        producer.Register(
            type1,
            sender => new FakeStage(sender, type1),
            sender => new FakeActor(sender, type1));

        producer.Register(
            type2,
            sender => new FakeStage(sender, type2),
            sender => new FakeActor(sender, type2));

        var stageSender1 = Substitute.For<IStageLink>();
        var stageSender2 = Substitute.For<IStageLink>();

        // When (행동)
        var stage1 = producer.GetStage(type1, stageSender1);
        var stage2 = producer.GetStage(type2, stageSender2);

        // Then (결과)
        (stage1 as FakeStage)!.StageType.Should().Be(type1);
        (stage2 as FakeStage)!.StageType.Should().Be(type2);
        stage1.Should().NotBeSameAs(stage2);
    }

    [Fact(DisplayName = "GetStage - 매 호출마다 새 인스턴스를 생성한다")]
    public void GetStage_MultipleCalls_CreatesNewInstances()
    {
        // Given (전제조건)
        var producer = CreateProducerForManualRegistration();
        const string stageType = "game_room";

        producer.Register(
            stageType,
            sender => new FakeStage(sender),
            sender => new FakeActor(sender));

        var stageSender = Substitute.For<IStageLink>();

        // When (행동)
        var stage1 = producer.GetStage(stageType, stageSender);
        var stage2 = producer.GetStage(stageType, stageSender);

        // Then (결과)
        stage1.Should().NotBeSameAs(stage2, "매번 새 인스턴스가 생성되어야 함");
    }
}
