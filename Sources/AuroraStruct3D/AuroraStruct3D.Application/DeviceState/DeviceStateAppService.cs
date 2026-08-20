using AuroraStruct3D.DeviceState;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Users;
using Microsoft.AspNetCore.Mvc;

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
        return Task.FromResult(MapCurrentState());
    }

    private DeviceStateDto MapCurrentState() => new()
        {
            Status = _deviceStateManager.Status,
            RunMode = _deviceStateManager.RunMode,
            CurrentFaultId = _deviceStateManager.CurrentFaultId,
            CurrentFaultLevel = _deviceStateManager.CurrentFaultLevel,
            CurrentFaultCode = _deviceStateManager.CurrentFaultCode,
            IsInTransition = _deviceStateManager.IsInTransition,
            CanAcceptProductionCommand = _deviceStateManager.CanAcceptProductionCommand,
            CanSwitchMode = _deviceStateManager.CanSwitchMode,
            CanStart = !_deviceStateManager.IsInTransition
                && _deviceStateManager.Status is DeviceStatus.Standby or DeviceStatus.Stopped
                && _deviceStateManager.RunMode is DeviceRunMode.Online or DeviceRunMode.Auto,
            CanPause = _deviceStateManager.Status == DeviceStatus.Running,
            CanResume = _deviceStateManager.Status == DeviceStatus.Paused,
            CanStop = _deviceStateManager.Status is DeviceStatus.Running or DeviceStatus.Paused,
            CanAcknowledgeFault = _deviceStateManager.Status == DeviceStatus.Fault,
            CanReset = _deviceStateManager.Status is DeviceStatus.Stopped or DeviceStatus.Fault or DeviceStatus.EmergencyStop,
            CanEmergencyStop = _deviceStateManager.Status != DeviceStatus.EmergencyStop,
        };

    [Authorize, HttpPost("/api/app/device-state/start")]
    public Task<DeviceStateDto> StartAsync(DeviceCommandInput input) => ExecuteAsync(_deviceStateManager.StartAsync, input.Reason);

    [Authorize, HttpPost("/api/app/device-state/pause")]
    public Task<DeviceStateDto> PauseAsync(DeviceCommandInput input) => ExecuteAsync(_deviceStateManager.PauseAsync, input.Reason);

    [Authorize, HttpPost("/api/app/device-state/resume")]
    public Task<DeviceStateDto> ResumeAsync(DeviceCommandInput input) => ExecuteAsync(_deviceStateManager.ResumeAsync, input.Reason);

    [Authorize, HttpPost("/api/app/device-state/stop")]
    public Task<DeviceStateDto> StopAsync(DeviceCommandInput input) => ExecuteAsync(_deviceStateManager.StopAsync, input.Reason);

    [Authorize, HttpPost("/api/app/device-state/acknowledge-fault")]
    public Task<DeviceStateDto> AcknowledgeFaultAsync(DeviceCommandInput input) => ExecuteAsync(_deviceStateManager.AcknowledgeFaultAsync, input.Reason);

    [Authorize, HttpPost("/api/app/device-state/reset")]
    public Task<DeviceStateDto> ResetAsync(DeviceCommandInput input) => ExecuteAsync(_deviceStateManager.ResetAsync, input.Reason);

    [Authorize, HttpPost("/api/app/device-state/emergency-stop")]
    public async Task<DeviceStateDto> EmergencyStopAsync(EmergencyStopInput input)
    {
        StateChangeContext context = StateChangeContext.Emergency("软件急停", input.Reason);
        try { await _deviceStateManager.EmergencyStopAsync(context); }
        catch (InvalidOperationException ex) { throw new Volo.Abp.UserFriendlyException(ex.Message); }
        return MapCurrentState();
    }

    private async Task<DeviceStateDto> ExecuteAsync(Func<StateChangeContext, Task> command, string? reason)
    {
        StateChangeContext context = StateChangeContext.User(
            CurrentUser.Id?.ToString() ?? "system",
            CurrentUser.Name ?? CurrentUser.UserName ?? "system",
            reason);
        try { await command(context); }
        catch (InvalidOperationException ex) { throw new Volo.Abp.UserFriendlyException(ex.Message); }
        return MapCurrentState();
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
            source: input.Source,
            faultLevel: input.FaultLevel,
            isResolved: input.IsResolved,
            startTime: input.StartTime,
            endTime: input.EndTime
        );

        List<DeviceFault> items = await _faultRepository.GetPagedListAsync(
            skipCount: input.SkipCount,
            maxResultCount: input.MaxResultCount,
            source: input.Source,
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
            Source = fault.Source,
            DeviceId = fault.DeviceId,
            DeviceName = fault.DeviceName,
            WorkflowProjectId = fault.WorkflowProjectId,
            WorkflowProjectName = fault.WorkflowProjectName,
            WorkflowRunId = fault.WorkflowRunId,
            WorkflowId = fault.WorkflowId,
            WorkflowName = fault.WorkflowName,
            WorkflowNodeId = fault.WorkflowNodeId,
            LastOccurredAt = fault.LastOccurredAt,
            OccurrenceCount = fault.OccurrenceCount,
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
