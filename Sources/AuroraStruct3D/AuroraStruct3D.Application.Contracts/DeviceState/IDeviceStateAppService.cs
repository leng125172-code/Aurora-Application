using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AuroraStruct3D.DeviceState;

/// <summary>
/// 设备状态管理应用服务接口
/// </summary>
public interface IDeviceStateAppService : IApplicationService
{
    /// <summary>
    /// 获取当前设备状态快照（无需登录）
    /// </summary>
    Task<DeviceStateDto> GetCurrentStateAsync();

    /// <summary>
    /// 切换运行模式（仅在 Standby 或 Stopped 时允许，需要登录）
    /// </summary>
    /// <exception cref="Volo.Abp.UserFriendlyException">当前状态不允许切换时</exception>
    Task SwitchModeAsync(SwitchModeInput input);

    /// <summary>
    /// 获取当前活跃故障记录（无需登录，无故障时返回 null）
    /// </summary>
    Task<DeviceFaultDto?> GetCurrentFaultAsync();

    /// <summary>
    /// 分页查询历史故障记录（需要登录）
    /// </summary>
    Task<PagedResultDto<DeviceFaultDto>> GetFaultPagedListAsync(GetFaultPagedInput input);

    /// <summary>
    /// 分页查询状态切换日志（需要登录）
    /// </summary>
    Task<PagedResultDto<DeviceStateLogDto>> GetStateLogPagedListAsync(GetStateLogPagedInput input);
}
