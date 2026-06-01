using AuroraStruct3D.DeviceState;
using Volo.Abp;

namespace AuroraStruct3D.CalibrationManagement;

/// <summary>
/// 标定模块应用服务公共基类。
/// 统一通过 <see cref="LazyServiceProvider"/> 解析设备状态管理器，并提供运行模式守卫方法，
/// 避免在每个标定 AppService 中重复注入与重复实现。
/// </summary>
public abstract class CalibrationAppServiceBase : AuroraStruct3DAppService
{
    /// <summary>
    /// 通过延迟解析获取设备状态管理器（单例服务）。
    /// </summary>
    protected IDeviceStateManager DeviceStateManager =>
        LazyServiceProvider.LazyGetRequiredService<IDeviceStateManager>();

    /// <summary>
    /// 校验当前运行模式必须为手动或检修，否则抛出业务异常。
    /// 标定流程涉及相机抓拍、电机运动、投射器输出等硬件动作，仅允许在受控的人工模式下执行。
    /// </summary>
    protected void EnsureManualOrMaintenanceMode()
    {
        DeviceRunMode mode = DeviceStateManager.RunMode;
        if (mode is not (DeviceRunMode.Manual or DeviceRunMode.Maintenance))
        {
            throw new UserFriendlyException(
                $"当前运行模式为【{mode switch
                {
                    DeviceRunMode.Online => "联机",
                    DeviceRunMode.Auto => "自动",
                    _ => mode.ToString()
                }}】，标定操作仅允许在手动模式或检修模式下执行"
            );
        }
    }
}
