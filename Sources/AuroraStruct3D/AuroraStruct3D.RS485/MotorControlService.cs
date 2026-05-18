using System.Collections.Frozen;
using System.Diagnostics;
using AuroraStruct3D.Motors;
using AuroraStruct3D.RS485.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AuroraStruct3D.RS485;

/// <summary>
/// 电机控制服务实现，统一管理三轴 RS485 电机：
///   - 电机1（slave_id=1）：瓴控KTECH，CMD 0x9A 私有协议
///   - 电机2（slave_id=2）：瓴控KTECH，CMD 0x9A 私有协议
///   - 电机3（slave_id=3）：雷赛iCL-RS，Modbus RTU 协议
/// 三个电机共享同一条 RS485 总线（/dev/ttyS6），由 RS485Port 内置互斥锁保障时序。
/// </summary>
public class MotorControlService : IMotorControlService, IDisposable
{
    private readonly IRS485Port _port;
    private readonly ILogger<MotorControlService> _logger;
    private readonly IServiceScopeFactory? _serviceScopeFactory;

    /// <summary>驱动字典（key = slave_id）</summary>
    private readonly FrozenDictionary<int, IMotorDriver> _drivers;

    /// <summary>从机地址 → 数据库电机轴 ID 映射（用于操作日志写入）</summary>
    private IReadOnlyDictionary<int, Guid> _axisIdBySlaveId = new Dictionary<int, Guid>();

    /// <summary>是否已释放资源</summary>
    private bool _disposed;

    /// <inheritdoc/>
    public IReadOnlyList<int> ConfiguredMotorIds { get; }

    /// <summary>
    /// 初始化电机控制服务
    /// </summary>
    /// <param name="port">RS485 串口，必须对应 /dev/ttyS6</param>
    /// <param name="drivers">电机驱动集合，由 RS485Module 配置并注入</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="serviceScopeFactory">用于创建 DB Scope 写入操作日志（可选）</param>
    public MotorControlService(
        IRS485Port port,
        IEnumerable<IMotorDriver> drivers,
        ILogger<MotorControlService> logger,
        IServiceScopeFactory? serviceScopeFactory = null
    )
    {
        _port = port;
        _logger = logger;
        _serviceScopeFactory = serviceScopeFactory;
        _drivers = drivers.ToFrozenDictionary(d => d.SlaveId);
        ConfiguredMotorIds = [.. _drivers.Keys];

        _logger.LogInformation(
            "MotorControlService 初始化完成，已配置电机: [{Ids}]",
            string.Join(", ", ConfiguredMotorIds)
        );
    }

    /// <inheritdoc/>
    public void SetAxisIdMapping(IReadOnlyDictionary<int, Guid> axisIds)
    {
        _axisIdBySlaveId = axisIds;
        _logger.LogInformation(
            "MotorControlService 已注入轴 ID 映射，共 {Count} 条",
            axisIds.Count
        );
    }

    /// <inheritdoc/>
    public bool IsMotorConfigured(int motorId) => _drivers.ContainsKey(motorId);

    /// <inheritdoc/>
    public async Task<MotorStatus> QueryStatusAsync(
        int motorId,
        CancellationToken cancellationToken = default
    )
    {
        IMotorDriver driver = GetDriver(motorId);
        return await driver.QueryStatusAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<MotorStatus>> QueryAllStatusAsync(
        CancellationToken cancellationToken = default
    )
    {
        // 因共享RS485总线，串行查询以避免冲突（RS485Port内部已加锁，但串行更清晰）
        List<MotorStatus> result = new(_drivers.Count);
        foreach (IMotorDriver driver in _drivers.Values.OrderBy(d => d.SlaveId))
        {
            try
            {
                MotorStatus status = await driver
                    .QueryStatusAsync(cancellationToken)
                    .ConfigureAwait(false);
                result.Add(status);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "查询电机 {Id}（{Brand}）状态失败",
                    driver.SlaveId,
                    driver.Brand
                );
                // 返回故障状态占位，不中断其他电机查询
                result.Add(
                    new MotorStatus
                    {
                        MotorId = driver.SlaveId,
                        Brand = driver.Brand,
                        HasFault = true,
                    }
                );
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task EnableAsync(int motorId, CancellationToken cancellationToken = default)
    {
        IMotorDriver driver = GetDriver(motorId);
        Stopwatch sw = Stopwatch.StartNew();
        try
        {
            await driver.EnableAsync(cancellationToken).ConfigureAwait(false);
            sw.Stop();
            RecordMotorLog(motorId, MotorOperationType.Enable, true, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordMotorLog(motorId, MotorOperationType.Enable, false, sw.ElapsedMilliseconds, ex.Message);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task DisableAsync(int motorId, CancellationToken cancellationToken = default)
    {
        IMotorDriver driver = GetDriver(motorId);
        Stopwatch sw = Stopwatch.StartNew();
        try
        {
            await driver.DisableAsync(cancellationToken).ConfigureAwait(false);
            sw.Stop();
            RecordMotorLog(motorId, MotorOperationType.Disable, true, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordMotorLog(motorId, MotorOperationType.Disable, false, sw.ElapsedMilliseconds, ex.Message);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task MoveAbsoluteAsync(
        int motorId,
        long position,
        int speedRpm = 0,
        CancellationToken cancellationToken = default
    )
    {
        IMotorDriver driver = GetDriver(motorId);
        Stopwatch sw = Stopwatch.StartNew();
        try
        {
            await driver.MoveAbsoluteAsync(position, speedRpm, cancellationToken).ConfigureAwait(false);
            sw.Stop();
            RecordMotorLog(motorId, MotorOperationType.MoveAbsolute, true, sw.ElapsedMilliseconds,
                parameterSummary: $"位置={position}, 速度={speedRpm}rpm");
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordMotorLog(motorId, MotorOperationType.MoveAbsolute, false, sw.ElapsedMilliseconds,
                errorMessage: ex.Message, parameterSummary: $"位置={position}, 速度={speedRpm}rpm");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task MoveRelativeAsync(
        int motorId,
        long delta,
        int speedRpm = 0,
        CancellationToken cancellationToken = default
    )
    {
        IMotorDriver driver = GetDriver(motorId);
        Stopwatch sw = Stopwatch.StartNew();
        try
        {
            await driver.MoveRelativeAsync(delta, speedRpm, cancellationToken).ConfigureAwait(false);
            sw.Stop();
            RecordMotorLog(motorId, MotorOperationType.MoveRelative, true, sw.ElapsedMilliseconds,
                parameterSummary: $"位移={delta}, 速度={speedRpm}rpm");
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordMotorLog(motorId, MotorOperationType.MoveRelative, false, sw.ElapsedMilliseconds,
                errorMessage: ex.Message, parameterSummary: $"位移={delta}, 速度={speedRpm}rpm");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task StopAsync(int motorId, CancellationToken cancellationToken = default)
    {
        IMotorDriver driver = GetDriver(motorId);
        Stopwatch sw = Stopwatch.StartNew();
        try
        {
            await driver.StopAsync(cancellationToken).ConfigureAwait(false);
            sw.Stop();
            RecordMotorLog(motorId, MotorOperationType.Stop, true, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordMotorLog(motorId, MotorOperationType.Stop, false, sw.ElapsedMilliseconds, ex.Message);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("停止所有电机");
        // 并行发送停止指令，RS485Port 内部互斥锁保证时序安全
        IEnumerable<Task> stopTasks = _drivers.Values.Select(async driver =>
        {
            try
            {
                await driver.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "停止电机 {Id}（{Brand}）时发生错误",
                    driver.SlaveId,
                    driver.Brand
                );
            }
        });

        await Task.WhenAll(stopTasks).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task EmergencyStopAsync(int motorId, CancellationToken cancellationToken = default)
    {
        IMotorDriver driver = GetDriver(motorId);
        Stopwatch sw = Stopwatch.StartNew();
        try
        {
            await driver.EmergencyStopAsync(cancellationToken).ConfigureAwait(false);
            sw.Stop();
            RecordMotorLog(motorId, MotorOperationType.EmergencyStop, true, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordMotorLog(motorId, MotorOperationType.EmergencyStop, false, sw.ElapsedMilliseconds, ex.Message);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task HomeAsync(int motorId, CancellationToken cancellationToken = default)
    {
        IMotorDriver driver = GetDriver(motorId);
        Stopwatch sw = Stopwatch.StartNew();
        try
        {
            await driver.HomeAsync(cancellationToken).ConfigureAwait(false);
            sw.Stop();
            RecordMotorLog(motorId, MotorOperationType.Home, true, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordMotorLog(motorId, MotorOperationType.Home, false, sw.ElapsedMilliseconds, ex.Message);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task ClearFaultAsync(int motorId, CancellationToken cancellationToken = default)
    {
        IMotorDriver driver = GetDriver(motorId);
        Stopwatch sw = Stopwatch.StartNew();
        try
        {
            await driver.ClearFaultAsync(cancellationToken).ConfigureAwait(false);
            sw.Stop();
            RecordMotorLog(motorId, MotorOperationType.ClearFault, true, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordMotorLog(motorId, MotorOperationType.ClearFault, false, sw.ElapsedMilliseconds, ex.Message);
            throw;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _port.Dispose();
        _logger.LogInformation("MotorControlService 已释放，RS485串口已关闭");
    }

    /// <summary>
    /// 根据电机编号获取对应驱动实例，不存在时抛出异常
    /// </summary>
    private IMotorDriver GetDriver(int motorId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_drivers.TryGetValue(motorId, out IMotorDriver? driver))
        {
            throw new ArgumentException(
                $"电机编号 {motorId} 未配置，已配置的电机编号: [{string.Join(", ", ConfiguredMotorIds)}]",
                nameof(motorId)
            );
        }

        return driver;
    }

    /// <summary>
    /// fire-and-forget 写入电机操作日志，异常仅记录 Warning，不向上抛出
    /// </summary>
    private void RecordMotorLog(
        int slaveId,
        MotorOperationType operationType,
        bool isSuccess,
        long roundTripMs,
        string? errorMessage = null,
        string? parameterSummary = null
    )
    {
        // 未注入映射或无 DI 容器时跳过
        if (_serviceScopeFactory is null || !_axisIdBySlaveId.TryGetValue(slaveId, out Guid axisId))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                using IServiceScope scope = _serviceScopeFactory.CreateScope();
                IMotorOperationLogRepository? repo = scope
                    .ServiceProvider.GetService<IMotorOperationLogRepository>();
                if (repo is null)
                    return;

                MotorOperationLog log = isSuccess
                    ? MotorOperationLog.Success(
                        Guid.NewGuid(),
                        axisId,
                        slaveId,
                        operationType,
                        roundTripMs,
                        parameterSummary: parameterSummary
                    )
                    : MotorOperationLog.Failure(
                        Guid.NewGuid(),
                        axisId,
                        slaveId,
                        operationType,
                        errorMessage ?? "未知错误",
                        roundTripMs,
                        parameterSummary: parameterSummary
                    );

                await repo.InsertAsync(log).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "电机 {SlaveId} 写入操作日志失败（操作={Op}）",
                    slaveId,
                    operationType
                );
            }
        });
    }
}
