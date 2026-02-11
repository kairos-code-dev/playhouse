#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PlayHouse.Abstractions;
using PlayHouse.Abstractions.Play;
using PlayHouse.Abstractions.System;
using PlayHouse.Core.Play.Bootstrap;
using PlayHouse.Runtime.ServerMesh.Discovery;

namespace PlayHouse.Extensions;

internal sealed class PlayServerBuilder : IPlayServerBuilder
{
    private readonly IServiceCollection _services;
    private readonly PlayServerOption _options;
    private readonly Dictionary<string, Type> _stageTypes = new();
    private readonly Dictionary<string, Type> _actorTypes = new();
    private readonly Dictionary<string, StageMode> _stageModes = new();
    private Type _systemControllerType = null!;

    public IServiceCollection Services => _services;

    internal PlayServerBuilder(IServiceCollection services, PlayServerOption options)
    {
        _services = services;
        _options = options;
    }

    public IPlayServerBuilder UseStage<TStage, TActor>(string stageType, StageMode mode = StageMode.Multi)
        where TStage : class, IStage
        where TActor : class, IActor
    {
        _stageTypes[stageType] = typeof(TStage);
        _actorTypes[stageType] = typeof(TActor);
        _stageModes[stageType] = mode;
        return this;
    }

    public IPlayServerBuilder UseSystemController<T>() where T : class, ISystemController
    {
        _systemControllerType = typeof(T);
        _services.AddSingleton<ISystemController, T>();

        // SystemController 등록 후 자동으로 Build 호출
        Build();

        return this;
    }

    public IPlayServerBuilder UseSystemController(ISystemController controller)
    {
        _systemControllerType = controller.GetType();
        _services.AddSingleton<ISystemController>(controller);

        // SystemController 등록 후 자동으로 Build 호출
        Build();

        return this;
    }

    internal void Build()
    {
        // SystemController 필수 검증
        var systemControllerType = _systemControllerType
            ?? throw new InvalidOperationException(
                "SystemController is required. Use UseSystemController<T>() to register.");

        // IServerInfoCenter 싱글턴 등록
        _services.AddSingleton<IServerInfoCenter, XServerInfoCenter>();

        // PlayServer 싱글턴 등록
        _services.AddSingleton<PlayServer>(sp =>
        {
            // ILoggerFactory 필수 검증
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            // ISystemController 필수 검증
            var systemController = sp.GetRequiredService<ISystemController>();

            var producer = new PlayProducer(_stageTypes, _actorTypes, sp, _stageModes);
            return new PlayServer(_options, producer, systemController, loggerFactory);
        });

        // IPlayServerControl로도 접근 가능하도록 등록
        _services.AddSingleton<IPlayServerControl>(sp => sp.GetRequiredService<PlayServer>());
    }
}
