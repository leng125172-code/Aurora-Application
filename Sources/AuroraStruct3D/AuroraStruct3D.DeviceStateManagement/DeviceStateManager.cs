using System.Diagnostics;
using AuroraStruct3D.DeviceState;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.Guids;

namespace AuroraStruct3D.DeviceState;

/// <summary>
/// 设备状态管理器实现（通过 <see cref="DeviceStateModule.AddDeviceStateManagement"/> 注册为单例）。
/// 负责统一管理设备总状态、运行模式、故障记录，执行互锁校验，并异步写入切换日志和故障记录。
/// </summary>
public class DeviceStateManager : IDeviceStateManager
{
    // ─── 依赖 ─────────────────────────────────────────────────────────────────

    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ILogger<DeviceStateManager> _logger;

    // ─── 线程安全锁 ──────────────────────────────────────────────────────────

    /// <summary>状态机操作互斥锁，防止并发切换</summary>
    private readonly SemaphoreSlim _lock = new(1, 1);

    // ─── 内部状态 ─────────────────────────────────────────────────────────────

    // 程序启动时进入初始化状态，由 DeviceInitializationJob 完成后切换为 Standby
    private DeviceStatus _status = DeviceStatus.Standby;
    private DeviceRunMode _runMode = DeviceRunMode.Manual;
    private DeviceFaultLevel? _faultLevel;
    private string? _faultCode;

    /// <summary>当前活跃故障记录的数据库 ID（无故障时为 null）</summary>
    private Guid? _currentFaultId;

    // ─── 接口属性 ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public DeviceStatus Status => _status;

    /// <inheritdoc/>
    public DeviceRunMode RunMode => _runMode;

    /// <inheritdoc/>
    public DeviceFaultLevel? CurrentFaultLevel => _faultLevel;

    /// <inheritdoc/>
    public string? CurrentFaultCode => _faultCode;

    /// <inheritdoc/>
    public Guid? CurrentFaultId => _currentFaultId;

    /// <inheritdoc/>
    public bool IsInTransition =>
        _status
            is DeviceStatus.Initializing
                or DeviceStatus.Starting
                or DeviceStatus.Stopping
                or DeviceStatus.Resetting
                or DeviceStatus.FaultAcknowledging;

    /// <inheritdoc/>
    public bool CanAcceptProductionCommand =>
        _status is DeviceStatus.Running or DeviceStatus.Paused;

    /// <inheritdoc/>
    public bool CanSwitchMode =>
        !IsInTransition && _status is DeviceStatus.Standby or DeviceStatus.Stopped;

    // ─── 事件 ─────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public event Action<DeviceStatus, DeviceStatus, StateChangeContext>? StatusChanged;

    /// <inheritdoc/>
    public event Action<DeviceRunMode, DeviceRunMode, StateChangeContext>? ModeChanged;

    // ─── 构造函数 ─────────────────────────────────────────────────────────────

    public DeviceStateManager(
        IGuidGenerator guidGenerator,
        ILogger<DeviceStateManager> logger,
        IServiceScopeFactory? scopeFactory = null
    )
    {
        _guidGenerator = guidGenerator;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    // ─── 状态切换方法 ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task StartAsync(StateChangeContext context)
    {
        await _lock.WaitAsync();
        try
        {
            GuardNotInTransition();
            if (_status is not (DeviceStatus.Standby or DeviceStatus.Stopped))
                throw new InvalidOperationException(
                    $"当前状态 [{_status}] 不允许启动，必须处于 Standby 或 Stopped 状态"
                );
            if (_runMode == DeviceRunMode.Maintenance)
                throw new InvalidOperationException(
                    "维护模式下不允许启动生产，请先切换至在线或自动模式"
                );

            await TransitionStatusAsync(DeviceStatus.Starting, context);
            await TransitionStatusAsync(DeviceStatus.Running, context);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task PauseAsync(StateChangeContext context)
    {
        await _lock.WaitAsync();
        try
        {
            if (_status != DeviceStatus.Running)
                throw new InvalidOperationException(
                    $"当前状态 [{_status}] 不允许暂停，必须处于 Running 状态"
                );

            await TransitionStatusAsync(DeviceStatus.Paused, context);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task ResumeAsync(StateChangeContext context)
    {
        await _lock.WaitAsync();
        try
        {
            if (_status != DeviceStatus.Paused)
                throw new InvalidOperationException(
                    $"当前状态 [{_status}] 不允许恢复，必须处于 Paused 状态"
                );

            await TransitionStatusAsync(DeviceStatus.Running, context);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task StopAsync(StateChangeContext context)
    {
        await _lock.WaitAsync();
        try
        {
            if (_status is not (DeviceStatus.Running or DeviceStatus.Paused))
                throw new InvalidOperationException(
                    $"当前状态 [{_status}] 不允许停机，必须处于 Running 或 Paused 状态"
                );

            await TransitionStatusAsync(DeviceStatus.Stopping, context);
            await TransitionStatusAsync(DeviceStatus.Stopped, context);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task EmergencyStopAsync(StateChangeContext context)
    {
        // 急停不判断当前状态，直接切换，优先级最高
        await _lock.WaitAsync();
        try
        {
            _faultLevel = DeviceFaultLevel.SafetyFault;
            _faultCode = context.FaultCode;

            // 生成故障记录 ID，以便与状态日志建立关联
            Guid faultId = _guidGenerator.Create();
            _currentFaultId = faultId;

            await TransitionStatusAsync(DeviceStatus.EmergencyStop, context, faultId: faultId);
            FireAndForgetCreateFault(faultId, DeviceFaultLevel.SafetyFault, context);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task FaultAsync(StateChangeContext context)
    {
        await _lock.WaitAsync();
        try
        {
            _faultLevel = context.FaultLevel;
            _faultCode = context.FaultCode;

            // 生成故障记录 ID，以便与状态日志建立关联
            Guid faultId = _guidGenerator.Create();
            _currentFaultId = faultId;

            await TransitionStatusAsync(DeviceStatus.Fault, context, faultId: faultId);
            FireAndForgetCreateFault(
                faultId,
                context.FaultLevel ?? DeviceFaultLevel.GeneralFault,
                context
            );
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task AcknowledgeFaultAsync(StateChangeContext context)
    {
        await _lock.WaitAsync();
        try
        {
            if (_status != DeviceStatus.Fault)
                throw new InvalidOperationException(
                    $"当前状态 [{_status}] 不需要确认故障，必须处于 Fault 状态"
                );

            Guid? resolvedFaultId = _currentFaultId;

            await TransitionStatusAsync(DeviceStatus.FaultAcknowledging, context);
            _faultLevel = null;
            _faultCode = null;
            _currentFaultId = null;
            await TransitionStatusAsync(DeviceStatus.Standby, context);

            // 故障由操作人确认处理（非自动恢复）
            if (resolvedFaultId.HasValue)
                FireAndForgetResolveFault(resolvedFaultId.Value, isAutoRecovered: false, context);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task ResetAsync(StateChangeContext context)
    {
        await _lock.WaitAsync();
        try
        {
            if (
                _status
                is not (DeviceStatus.EmergencyStop or DeviceStatus.Fault or DeviceStatus.Stopped)
            )
                throw new InvalidOperationException(
                    $"当前状态 [{_status}] 不允许复位，必须处于 EmergencyStop、Fault 或 Stopped 状态"
                );

            Guid? resolvedFaultId = _currentFaultId;

            await TransitionStatusAsync(DeviceStatus.Resetting, context);
            _faultLevel = null;
            _faultCode = null;
            _currentFaultId = null;
            await TransitionStatusAsync(DeviceStatus.Standby, context);

            // 复位视为系统自动恢复
            if (resolvedFaultId.HasValue)
                FireAndForgetResolveFault(resolvedFaultId.Value, isAutoRecovered: true, context);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task CompleteInitializationAsync(StateChangeContext context)
    {
        await _lock.WaitAsync();
        try
        {
            if (_status != DeviceStatus.Initializing)
                throw new InvalidOperationException(
                    $"当前状态 [{_status}] 不是 Initializing，无需调用 CompleteInitializationAsync"
                );

            await TransitionStatusAsync(DeviceStatus.Standby, context);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task SwitchModeAsync(DeviceRunMode newMode, StateChangeContext context)
    {
        await _lock.WaitAsync();
        try
        {
            if (!CanSwitchMode)
                throw new InvalidOperationException(
                    $"当前状态 [{_status}] 不允许切换运行模式，必须处于 Standby 或 Stopped 状态"
                );

            if (_runMode == newMode)
                return;

            DeviceRunMode oldMode = _runMode;
            _runMode = newMode;

            ModeChanged?.Invoke(oldMode, newMode, context);

            DeviceStateLog modeLog =
                context.Trigger == StateChangeTrigger.UserManual
                    ? DeviceStateLog.ByUser(
                        _guidGenerator.Create(),
                        _status,
                        _status,
                        oldMode,
                        newMode,
                        context.OperatorId ?? string.Empty,
                        context.OperatorName ?? string.Empty,
                        context.Reason,
                        context.Remark,
                        durationMs: null
                    )
                    : DeviceStateLog.BySystem(
                        _guidGenerator.Create(),
                        _status,
                        _status,
                        oldMode,
                        newMode,
                        context.Trigger,
                        context.Reason,
                        faultId: null,
                        durationMs: null,
                        context.Remark
                    );

            FireAndForgetLog(modeLog, context);
        }
        finally
        {
            _lock.Release();
        }
    }

    // ─── 私有辅助方法 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 执行状态切换，测量耗时，发布事件，fire-and-forget 写入日志
    /// </summary>
    private async Task TransitionStatusAsync(
        DeviceStatus newStatus,
        StateChangeContext context,
        Guid? faultId = null
    )
    {
        Stopwatch sw = Stopwatch.StartNew();
        DeviceStatus oldStatus = _status;
        _status = newStatus;

        StatusChanged?.Invoke(oldStatus, newStatus, context);

        sw.Stop();

        DeviceStateLog log =
            context.Trigger == StateChangeTrigger.UserManual
                ? DeviceStateLog.ByUser(
                    _guidGenerator.Create(),
                    oldStatus,
                    newStatus,
                    _runMode,
                    _runMode,
                    context.OperatorId ?? string.Empty,
                    context.OperatorName ?? string.Empty,
                    context.Reason,
                    context.Remark,
                    durationMs: sw.ElapsedMilliseconds
                )
                : DeviceStateLog.BySystem(
                    _guidGenerator.Create(),
                    oldStatus,
                    newStatus,
                    _runMode,
                    _runMode,
                    context.Trigger,
                    context.Reason,
                    faultId,
                    sw.ElapsedMilliseconds,
                    context.Remark
                );

        FireAndForgetLog(log, context);

        // 给过渡状态一个短暂的异步让步，模拟状态通知传播
        await Task.Yield();
    }

    /// <summary>
    /// 校验当前不处于过渡状态
    /// </summary>
    private void GuardNotInTransition()
    {
        if (IsInTransition)
            throw new InvalidOperationException(
                $"设备正处于过渡状态 [{_status}]，请等待过渡完成后再操作"
            );
    }

    /// <summary>
    /// Fire-and-forget 写入状态切换日志到数据库，失败仅记录 Warning
    /// </summary>
    private void FireAndForgetLog(DeviceStateLog log, StateChangeContext context)
    {
        if (_scopeFactory is null)
            return;

        // 抑制 ExecutionContext 流传播，防止父 ABP UoW 通过 AsyncLocal 传入新任务
        // 否则会触发 "This unit of work already contains a transaction API for the given key"
        using (ExecutionContext.SuppressFlow())
            _ = Task.Run(async () =>
            {
                try
                {
                    using IServiceScope scope = _scopeFactory.CreateScope();
                    IDeviceStateLogRepository repo =
                        scope.ServiceProvider.GetRequiredService<IDeviceStateLogRepository>();
                    await repo.InsertAsync(log, autoSave: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "写入设备状态日志失败：{Status} -> {NewStatus}，触发来源：{Trigger}",
                        log.PreviousStatus,
                        log.NewStatus,
                        context.Trigger
                    );
                }
            });
    }

    /// <summary>
    /// Fire-and-forget 向数据库写入新故障记录，失败仅记录 Warning
    /// </summary>
    private void FireAndForgetCreateFault(
        Guid faultId,
        DeviceFaultLevel faultLevel,
        StateChangeContext context
    )
    {
        if (_scopeFactory is null)
            return;

        // 抑制 ExecutionContext 流传播，防止父 ABP UoW 通过 AsyncLocal 传入新任务
        using (ExecutionContext.SuppressFlow())
            _ = Task.Run(async () =>
            {
                try
                {
                    DeviceFault fault = new(
                        faultId,
                        faultLevel,
                        context.FaultCode,
                        context.FaultMessage,
                        context.FaultReason,
                        causedModeSwitch: false,
                        switchedToMode: null,
                        stateLogId: null,
                        context.Remark
                    );

                    using IServiceScope scope = _scopeFactory.CreateScope();
                    IDeviceFaultRepository repo =
                        scope.ServiceProvider.GetRequiredService<IDeviceFaultRepository>();
                    await repo.InsertAsync(fault, autoSave: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "写入故障记录失败，故障 ID：{FaultId}，级别：{FaultLevel}",
                        faultId,
                        faultLevel
                    );
                }
            });
    }

    /// <summary>
    /// Fire-and-forget 更新数据库中的故障记录为已解决，失败仅记录 Warning
    /// </summary>
    private void FireAndForgetResolveFault(
        Guid faultId,
        bool isAutoRecovered,
        StateChangeContext context
    )
    {
        if (_scopeFactory is null)
            return;

        // 抑制 ExecutionContext 流传播，防止父 ABP UoW 通过 AsyncLocal 传入新任务
        using (ExecutionContext.SuppressFlow())
            _ = Task.Run(async () =>
            {
                try
                {
                    using IServiceScope scope = _scopeFactory.CreateScope();
                    IDeviceFaultRepository repo =
                        scope.ServiceProvider.GetRequiredService<IDeviceFaultRepository>();

                    DeviceFault? fault = await repo.FindAsync(faultId);
                    if (fault is null)
                    {
                        _logger.LogWarning(
                            "尝试解除故障 {FaultId} 时，数据库中未找到对应记录",
                            faultId
                        );
                        return;
                    }

                    if (isAutoRecovered)
                        fault.MarkAutoRecovered(context.Reason);
                    else
                        fault.MarkResolved(
                            context.OperatorId ?? string.Empty,
                            context.OperatorName ?? string.Empty,
                            resolutionDescription: context.Reason,
                            remark: context.Remark
                        );

                    await repo.UpdateAsync(fault, autoSave: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "更新故障记录失败，故障 ID：{FaultId}，isAutoRecovered={IsAutoRecovered}",
                        faultId,
                        isAutoRecovered
                    );
                }
            });
    }
}
