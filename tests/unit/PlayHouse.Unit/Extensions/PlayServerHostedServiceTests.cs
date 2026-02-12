#nullable enable

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Abstractions.System;
using PlayHouse.Connector;
using PlayHouse.Core.Play.Bootstrap;
using PlayHouse.Core.Shared;
using PlayHouse.Extensions;
using PlayHouse.Runtime.ServerMesh.Discovery;
using Xunit;
using ClientConnector = PlayHouse.Connector.Connector;
using ClientPacket = PlayHouse.Connector.Protocol.Packet;

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
        internal static int OnCreateCount;
        internal static int OnPostCreateCount;

        internal static void ResetCounters()
        {
            OnCreateCount = 0;
            OnPostCreateCount = 0;
        }

        public TestStage(IStageLink stageLink)
        {
            StageLink = stageLink;
        }

        public IStageLink StageLink { get; }
        public Task<(bool result, IPacket reply)> OnCreate(IPacket packet)
        {
            Interlocked.Increment(ref OnCreateCount);
            return Task.FromResult<(bool result, IPacket reply)>((true, CPacket.Empty("TestReply")));
        }

        public Task OnPostCreate()
        {
            Interlocked.Increment(ref OnPostCreateCount);
            return Task.CompletedTask;
        }
        public Task OnDestroy() => Task.CompletedTask;
        public Task<bool> OnJoinStage(IActor actor) => Task.FromResult(true);
        public Task OnPostJoinStage(IActor actor) => Task.CompletedTask;
        public ValueTask OnConnectionChanged(IActor actor, bool isConnected) => ValueTask.CompletedTask;
        public Task OnDispatch(IActor actor, IPacket packet) => Task.CompletedTask;
        public Task OnDispatch(IPacket packet) => Task.CompletedTask;
    }

    private class FailFirstPostCreateTestStage : IStage
    {
        private static int _shouldFailPostCreate;
        internal static int OnCreateCount;
        internal static int OnPostCreateCount;

        internal static void ResetCounters()
        {
            _shouldFailPostCreate = 1;
            OnCreateCount = 0;
            OnPostCreateCount = 0;
        }

        public FailFirstPostCreateTestStage(IStageLink stageLink)
        {
            StageLink = stageLink;
        }

        public IStageLink StageLink { get; }

        public Task<(bool result, IPacket reply)> OnCreate(IPacket packet)
        {
            Interlocked.Increment(ref OnCreateCount);
            return Task.FromResult<(bool result, IPacket reply)>((true, CPacket.Empty("FailFirstPostCreateReply")));
        }

        public Task OnPostCreate()
        {
            Interlocked.Increment(ref OnPostCreateCount);
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

    private class TestActor : IActor
    {
        public TestActor(IActorLink actorLink)
        {
            ActorLink = actorLink;
        }

        public IActorLink ActorLink { get; }
        public Task OnCreate() => Task.CompletedTask;
        public Task OnDestroy() => Task.CompletedTask;
        public Task<(bool result, IPacket? reply)> OnAuthenticate(IPacket authPacket)
        {
            ActorLink.SetAuthSingleContext("1", "TestStage");
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
        .UseStage<TestStage, TestActor>("TestStage", StageMode.Single)
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
            options.DefaultStageType = "TestStage";
        })
        .UseStage<TestStage, TestActor>("TestStage", StageMode.Single)
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

    [Fact(DisplayName = "CreateStageIfNotExists - OnCreate/OnPostCreate 완료를 보장한다")]
    public async Task CreateStageIfNotExists_EnsuresStageCreationCompleted()
    {
        // Given
        TestStage.ResetCounters();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPlayServer(options =>
        {
            options.ServerType = ServerType.Play;
            options.TcpPort = 0; // 랜덤 포트
            options.AuthenticateMessageId = "Auth";
            options.DefaultStageType = "TestStage";
        })
        .UseStage<TestStage, TestActor>("TestStage", StageMode.Single)
        .UseSystemController<TestSystemController>();

        var serviceProvider = services.BuildServiceProvider();
        var playServer = serviceProvider.GetRequiredService<PlayServer>();
        var logger = serviceProvider.GetRequiredService<ILogger<PlayServerHostedService>>();
        var hostedService = new PlayServerHostedService(playServer, logger);

        try
        {
            await hostedService.StartAsync(CancellationToken.None);

            // When
            var created = playServer.CreateStageIfNotExists("42", "TestStage");
            var createdAgain = playServer.CreateStageIfNotExists("42", "TestStage");

            // Then
            created.Should().BeTrue();
            createdAgain.Should().BeTrue();
            TestStage.OnCreateCount.Should().Be(1, "같은 Stage는 한 번만 생성되어야 함");
            TestStage.OnPostCreateCount.Should().Be(1, "OnPostCreate도 한 번만 호출되어야 함");
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
            await playServer.DisposeAsync();
        }
    }

    [Fact(DisplayName = "인증 경로 - Stage 생성 시 OnCreate/OnPostCreate 완료를 보장한다")]
    public async Task AuthenticatePath_EnsuresStageCreationCompleted()
    {
        // Given
        TestStage.ResetCounters();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPlayServer(options =>
        {
            options.ServerType = ServerType.Play;
            options.TcpPort = 0; // 랜덤 포트
            options.AuthenticateMessageId = "Auth";
            options.DefaultStageType = "TestStage";
        })
        .UseStage<TestStage, TestActor>("TestStage", StageMode.Single)
        .UseSystemController<TestSystemController>();

        var serviceProvider = services.BuildServiceProvider();
        var playServer = serviceProvider.GetRequiredService<PlayServer>();
        var logger = serviceProvider.GetRequiredService<ILogger<PlayServerHostedService>>();
        var hostedService = new PlayServerHostedService(playServer, logger);

        var connector = new ClientConnector();
        connector.Init(new ConnectorConfig
        {
            RequestTimeoutMs = 5000,
            HeartBeatIntervalMs = 0,
            HeartbeatTimeoutMs = 0,
            ConnectionIdleTimeoutMs = 5000
        });

        try
        {
            await hostedService.StartAsync(CancellationToken.None);

            // When
            var connected = await connector.ConnectAsync("127.0.0.1", playServer.ActualTcpPort);
            connected.Should().BeTrue("테스트 서버에 연결되어야 함");

            using var authReq = ClientPacket.Empty("Auth");
            using var authRes = await connector.AuthenticateAsync(authReq);

            // Then
            connector.IsAuthenticated().Should().BeTrue("인증이 성공해야 함");
            TestStage.OnCreateCount.Should().Be(1, "인증으로 Stage가 처음 생성될 때 OnCreate가 호출되어야 함");
            TestStage.OnPostCreateCount.Should().Be(1, "인증으로 Stage가 처음 생성될 때 OnPostCreate도 호출되어야 함");
        }
        finally
        {
            await connector.DisposeAsync();
            await hostedService.StopAsync(CancellationToken.None);
            await playServer.DisposeAsync();
        }
    }

    [Fact(DisplayName = "CreateStageIfNotExists - OnPostCreate 실패 시 false 반환 후 재시도 성공")]
    public async Task CreateStageIfNotExists_OnPostCreateFailure_ReturnsFalseThenSucceedsOnRetry()
    {
        // Given
        FailFirstPostCreateTestStage.ResetCounters();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPlayServer(options =>
        {
            options.ServerType = ServerType.Play;
            options.TcpPort = 0;
            options.AuthenticateMessageId = "Auth";
            options.DefaultStageType = "FailFirstPostCreateStage";
        })
        .UseStage<FailFirstPostCreateTestStage, TestActor>("FailFirstPostCreateStage")
        .UseSystemController<TestSystemController>();

        var serviceProvider = services.BuildServiceProvider();
        var playServer = serviceProvider.GetRequiredService<PlayServer>();
        var logger = serviceProvider.GetRequiredService<ILogger<PlayServerHostedService>>();
        var hostedService = new PlayServerHostedService(playServer, logger);

        try
        {
            await hostedService.StartAsync(CancellationToken.None);

            // When
            var firstAttempt = await playServer.CreateStageIfNotExistsAsync("84", "FailFirstPostCreateStage");
            var secondAttempt = await playServer.CreateStageIfNotExistsAsync("84", "FailFirstPostCreateStage");

            // Then
            firstAttempt.Should().BeFalse("OnPostCreate 실패 시 생성 완료를 보장할 수 없으므로 false를 반환해야 함");
            secondAttempt.Should().BeTrue("재시도에서 OnPostCreate가 성공하면 true를 반환해야 함");
            FailFirstPostCreateTestStage.OnCreateCount.Should().Be(2, "실패 후 재시도 시 OnCreate가 다시 호출되어야 함");
            FailFirstPostCreateTestStage.OnPostCreateCount.Should().Be(2, "OnPostCreate는 실패/성공 각각 1회씩 호출되어야 함");
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
            await playServer.DisposeAsync();
        }
    }
}
