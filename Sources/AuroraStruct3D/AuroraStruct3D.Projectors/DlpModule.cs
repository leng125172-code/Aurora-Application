using Microsoft.Extensions.DependencyInjection;

namespace AuroraStruct3D.Projectors;

/// <summary>
/// 腾聚（TJ）结构光投影机模块，提供 TCP 控制服务注册扩展。
/// 支持 linux-arm64（RK3588）和 Windows 平台，无需本地 DLL。
/// </summary>
public static class DlpModule
{
    /// <summary>
    /// 注册结构光投影机 TCP 控制服务（单例）。
    /// 单例保持 TCP 长连接，避免频繁握手开销。
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合（链式调用）</returns>
    public static IServiceCollection AddDlpProjectorServices(this IServiceCollection services)
    {
        // 保留单实例兼容旧代码（直接注入 IDlpProjectorService 的调用方）
        services.AddSingleton<IDlpProjectorService, DlpProjectorService>();

        // 多设备连接池（推荐方式：通过 IProjectorConnectionPool 按 Guid 管理多台设备）
        services.AddSingleton<IProjectorConnectionPool, ProjectorConnectionPool>();
        return services;
    }
}
