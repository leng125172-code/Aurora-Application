using Microsoft.Extensions.DependencyInjection;

namespace AuroraStruct3D.RS485;

/// <summary>
/// RS485 电机控制模块注册扩展
/// </summary>
public static class RS485Module
{
    /// <summary>
    /// 注册 RS485 电机控制服务。
    /// 串口和驱动的创建在 OnApplicationInitialization 阶段通过
    /// <see cref="IMotorControlService.Initialize"/> 从数据库配置完成，不在此处硬编码。
    /// </summary>
    /// <param name="services">依赖注入容器</param>
    /// <returns>服务集合（支持链式调用）</returns>
    public static IServiceCollection AddRS485MotorServices(this IServiceCollection services)
    {
        services.AddSingleton<IMotorControlService, MotorControlService>();
        return services;
    }
}
