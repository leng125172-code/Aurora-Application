using System.Collections.Frozen;
using System.Diagnostics;
using System.IO.Ports;
using AuroraStruct3D.Motors;
using AuroraStruct3D.RS485.Ktech;
using AuroraStruct3D.RS485.Leisai;
using AuroraStruct3D.RS485.Protocol;
using AuroraStruct3D.SerialPorts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AuroraStruct3D.RS485;

/// <summary>
/// 电机控制服务实现，统一管理多条 RS485 总线上的所有电机。
/// 串口配置和电机信息从数据库读取，通过 Initialize 方法在启动时完成初始化。
/// </summary>
public class MotorControlService : IMotorControlService, IDisposable
{
    private const string LogTag = "[Servos]";

    private readonly ILogger<MotorControlService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IServiceScopeFactory? _serviceScopeFactory;

    /// <summary>串口配置 ID → 串口实例，作为电机控制和串口调试的唯一运行态入口</summary>
    private readonly Dictionary<Guid, IRS485Port> _portsByConfigId = [];

    /// <summary>串口配置 ID → 运行态参数签名，用于参数变化时重建串口</summary>
    private readonly Dictionary<Guid, string> _portSignaturesByConfigId = [];

    /// <summary>驱动字典（key = slave_id，全局唯一）</summary>
    private FrozenDictionary<int, IMotorDriver> _drivers = FrozenDictionary<
        int,
        IMotorDriver
    >.Empty;

    /// <summary>从机地址 → 数据库电机轴 ID 映射（用于操作日志写入）</summary>
    private IReadOnlyDictionary<int, Guid> _axisIdBySlaveId = new Dictionary<int, Guid>();

    /// <summary>是否已释放资源</summary>
    private bool _disposed;

    /// <inheritdoc/>
    public IReadOnlyList<int> ConfiguredMotorIds { get; private set; } = [];

    /// <summary>
    /// 初始化电机控制服务（不打开串口，不创建驱动，等待 Initialize 调用）
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="loggerFactory">日志工厂，用于为各驱动创建类型化日志器</param>
    /// <param name="serviceScopeFactory">用于创建 DB Scope 写入操作日志（可选）</param>
    public MotorControlService(
        ILogger<MotorControlService> logger,
        ILoggerFactory loggerFactory,
        IServiceScopeFactory? serviceScopeFactory = null
    )
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _serviceScopeFactory = serviceScopeFactory;
    }

    /// <inheritdoc/>
    public void Initialize(
        IReadOnlyList<SerialPortConfig> portConfigs,
        IReadOnlyList<MotorAxis> enabledAxes
    )
    {
        foreach (IRS485Port oldPort in _portsByConfigId.Values)
        {
            oldPort.Dispose();
        }
        _portsByConfigId.Clear();
        _portSignaturesByConfigId.Clear();

        // 按串口 ID 分组电机轴
        ILookup<Guid, MotorAxis> axesByPort = enabledAxes.ToLookup(a => a.SerialPortConfigId);
        Dictionary<int, IMotorDriver> driverDict = new();

        foreach (SerialPortConfig portConfig in portConfigs)
        {
            // 创建串口实例（枚举值与 System.IO.Ports 完全对齐，可安全强转）
            IRS485Port port = GetOrCreateSerialPort(portConfig);

            // 尝试打开串口（开发环境串口不存在时记录警告，不崩溃启动）
            try
            {
                if (portConfig.IsEnabled && portConfig.BaudRate > 0)
                {
                    port.Open();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "{Tag} Serial port [{Name}] {Port} open failed. Motors on this bus are unavailable (ignorable in dev mode).",
                    LogTag,
                    portConfig.DisplayName,
                    portConfig.PortName
                );
            }

            // 为该串口上的所有启用轴创建对应驱动
            foreach (MotorAxis axis in axesByPort[portConfig.Id])
            {
                IMotorDriver driver = axis.Brand switch
                {
                    MotorBrand.KtechKtech => new KtechMotorDriver(
                        axis.SlaveId,
                        port,
                        _loggerFactory.CreateLogger<KtechMotorDriver>()
                    ),
                    MotorBrand.LeisaiIclRs => new LeisaiMotorDriver(
                        axis.SlaveId,
                        port,
                        _loggerFactory.CreateLogger<LeisaiMotorDriver>()
                    ),
                    _ => throw new NotSupportedException(
                        $"Motor [{axis.Name}] brand {axis.Brand} is not supported for automatic driver creation"
                    ),
                };

                // 全局 SlaveId 必须唯一（不同总线上的电机地址不可重复）
                if (!driverDict.TryAdd(axis.SlaveId, driver))
                {
                    _logger.LogWarning(
                        "{Tag} Motor [{Name}] slave address {SlaveId} conflicts with an existing motor and will be ignored. Ensure all motors have a globally unique SlaveId.",
                        LogTag,
                        axis.Name,
                        axis.SlaveId
                    );
                }
            }
        }

        _drivers = driverDict.ToFrozenDictionary();
        ConfiguredMotorIds = [.. _drivers.Keys];

        _logger.LogInformation(
            "{Tag} MotorControlService initialized. Buses: {PortCount}, Motors: {MotorCount} | [{Ids}]",
            LogTag,
            portConfigs.Count,
            _drivers.Count,
            string.Join(", ", ConfiguredMotorIds)
        );
    }

    /// <inheritdoc/>
    public void SetAxisIdMapping(IReadOnlyDictionary<int, Guid> axisIds)
    {
        _axisIdBySlaveId = axisIds;
        _logger.LogInformation(
            "{Tag} MotorControlService axis ID mapping injected, total {Count} entries",
            LogTag,
            axisIds.Count
        );
    }

    /// <inheritdoc/>
    public bool IsMotorConfigured(int motorId) => _drivers.ContainsKey(motorId);

    /// <inheritdoc/>
    public KtechMotorDriver? GetKtechMotorDriver(int slaveId)
    {
        return _drivers.TryGetValue(slaveId, out IMotorDriver? driver)
            ? driver as KtechMotorDriver
            : null;
    }

    /// <inheritdoc/>
    public IReadOnlyList<KtechMotorDriver> KtechDrivers =>
        [.. _drivers.Values.OfType<KtechMotorDriver>().OrderBy(d => d.SlaveId)];

    /// <inheritdoc/>
    public LeisaiMotorDriver? GetLeisaiMotorDriver(int slaveId)
    {
        return _drivers.TryGetValue(slaveId, out IMotorDriver? driver)
            ? driver as LeisaiMotorDriver
            : null;
    }

    /// <inheritdoc/>
    public IReadOnlyList<LeisaiMotorDriver> LeisaiDrivers =>
        [.. _drivers.Values.OfType<LeisaiMotorDriver>().OrderBy(d => d.SlaveId)];

    /// <inheritdoc/>
    public bool IsSerialPortOpen(Guid serialPortConfigId)
    {
        return _portsByConfigId.TryGetValue(serialPortConfigId, out IRS485Port? port)
            && port.IsOpen;
    }

    /// <inheritdoc/>
    public Task OpenSerialPortAsync(
        SerialPortConfig portConfig,
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        if (portConfig.BaudRate <= 0)
        {
            throw new InvalidOperationException(
                $"{LogTag} Serial port {portConfig.PortName} has no baud rate configured."
            );
        }

        IRS485Port port = GetOrCreateSerialPort(portConfig);
        port.Open();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void CloseSerialPort(Guid serialPortConfigId)
    {
        if (_portsByConfigId.TryGetValue(serialPortConfigId, out IRS485Port? port))
        {
            port.Close();
        }
    }

    /// <inheritdoc/>
    public async Task<byte[]> SendRawAsync(
        SerialPortConfig portConfig,
        byte[] request,
        int expectedResponseLength = -1,
        int timeoutMs = 500,
        CancellationToken cancellationToken = default
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        IRS485Port port = GetOrCreateSerialPort(portConfig);
        if (!port.IsOpen)
        {
            port.Open();
        }

        return await port.SendAndReceiveAsync(
                request,
                expectedResponseLength,
                timeoutMs,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

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
                    "{Tag} Failed to query motor {Id} ({Brand}) status. Returning fault status placeholder. Ensure the motor is properly connected and configured.",
                    LogTag,
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
            RecordMotorLog(
                motorId,
                MotorOperationType.Enable,
                false,
                sw.ElapsedMilliseconds,
                ex.Message
            );
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
            RecordMotorLog(
                motorId,
                MotorOperationType.Disable,
                false,
                sw.ElapsedMilliseconds,
                ex.Message
            );
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
            await driver
                .MoveAbsoluteAsync(position, speedRpm, cancellationToken)
                .ConfigureAwait(false);
            sw.Stop();
            RecordMotorLog(
                motorId,
                MotorOperationType.MoveAbsolute,
                true,
                sw.ElapsedMilliseconds,
                parameterSummary: $"Position={position}, Speed={speedRpm}rpm"
            );
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordMotorLog(
                motorId,
                MotorOperationType.MoveAbsolute,
                false,
                sw.ElapsedMilliseconds,
                errorMessage: ex.Message,
                parameterSummary: $"Position={position}, Speed={speedRpm}rpm"
            );
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
            await driver
                .MoveRelativeAsync(delta, speedRpm, cancellationToken)
                .ConfigureAwait(false);
            sw.Stop();
            RecordMotorLog(
                motorId,
                MotorOperationType.MoveRelative,
                true,
                sw.ElapsedMilliseconds,
                parameterSummary: $"Delta={delta}, Speed={speedRpm}rpm"
            );
        }
        catch (Exception ex)
        {
            sw.Stop();
            RecordMotorLog(
                motorId,
                MotorOperationType.MoveRelative,
                false,
                sw.ElapsedMilliseconds,
                errorMessage: ex.Message,
                parameterSummary: $"Delta={delta}, Speed={speedRpm}rpm"
            );
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
            RecordMotorLog(
                motorId,
                MotorOperationType.Stop,
                false,
                sw.ElapsedMilliseconds,
                ex.Message
            );
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("{Tag} Stopping all motors", LogTag);
        // Parallel stop commands, RS485Port internal mutex ensures timing safety
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
                    "{Tag} Error occurred while stopping motor {Id} ({Brand})",
                    LogTag,
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
            RecordMotorLog(
                motorId,
                MotorOperationType.EmergencyStop,
                false,
                sw.ElapsedMilliseconds,
                ex.Message
            );
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
            RecordMotorLog(
                motorId,
                MotorOperationType.Home,
                false,
                sw.ElapsedMilliseconds,
                ex.Message
            );
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
            RecordMotorLog(
                motorId,
                MotorOperationType.ClearFault,
                false,
                sw.ElapsedMilliseconds,
                ex.Message
            );
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
        foreach (IRS485Port port in _portsByConfigId.Values)
        {
            try
            {
                port.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "{Tag} An exception occurred while releasing the serial port resources.",
                    LogTag
                );
            }
        }
        _logger.LogInformation(
            "{Tag} MotorControlService released, total serial bus ports closed: {Count}",
            LogTag,
            _portsByConfigId.Count
        );
    }

    /// <summary>
    /// 根据串口配置创建或复用串口实例，参数变化时会安全重建。
    /// </summary>
    private IRS485Port GetOrCreateSerialPort(SerialPortConfig portConfig)
    {
        string signature = CreatePortSignature(portConfig);
        if (
            _portsByConfigId.TryGetValue(portConfig.Id, out IRS485Port? existingPort)
            && _portSignaturesByConfigId.TryGetValue(portConfig.Id, out string? existingSignature)
            && existingSignature == signature
        )
        {
            return existingPort;
        }

        if (_portsByConfigId.TryGetValue(portConfig.Id, out IRS485Port? oldPort))
        {
            oldPort.Dispose();
        }

        RS485Port newPort = new(
            portConfig.PortName,
            portConfig.BaudRate > 0 ? portConfig.BaudRate : 9600,
            _loggerFactory.CreateLogger<RS485Port>(),
            (Parity)(int)portConfig.Parity,
            portConfig.DataBits,
            (StopBits)(int)portConfig.StopBits
        );
        _portsByConfigId[portConfig.Id] = newPort;
        _portSignaturesByConfigId[portConfig.Id] = signature;
        return newPort;
    }

    /// <summary>
    /// 生成串口运行参数签名，用于判断是否需要重建底层 SerialPort。
    /// </summary>
    private static string CreatePortSignature(SerialPortConfig portConfig)
    {
        return string.Join(
            '|',
            portConfig.PortName,
            portConfig.BaudRate,
            portConfig.DataBits,
            (int)portConfig.Parity,
            (int)portConfig.StopBits,
            (int)portConfig.Handshake
        );
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
                $"Motor ID {motorId} is not configured. Configured motor IDs: [{string.Join(", ", ConfiguredMotorIds)}]",
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
                IMotorOperationLogRepository? repo =
                    scope.ServiceProvider.GetService<IMotorOperationLogRepository>();
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
                        errorMessage ?? "Unknown error",
                        roundTripMs,
                        parameterSummary: parameterSummary
                    );

                await repo.InsertAsync(log).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "{Tag} Failed to write operation log for motor {SlaveId} (Operation={Op})",
                    LogTag,
                    slaveId,
                    operationType
                );
            }
        });
    }
}
