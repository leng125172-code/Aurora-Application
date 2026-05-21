using AuroraStruct3D.DeviceState;
using Microsoft.Extensions.DependencyInjection;

namespace AuroraStruct3D.DeviceState;

/// <summary>
/// 设备状态管理模块，提供服务注册扩展。
/// 单例注册确保全局唯一状态机实例。
/// </summary>
public static class DeviceStateModule
{
  /// <summary>
  /// 注册设备状态管理服务（单例）。
  /// </summary>
  /// <param name="services">服务集合</param>
  /// <returns>服务集合（链式调用）</returns>
  public static IServiceCollection AddDeviceStateManagement(this IServiceCollection services)
  {
    services.AddSingleton<IDeviceStateManager, DeviceStateManager>();
    return services;
  }
}
