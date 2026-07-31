using AuroraStruct3D.Cameras.Tucam;
using Microsoft.Extensions.DependencyInjection;

namespace AuroraStruct3D.Cameras;

/// <summary>
/// 通用相机驱动模块，提供驱动与注册表服务注册扩展。
/// </summary>
public static class CameraModule
{
    /// <summary>
    /// 注册一个通用相机驱动实现。华睿/大华驱动在 SDK 到位后实现
    /// <see cref="ICameraDriver"/> 并调用此方法。
    /// </summary>
    public static IServiceCollection AddCameraDriver<TCameraDriver>(this IServiceCollection services)
        where TCameraDriver : class, ICameraDriver
    {
        services.AddSingleton<TCameraDriver>();
        services.AddSingleton<ICameraDriver>(sp => sp.GetRequiredService<TCameraDriver>());
        return services;
    }

    /// <summary>
    /// 注册相机驱动服务（单例，TUCam SDK 全局状态需唯一实例）。
    /// <see cref="ICameraDriver"/> 是上层稳定契约；TUCam 仅是当前驱动实现。
    /// </summary>
    public static IServiceCollection AddTucamCameraDriver(this IServiceCollection services)
    {
        services.AddCameraDriver<TucamCameraService>();
        services.AddSingleton<ICameraDriverRegistry, CameraDriverRegistry>();
        services.AddSingleton<ITucamCameraService>(sp => sp.GetRequiredService<TucamCameraService>());
        return services;
    }
}
