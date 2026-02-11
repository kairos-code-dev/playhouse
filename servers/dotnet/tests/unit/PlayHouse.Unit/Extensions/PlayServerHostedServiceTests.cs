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
/// PlayServerHostedService의 단위 테스트
/// PlayServer/ApiServer는 sealed 클래스이므로 Mock 불가
/// 실제 서버 인스턴스를 사용하는 통합 테스트
/// </summary>
public class PlayServerHostedServiceTests
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
            ActorLink.SetAuthContext("1", 1L);
            return Task.FromResult<(bool, IPacket?)>((true, null));
        }
        public Task OnPostAuthenticate() => Task.CompletedTask;
    }

    [Fact(DisplayName = "생성자 - PlayServer를 받아 HostedService를 생성한다")]
    public void Constructor_AcceptsPlayServer()
    {
        // Given
        var services = new ServiceCollection();
        services.AddLogging();
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
        var logger = serviceProvider.GetRequiredService<ILogger<PlayServerHostedService>>();

        // When
        var hostedService = new PlayServerHostedService(playServer, logger);

        // Then
        hostedService.Should().NotBeNull("HostedService가 생성되어야 함");
    }

    [Fact(DisplayName = "StartAsync와 StopAsync - 정상적으로 서버 생명주기를 관리한다")]
    public async Task StartAndStop_ManagesServerLifecycle()
    {
        // Given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPlayServer(options =>
        {
            options.ServerType = ServerType.Play;
            options.TcpPort = 0; // 랜덤 포트
            options.AuthenticateMessageId = "Auth";
        })
        .UseStage<TestStage, TestActor>("TestStage")
        .UseSystemController<TestSystemController>();

        var serviceProvider = services.BuildServiceProvider();
        var playServer = serviceProvider.GetRequiredService<PlayServer>();
        var logger = serviceProvider.GetRequiredService<ILogger<PlayServerHostedService>>();
        var hostedService = new PlayServerHostedService(playServer, logger);

        try
        {
            // When - Start
            await hostedService.StartAsync(CancellationToken.None);

            // Then - 서버가 시작되어야 함
            playServer.ActualTcpPort.Should().BeGreaterThan(0, "서버가 시작되면 포트가 할당되어야 함");

            // When - Stop
            await hostedService.StopAsync(CancellationToken.None);

            // Then - 정상적으로 종료되어야 함 (예외 없음)
        }
        finally
        {
            // Cleanup
            await playServer.DisposeAsync();
        }
    }
}
