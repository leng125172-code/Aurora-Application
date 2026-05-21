using AuroraStruct3D.DeviceState;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Volo.Abp.AspNetCore.SignalR;

namespace AuroraStruct3D.Hubs;

/// <summary>
/// 设备状态实时推送 SignalR Hub。
/// 允许匿名访问，登录前后均可连接。
/// 客户端连接后立即收到当前状态快照。
/// </summary>
[AllowAnonymous]
[DisableAutoHubMap] // 禁止 ABP 自动注册，在 Module 中显式 MapHub
public class DeviceStateHub : AbpHub<IDeviceStateHub>
{
    private readonly IDeviceStateManager _deviceStateManager;
    private readonly IDeviceFaultRepository _faultRepository;

    public DeviceStateHub(
        IDeviceStateManager deviceStateManager,
        IDeviceFaultRepository faultRepository
    )
    {
        _deviceStateManager = deviceStateManager;
        _faultRepository = faultRepository;
    }

    /// <summary>客户端连接时，立即推送当前状态快照</summary>
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();

        // 推送当前设备状态
        DeviceStateDto state = BuildCurrentStateDto();
        await Clients.Caller.ReceiveDeviceStateAsync(state);

        // 推送当前活跃故障
        DeviceFaultDto? faultDto = await GetCurrentFaultDtoAsync();
        await Clients.Caller.ReceiveDeviceFaultAsync(faultDto);
    }

    // ─── 私有辅助方法 ─────────────────────────────────────────────────────────

    private DeviceStateDto BuildCurrentStateDto() =>
        new DeviceStateDto
        {
            Status = _deviceStateManager.Status,
            RunMode = _deviceStateManager.RunMode,
            CurrentFaultId = _deviceStateManager.CurrentFaultId,
            CurrentFaultLevel = _deviceStateManager.CurrentFaultLevel,
            CurrentFaultCode = _deviceStateManager.CurrentFaultCode,
            IsInTransition = _deviceStateManager.IsInTransition,
            CanAcceptProductionCommand = _deviceStateManager.CanAcceptProductionCommand,
            CanSwitchMode = _deviceStateManager.CanSwitchMode,
        };

    private async Task<DeviceFaultDto?> GetCurrentFaultDtoAsync()
    {
        List<DeviceFault> unresolved = await _faultRepository.GetUnresolvedListAsync();
        DeviceFault? fault = unresolved.FirstOrDefault();
        if (fault is null)
            return null;

        return new DeviceFaultDto
        {
            Id = fault.Id,
            OccurredAt = fault.OccurredAt,
            FaultLevel = fault.FaultLevel,
            FaultCode = fault.FaultCode,
            FaultMessage = fault.FaultMessage,
            FaultReason = fault.FaultReason,
            IsResolved = fault.IsResolved,
            IsAutoRecovered = fault.IsAutoRecovered,
            ResolverId = fault.ResolverId,
            ResolverName = fault.ResolverName,
            ResolvedAt = fault.ResolvedAt,
            ResolutionDescription = fault.ResolutionDescription,
            DurationMs = fault.DurationMs,
            CausedModeSwitch = fault.CausedModeSwitch,
            SwitchedToMode = fault.SwitchedToMode,
            StateLogId = fault.StateLogId,
            Remark = fault.Remark,
            CreationTime = fault.CreationTime,
        };
    }
}
