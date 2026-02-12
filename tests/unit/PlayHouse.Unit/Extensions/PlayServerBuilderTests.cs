#nullable enable

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Abstractions.System;
using PlayHouse.Core.Play.Bootstrap;
using PlayHouse.Core.Shared;
using PlayHouse.Extensions;
using PlayHouse.Runtime.ServerMesh.Discovery;
using Xunit;

namespace PlayHouse.Unit.Extensions;

/// <summary>
/// PlayServerBuilder의 단위 테스트
/// </summary>
public class PlayServerBuilderTests
{
    private class TestStage : IStage
    {
        public IStageLink StageLink { get; private set; } = null!;
        public Task<(bool result, IPacket reply)> OnCreate(IPacket packet) =>
            Task.FromResult<(bool result, IPacket reply)>((true, CPacket.Empty("TestReply")));
        public Task OnPostCreate() => Task.CompletedTask;
        public Task OnDestroy() => Task.CompletedTask;
        public Task<bool> OnJoinStage(IActor actor) => Task.FromResult(true);
        public Task OnPostJoinStage(IActor actor) => Task.CompletedTask;
        public ValueTask OnConnectionChanged(IActor actor, bool isConnected) => ValueTask.CompletedTask;
        public Task OnDispatch(IActor actor, IPacket packet) => Task.CompletedTask;
        public Task OnDispatch(IPacket packet) => Task.CompletedTask;
    }

    private class TestActor : IActor
    {
        public IActorLink ActorLink { get; private set; } = null!;
        public Task OnCreate() => Task.CompletedTask;
        public Task OnDestroy() => Task.CompletedTask;
        public Task<(bool result, IPacket? reply)> OnAuthenticate(IPacket authPacket)
        {
            ActorLink.SetAuthContext("1", "1");
            return Task.FromResult<(bool, IPacket?)>((true, null));
        }
        public Task OnPostAuthenticate() => Task.CompletedTask;
    }

    [Fact(DisplayName = "AddPlayServer - PlayServer가 싱글톤으로 등록된다")]
    public void AddPlayServer_RegistersPlayServerAsSingleton()
    {
        // Given
        var services = new ServiceCollection();
        services.AddLogging();

        // When
        services.AddPlayServer(options =>
        {
            options.ServerType = ServerType.Play;
            options.TcpPort = 0; // 랜덤 포트
            options.AuthenticateMessageId = "Auth";
        })
        .UseSystemController<TestSystemController>();

        var serviceProvider = services.BuildServiceProvider();

        // Then
        var playServer1 = serviceProvider.GetRequiredService<PlayServer>();
        var playServer2 = serviceProvider.GetRequiredService<PlayServer>();

        playServer1.Should().NotBeNull("PlayServer가 등록되어야 함");
        playServer1.Should().BeSameAs(playServer2, "싱글톤으로 등록되어 같은 인스턴스여야 함");
    }

    [Fact(DisplayName = "AddPlayServer - IPlayServerControl 인터페이스로 해결 가능하다")]
    public void AddPlayServer_RegistersIPlayServerControl()
    {
        // Given
        var services = new ServiceCollection();
        services.AddLogging();

        // When
        services.AddPlayServer(options =>
        {
            options.ServerType = ServerType.Play;
            options.TcpPort = 0;
            options.AuthenticateMessageId = "Auth";
        })
        .UseSystemController<TestSystemController>();

        var serviceProvider = services.BuildServiceProvider();

        // Then
        var playServerControl = serviceProvider.GetRequiredService<IPlayServerControl>();
        var playServer = serviceProvider.GetRequiredService<PlayServer>();

        playServerControl.Should().NotBeNull("IPlayServerControl이 등록되어야 함");
        playServerControl.Should().BeSameAs(playServer, "IPlayServerControl이 PlayServer와 같은 인스턴스여야 함");
    }

    [Fact(DisplayName = "UseStage - 스테이지와 액터 타입이 등록되고 생성할 수 있다")]
    public void UseStage_RegistersStageAndActorTypes()
    {
        // Given
        var services = new ServiceCollection();
        services.AddLogging();

        // When
        services.AddPlayServer(options =>
        {
            options.ServerType = ServerType.Play;
            options.TcpPort = 0;
            options.AuthenticateMessageId = "Auth";
        })
        .UseStage<TestStage, TestActor>("TestStage")
        .UseSystemController<TestSystemController>();

        var serviceProvider = services.BuildServiceProvider();
        var playServer = serviceProvider.GetRequiredService<PlayServer>();

        // Then
        playServer.Should().NotBeNull("PlayServer가 등록되어야 함");
        // Note: 실제 Stage/Actor 생성 검증은 E2E 테스트에서 수행
        // 여기서는 빌더 체인이 정상 동작하는지만 확인
    }

    [Fact(DisplayName = "빌더 체인 - 여러 UseStage를 연속 호출할 수 있다")]
    public void BuilderChain_CanCallMultipleUseStage()
    {
        // Given
        var services = new ServiceCollection();
        services.AddLogging();

        // When
        var builder = services.AddPlayServer(options =>
        {
            options.ServerType = ServerType.Play;
            options.TcpPort = 0;
            options.AuthenticateMessageId = "Auth";
        })
        .UseStage<TestStage, TestActor>("TestStage1")
        .UseStage<TestStage, TestActor>("TestStage2")
        .UseSystemController<TestSystemController>();

        var serviceProvider = services.BuildServiceProvider();
        var playServer = serviceProvider.GetRequiredService<PlayServer>();

        // Then
        builder.Should().NotBeNull("빌더가 반환되어야 함");
        playServer.Should().NotBeNull("PlayServer가 등록되어야 함");
    }
}
