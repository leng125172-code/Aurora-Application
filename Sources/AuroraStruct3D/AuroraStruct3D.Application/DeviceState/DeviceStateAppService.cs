using AuroraStruct3D.DeviceState;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Users;

namespace AuroraStruct3D.DeviceState;

/// <summary>
/// 设备状态管理应用服务实现
/// </summary>
public class DeviceStateAppService : AuroraStruct3DAppService, IDeviceStateAppService
{
    private readonly IDeviceStateManager _deviceStateManager;
    private readonly IDeviceFaultRepository _faultRepository;
    private readonly IDeviceStateLogRepository _logRepository;

    public DeviceStateAppService(
        IDeviceStateManager deviceStateManager,
        IDeviceFaultRepository faultRepository,
        IDeviceStateLogRepository logRepository
    )
    {
        _deviceStateManager = deviceStateManager;
        _faultRepository = faultRepository;
        _logRepository = logRepository;
    }

    /// <inheritdoc/>
    [AllowAnonymous]
    public Task<DeviceStateDto> GetCurrentStateAsync()
    {
        DeviceStateDto dto = new DeviceStateDto
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
        return Task.FromResult(dto);
    }

    /// <inheritdoc/>
    [Authorize]
    public async Task SwitchModeAsync(SwitchModeInput input)
    {
        // 从当前登录用户获取操作人信息
        StateChangeContext context = StateChangeContext.User(
            operatorId: CurrentUser.Id?.ToString() ?? "system",
            operatorName: CurrentUser.Name ?? CurrentUser.UserName ?? "system",
            reason: input.Reason
        );

        try
        {
            await _deviceStateManager.SwitchModeAsync(input.NewMode, context);
        }
        catch (InvalidOperationException ex)
        {
            throw new Volo.Abp.UserFriendlyException(ex.Message);
        }
    }

    /// <inheritdoc/>
    [AllowAnonymous]
    public async Task<DeviceFaultDto?> GetCurrentFaultAsync()
    {
        List<DeviceFault> unresolved = await _faultRepository.GetUnresolvedListAsync();
        DeviceFault? fault = unresolved.FirstOrDefault();
        return fault is null ? null : MapFaultToDto(fault);
    }

    /// <inheritdoc/>
    [Authorize]
    public async Task<PagedResultDto<DeviceFaultDto>> GetFaultPagedListAsync(
        GetFaultPagedInput input
    )
    {
        long totalCount = await _faultRepository.GetCountAsync(
            faultLevel: input.FaultLevel,
            isResolved: input.IsResolved,
            startTime: input.StartTime,
            endTime: input.EndTime
        );

        List<DeviceFault> items = await _faultRepository.GetPagedListAsync(
            skipCount: input.SkipCount,
            maxResultCount: input.MaxResultCount,
            faultLevel: input.FaultLevel,
            isResolved: input.IsResolved,
            startTime: input.StartTime,
            endTime: input.EndTime
        );

        return new PagedResultDto<DeviceFaultDto>(totalCount, items.Select(MapFaultToDto).ToList());
    }

    /// <inheritdoc/>
    [Authorize]
    public async Task<PagedResultDto<DeviceStateLogDto>> GetStateLogPagedListAsync(
        GetStateLogPagedInput input
    )
    {
        long totalCount = await _logRepository.GetCountAsync(
            trigger: input.Trigger,
            status: input.Status,
            startTime: input.StartTime,
            endTime: input.EndTime,
            isSuccessful: input.IsSuccessful
        );

        List<DeviceStateLog> items = await _logRepository.GetPagedListAsync(
            skipCount: input.SkipCount,
            maxResultCount: input.MaxResultCount,
            trigger: input.Trigger,
            status: input.Status,
            startTime: input.StartTime,
            endTime: input.EndTime,
            isSuccessful: input.IsSuccessful
        );

        return new PagedResultDto<DeviceStateLogDto>(
            totalCount,
            items.Select(MapLogToDto).ToList()
        );
    }

    // ─── 私有映射辅助 ────────────────────────────────────────────────────────

    private static DeviceFaultDto MapFaultToDto(DeviceFault fault) =>
        new DeviceFaultDto
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

    private static DeviceStateLogDto MapLogToDto(DeviceStateLog log) =>
        new DeviceStateLogDto
        {
            Id = log.Id,
            OccurredAt = log.OccurredAt,
            PreviousStatus = log.PreviousStatus,
            NewStatus = log.NewStatus,
            IsStatusChange = log.IsStatusChange,
            IsTransitionState = log.IsTransitionState,
            PreviousMode = log.PreviousMode,
            NewMode = log.NewMode,
            IsModeChange = log.IsModeChange,
            Trigger = log.Trigger,
            FaultId = log.FaultId,
            OperatorId = log.OperatorId,
            OperatorName = log.OperatorName,
            Reason = log.Reason,
            Remark = log.Remark,
            DurationMs = log.DurationMs,
            IsSuccessful = log.IsSuccessful,
            ErrorMessage = log.ErrorMessage,
            CreationTime = log.CreationTime,
        };
}
