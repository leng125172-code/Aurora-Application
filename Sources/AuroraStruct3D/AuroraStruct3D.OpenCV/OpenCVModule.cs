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
    /// 注册 OpenCV 图像处理服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合（支持链式调用）</returns>
    public static IServiceCollection AddOpenCVServices(this IServiceCollection services)
    {
        // 后续可在此注册 OpenCV 相关单例/瞬态服务
        return services;
    }
}
