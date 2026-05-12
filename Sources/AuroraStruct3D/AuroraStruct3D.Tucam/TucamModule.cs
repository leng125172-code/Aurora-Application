using Microsoft.Extensions.DependencyInjection;

namespace AuroraStruct3D.Tucam;

/// <summary>
/// TUCam相机SDK封装模块，提供服务注册扩展
/// </summary>
public static class TucamModule
{
    /// <summary>
    /// 注册TUCam相机服务（单例，SDK全局状态需唯一实例）
    /// </summary>
    public static IServiceCollection AddTucamCameraServices(this IServiceCollection services)
    {
        services.AddSingleton<ITucamCameraService, TucamCameraService>();
        return services;
    }
}
