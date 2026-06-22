using System.Reflection;
using AuroraStruct3D.OpenCV.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace AuroraStruct3D.OpenCV;

/// <summary>
/// OpenCV 图像处理封装模块，提供服务注册扩展。
/// 将 OpenCvSharp4 的引用从 Application 层解耦，
/// 统一在本项目管理 OpenCV 相关依赖与服务生命周期。
/// </summary>
public static class OpenCVModule
{
    /// <summary>
    /// 注册 OpenCV 图像处理服务及算子注册表。
    /// <para>
    /// 始终扫描本程序集（AuroraStruct3D.OpenCV）；
    /// 通过 <paramref name="additionalAssemblies"/> 可追加外部算子所在的程序集。
    /// </para>
    /// <para>
    /// 宿主项目需提前注册 <c>IDistributedCache</c>（Redis）实现，例如：
    /// <code>
    /// services.AddStackExchangeRedisCache(opt => opt.Configuration = "localhost");
    /// services.AddOpenCVServices();
    /// </code>
    /// </para>
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="additionalAssemblies">
    /// 包含外部算子实现的额外程序集，可传入零个或多个。
    /// </param>
    /// <returns>服务集合（支持链式调用）。</returns>
    public static IServiceCollection AddOpenCVServices(
        this IServiceCollection services,
        params Assembly[] additionalAssemblies
    )
    {
        // 始终包含本程序集，合并外部传入的程序集（自动去重）
        var scanAssemblies = new OperatorScanAssemblies(
            new[] { typeof(IOperator).Assembly }.Concat(additionalAssemblies)
        );

        services.AddSingleton(scanAssemblies);

        // 注册算子注册表：通过接口暴露，内部实现保持 internal
        services.AddSingleton<OperatorRegistry>();
        services.AddSingleton<IOperatorRegistry>(sp => sp.GetRequiredService<OperatorRegistry>());

        // 程序启动时自动扫描并写入 Redis
        services.AddHostedService<OperatorRegistryInitializer>();

        return services;
    }
}
