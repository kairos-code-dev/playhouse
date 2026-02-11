#nullable enable

using Microsoft.Extensions.DependencyInjection;
using PlayHouse.Abstractions.Api;
using PlayHouse.Abstractions.Play;
using PlayHouse.Abstractions.System;

namespace PlayHouse.Extensions;

/// <summary>
/// PlayServer 구성 빌더 인터페이스.
/// </summary>
public interface IPlayServerBuilder
{
    IServiceCollection Services { get; }
    IPlayServerBuilder UseStage<TStage, TActor>(string stageType, StageMode mode = StageMode.Multi)
        where TStage : class, IStage
        where TActor : class, IActor;

    /// <summary>
    /// System Controller를 등록합니다.
    /// </summary>
    /// <typeparam name="T">ISystemController 구현 타입.</typeparam>
    /// <returns>빌더 인스턴스.</returns>
    /// <remarks>
    /// 이 메서드는 필수입니다. PlayServer를 빌드하기 전에 반드시 호출해야 합니다.
    /// </remarks>
    IPlayServerBuilder UseSystemController<T>() where T : class, ISystemController;

    /// <summary>
    /// System Controller 인스턴스를 등록합니다.
    /// </summary>
    /// <param name="controller">ISystemController 인스턴스.</param>
    /// <returns>빌더 인스턴스.</returns>
    /// <remarks>
    /// 이 메서드는 필수입니다. PlayServer를 빌드하기 전에 반드시 호출해야 합니다.
    /// </remarks>
    IPlayServerBuilder UseSystemController(ISystemController controller);
}

/// <summary>
/// ApiServer 구성 빌더 인터페이스.
/// </summary>
public interface IApiServerBuilder
{
    IServiceCollection Services { get; }
    IApiServerBuilder UseController<T>() where T : class, IApiController;
    IApiServerBuilder UseSystemController<T>() where T : class, ISystemController;
    IApiServerBuilder UseSystemController(ISystemController controller);
}
