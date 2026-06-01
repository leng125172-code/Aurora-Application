using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Ports;
using AuroraStruct3D.DeviceState;
using AuroraStruct3D.Motors.Dtos;
using AuroraStruct3D.RS485;
using AuroraStruct3D.RS485.Ktech;
using AuroraStruct3D.RS485.Modbus;
using AuroraStruct3D.RS485.Protocol;
using AuroraStruct3D.SerialPorts;
using AuroraStruct3D.Sessions;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Application.Dtos;

namespace AuroraStruct3D.Motors;

/// <summary>
/// 485 串口电机设备管理应用服务实现。
/// </summary>
public class MotorDeviceAppService : AuroraStruct3DAppService, IMotorDeviceAppService
{
    private const long KtechSingleTurnAngleUnits = MotorConsts.KtechSingleTurnAngleUnits;
    private const int KtechMinSlaveId = 1;
    private const int KtechMaxSlaveId = 32;
    private const int LeisaiMinSlaveId = 1;
    private const int LeisaiMaxSlaveId = 31;
    private const int ScanProbeIntervalMs = 50;

    private static readonly int[] DefaultMotorScanBaudRates = [115200, 38400, 9600, 19200, 57600];

    private readonly IMotorAxisRepository _motorAxisRepository;
    private readonly ISerialPortConfigRepository _serialPortConfigRepository;
    private readonly IMotorControlService _motorControlService;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IDeviceOperationSessionManager _sessionManager;
    private readonly ICurrentClientSession _currentClientSession;
    private readonly IMotorScanProgressNotifier _scanProgressNotifier;
    private readonly IDeviceStateManager _deviceStateManager;
    private readonly IMotorOperationLogRepository _operationLogRepository;

    public MotorDeviceAppService(
        IMotorAxisRepository motorAxisRepository,
        ISerialPortConfigRepository serialPortConfigRepository,
        IMotorControlService motorControlService,
        ILoggerFactory loggerFactory,
        IDeviceOperationSessionManager sessionManager,
        ICurrentClientSession currentClientSession,
        IMotorScanProgressNotifier scanProgressNotifier,
        IDeviceStateManager deviceStateManager,
        IMotorOperationLogRepository operationLogRepository
    )
    {
        _motorAxisRepository = motorAxisRepository;
        _serialPortConfigRepository = serialPortConfigRepository;
        _motorControlService = motorControlService;
        _loggerFactory = loggerFactory;
        _sessionManager = sessionManager;
        _currentClientSession = currentClientSession;
        _scanProgressNotifier = scanProgressNotifier;
        _deviceStateManager = deviceStateManager;
        _operationLogRepository = operationLogRepository;
    }

    /// <inheritdoc/>
    public async Task<PagedResultDto<MotorAxisDto>> GetListAsync(GetMotorAxisListDto input)
    {
        List<MotorAxis> axes = await _motorAxisRepository.GetListAsync();
        List<SerialPortConfig> portConfigs = await _serialPortConfigRepository.GetListAsync();
        Dictionary<Guid, SerialPortConfig> portMap = portConfigs.ToDictionary(config => config.Id);

        if (input.RefreshHardware)
        {
            foreach (MotorAxis axis in axes)
            {
                if (
                    !axis.IsEnabled
                    || !_motorControlService.IsSerialPortOpen(axis.SerialPortConfigId)
                )
                {
                    continue;
                }

                await RefreshStatusCoreAsync(axis, throwOnFailure: false);
            }

            axes = await _motorAxisRepository.GetListAsync();
        }

        IEnumerable<MotorAxis> filtered = axes;
        if (input.IsEnabled.HasValue)
        {
            filtered = filtered.Where(axis => axis.IsEnabled == input.IsEnabled.Value);
        }
        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            string filter = input.Filter.Trim();
            filtered = filtered.Where(axis =>
                axis.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || axis.SlaveId.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase)
                || (
                    portMap.TryGetValue(axis.SerialPortConfigId, out SerialPortConfig? portConfig)
                    && portConfig.PortName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                )
            );
        }

        List<MotorAxis> filteredList = filtered.OrderBy(axis => axis.AxisIndex).ToList();
        List<MotorAxisDto> page = filteredList
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount)
            .Select(axis => ToDto(axis, portMap.GetValueOrDefault(axis.SerialPortConfigId)))
            .ToList();

        return new PagedResultDto<MotorAxisDto>(filteredList.Count, page);
    }

    /// <inheritdoc/>
    public async Task<MotorAxisDto> GetAsync(Guid id)
    {
        MotorAxis axis = await _motorAxisRepository.GetAsync(id);
        SerialPortConfig? portConfig = await _serialPortConfigRepository.FindAsync(
            axis.SerialPortConfigId
        );
        return ToDto(axis, portConfig);
    }

    /// <inheritdoc/>
    public async Task<ScanMotorDevicesResultDto> ScanDevicesAsync(ScanMotorDevicesInput input)
    {
        EnsureManualOrMaintenanceMode();
        if (input.StartSlaveId > input.EndSlaveId)
        {
            throw new UserFriendlyException("起始从机地址不能大于结束从机地址");
        }

        await SyncSystemSerialPortsAsync();
        List<SerialPortConfig> portConfigs = await _serialPortConfigRepository.GetListAsync();
        int[] baudRates = GetMotorScanBaudRates(input.BaudRates);

        // 扫描期间释放所有已占用串口，避免与电机控制服务冲突
        foreach (SerialPortConfig portConfig in portConfigs)
        {
            _motorControlService.CloseSerialPort(portConfig.Id);
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        ScanCounters counters = new();
        int totalPorts = portConfigs.Count;
        ConcurrentBag<ScanCandidate> candidates = new();

        await NotifyProgressAsync(
            new MotorScanProgressDto
            {
                Kind = MotorScanProgressKind.Started,
                Message = $"开始扫描，共 {totalPorts} 个串口、{baudRates.Length} 个波特率",
                TotalPorts = totalPorts,
                ElapsedMs = 0,
            }
        );

        // 一个串口一个 Task，串口内仍按波特率串行（同一物理总线无法同时跑两个波特率）
        IEnumerable<Task> scanTasks = portConfigs.Select(portConfig =>
            Task.Run(async () =>
            {
                await NotifyProgressAsync(
                    new MotorScanProgressDto
                    {
                        Kind = MotorScanProgressKind.PortStarted,
                        Message = $"开始扫描串口 {portConfig.PortName}",
                        SerialPortConfigId = portConfig.Id,
                        PortName = portConfig.PortName,
                        TotalPorts = totalPorts,
                        FinishedPorts = counters.FinishedPorts,
                        TriedCount = counters.TriedCount,
                        FoundCount = counters.FoundCount,
                        ElapsedMs = stopwatch.ElapsedMilliseconds,
                    }
                );

                int portFoundCount = 0;
                // 整体扫描预算：避免 Linux 下个别串口因驱动异常导致 Read/Write 永久阻塞，
                // 一旦超过预算就让本端口快速失败，保证 Task.WhenAll 能及时返回
                using CancellationTokenSource portBudgetCts = new(TimeSpan.FromSeconds(45));
                bool portBudgetExceeded = false;
                try
                {
                    foreach (int baudRate in baudRates)
                    {
                        if (portBudgetCts.IsCancellationRequested)
                        {
                            portBudgetExceeded = true;
                            break;
                        }
                        bool foundOnCurrentBaud = false;
                        using RS485Port probePort = new(
                            portConfig.PortName,
                            baudRate,
                            _loggerFactory.CreateLogger<RS485Port>(),
                            Parity.None,
                            8,
                            StopBits.One
                        );

                        try
                        {
                            // SerialPort.Open 在 Linux 上偶发会阻塞（无效串口或被独占），
                            // 这里强制超时，避免单个串口拖死整个 Task.WhenAll
                            await Task.Run(() => probePort.Open())
                                .WaitAsync(TimeSpan.FromSeconds(2));
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning(
                                ex,
                                "串口 {PortName} 以 {BaudRate} 打开失败，跳过该串口",
                                portConfig.PortName,
                                baudRate
                            );
                            await NotifyProgressAsync(
                                new MotorScanProgressDto
                                {
                                    Kind = MotorScanProgressKind.PortError,
                                    Message = $"串口 {portConfig.PortName} 打开失败：{ex.Message}",
                                    SerialPortConfigId = portConfig.Id,
                                    PortName = portConfig.PortName,
                                    BaudRate = baudRate,
                                    TotalPorts = totalPorts,
                                    FinishedPorts = counters.FinishedPorts,
                                    TriedCount = counters.TriedCount,
                                    FoundCount = counters.FoundCount,
                                    ElapsedMs = stopwatch.ElapsedMilliseconds,
                                }
                            );
                            break;
                        }

                        try
                        {
                            foundOnCurrentBaud |= await ProbeRangeAsync(
                                    probePort,
                                    portConfig,
                                    baudRate,
                                    input,
                                    candidates,
                                    MotorBrand.KtechKtech,
                                    (port, slaveId) =>
                                        ProbeKtechAsync(port, slaveId, input.ProbeTimeoutMs),
                                    KtechMinSlaveId,
                                    KtechMaxSlaveId,
                                    counters,
                                    stopwatch,
                                    totalPorts
                                )
                                .WaitAsync(portBudgetCts.Token);

                            foundOnCurrentBaud |= await ProbeRangeAsync(
                                    probePort,
                                    portConfig,
                                    baudRate,
                                    input,
                                    candidates,
                                    MotorBrand.LeisaiIclRs,
                                    (port, slaveId) =>
                                        ProbeLeisaiAsync(port, slaveId, input.ProbeTimeoutMs),
                                    LeisaiMinSlaveId,
                                    LeisaiMaxSlaveId,
                                    counters,
                                    stopwatch,
                                    totalPorts
                                )
                                .WaitAsync(portBudgetCts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            // 端口整体预算超时，跳出全部波特率循环
                            portBudgetExceeded = true;
                            break;
                        }

                        if (foundOnCurrentBaud)
                        {
                            portFoundCount++;
                            break;
                        }
                    }
                }
                finally
                {
                    if (portBudgetExceeded)
                    {
                        Logger.LogWarning(
                            "串口 {PortName} 扫描整体超时（>45s），可能驱动/硬件无应答，已强制放弃",
                            portConfig.PortName
                        );
                        await NotifyProgressAsync(
                            new MotorScanProgressDto
                            {
                                Kind = MotorScanProgressKind.PortError,
                                Message =
                                    $"串口 {portConfig.PortName} 整体扫描超时（>45s），已放弃",
                                SerialPortConfigId = portConfig.Id,
                                PortName = portConfig.PortName,
                                TotalPorts = totalPorts,
                                FinishedPorts = counters.FinishedPorts,
                                TriedCount = counters.TriedCount,
                                FoundCount = counters.FoundCount,
                                ElapsedMs = stopwatch.ElapsedMilliseconds,
                            }
                        );
                    }

                    int finishedSnapshot = counters.IncrementFinishedPorts();
                    await NotifyProgressAsync(
                        new MotorScanProgressDto
                        {
                            Kind = MotorScanProgressKind.PortFinished,
                            Message =
                                portFoundCount > 0
                                    ? $"串口 {portConfig.PortName} 扫描完成，发现设备"
                                    : $"串口 {portConfig.PortName} 扫描完成，未发现设备",
                            SerialPortConfigId = portConfig.Id,
                            PortName = portConfig.PortName,
                            Found = portFoundCount > 0,
                            TotalPorts = totalPorts,
                            FinishedPorts = finishedSnapshot,
                            TriedCount = counters.TriedCount,
                            FoundCount = counters.FoundCount,
                            ElapsedMs = stopwatch.ElapsedMilliseconds,
                        }
                    );
                }
            })
        );

        await Task.WhenAll(scanTasks);

        // 探测阶段结束，回到主线程串行写库，避免 EF Core DbContext 并发问题
        // 用 AsNoTracking 只查最大 AxisIndex，避免把全量实体加入追踪器
        // 否则后续 FindBySlaveIdAsync（AsNoTracking）→ UpdateAsync（Attach）会触发追踪冲突
        int? maxAxisIndex = await _motorAxisRepository.GetMaxAxisIndexAsync();
        int nextAxisIndex = (maxAxisIndex ?? -1) + 1;
        List<DiscoveredMotorDeviceDto> discovered = new();

        foreach (
            ScanCandidate candidate in candidates
                .OrderBy(item => item.PortConfig.PortName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.SlaveId)
        )
        {
            Guid axisId = await UpsertDiscoveredAxisAsync(
                candidate.PortConfig,
                candidate.BaudRate,
                candidate.SlaveId,
                candidate.Brand,
                candidate.Status,
                () => nextAxisIndex++
            );
            discovered.Add(
                ToDiscoveredDto(
                    candidate.PortConfig,
                    candidate.BaudRate,
                    candidate.SlaveId,
                    candidate.Brand,
                    axisId
                )
            );
        }

        await ReinitializeMotorControlAsync();
        stopwatch.Stop();

        await NotifyProgressAsync(
            new MotorScanProgressDto
            {
                Kind = MotorScanProgressKind.Completed,
                Message = $"扫描完成，尝试 {counters.TriedCount} 次，发现 {discovered.Count} 台",
                TotalPorts = totalPorts,
                FinishedPorts = counters.FinishedPorts,
                TriedCount = counters.TriedCount,
                FoundCount = discovered.Count,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
            }
        );

        return new ScanMotorDevicesResultDto
        {
            TriedCount = counters.TriedCount,
            FoundCount = discovered.Count,
            ElapsedMs = stopwatch.ElapsedMilliseconds,
            Items = discovered,
        };
    }

    /// <summary>
    /// 按协议范围对指定品牌进行从机地址扫描，将命中结果加入候选集合并推送进度。
    /// </summary>
    private async Task<bool> ProbeRangeAsync(
        IRS485Port port,
        SerialPortConfig portConfig,
        int baudRate,
        ScanMotorDevicesInput input,
        ConcurrentBag<ScanCandidate> candidates,
        MotorBrand brand,
        Func<IRS485Port, int, Task<ProbeAttemptResult>> probeFunc,
        int protocolMinSlaveId,
        int protocolMaxSlaveId,
        ScanCounters counters,
        Stopwatch stopwatch,
        int totalPorts
    )
    {
        (int startSlaveId, int endSlaveId) = GetClampedScanRange(
            input.StartSlaveId,
            input.EndSlaveId,
            protocolMinSlaveId,
            protocolMaxSlaveId
        );

        bool anyFound = false;
        for (int slaveId = startSlaveId; slaveId <= endSlaveId; slaveId++)
        {
            int triedSnapshot = counters.IncrementTried();

            ProbeAttemptResult probeResult = await probeFunc(port, slaveId);
            MotorStatus? status = probeResult.Status;
            int foundSnapshot = counters.FoundCount;

            if (status is not null)
            {
                foundSnapshot = counters.IncrementFound();
                anyFound = true;
                candidates.Add(new ScanCandidate(portConfig, baudRate, slaveId, brand, status));

                await NotifyProgressAsync(
                    new MotorScanProgressDto
                    {
                        Kind = MotorScanProgressKind.DeviceFound,
                        Message =
                            $"{portConfig.PortName} @ {baudRate} ID={slaveId} 发现 {GetBrandText(brand)}"
                            + $" | TX={probeResult.TxHex} | RX={probeResult.RxHex}",
                        SerialPortConfigId = portConfig.Id,
                        PortName = portConfig.PortName,
                        BaudRate = baudRate,
                        SlaveId = slaveId,
                        Brand = GetBrandText(brand),
                        Found = true,
                        TxHex = probeResult.TxHex,
                        RxHex = probeResult.RxHex,
                        TotalPorts = totalPorts,
                        FinishedPorts = counters.FinishedPorts,
                        TriedCount = triedSnapshot,
                        FoundCount = foundSnapshot,
                        ElapsedMs = stopwatch.ElapsedMilliseconds,
                    }
                );
            }
            else
            {
                await NotifyProgressAsync(
                    new MotorScanProgressDto
                    {
                        Kind = MotorScanProgressKind.Probing,
                        Message =
                            $"{portConfig.PortName} @ {baudRate} ID={slaveId} 无响应"
                            + $" | TX={probeResult.TxHex}"
                            + (
                                string.IsNullOrWhiteSpace(probeResult.RxHex)
                                    ? string.Empty
                                    : $" | RX={probeResult.RxHex}"
                            )
                            + (
                                string.IsNullOrWhiteSpace(probeResult.ErrorMessage)
                                    ? string.Empty
                                    : $" | 错误={probeResult.ErrorMessage}"
                            ),
                        SerialPortConfigId = portConfig.Id,
                        PortName = portConfig.PortName,
                        BaudRate = baudRate,
                        SlaveId = slaveId,
                        Brand = GetBrandText(brand),
                        Found = false,
                        TxHex = probeResult.TxHex,
                        RxHex = probeResult.RxHex,
                        ErrorMessage = probeResult.ErrorMessage,
                        TotalPorts = totalPorts,
                        FinishedPorts = counters.FinishedPorts,
                        TriedCount = triedSnapshot,
                        FoundCount = foundSnapshot,
                        ElapsedMs = stopwatch.ElapsedMilliseconds,
                    }
                );
            }

            await Task.Delay(ScanProbeIntervalMs);
        }

        return anyFound;
    }

    /// <summary>
    /// 包裹一次进度推送，吞掉推送异常，确保扫描主流程不被影响。
    /// </summary>
    private async Task NotifyProgressAsync(MotorScanProgressDto progress)
    {
        try
        {
            await _scanProgressNotifier.NotifyAsync(progress);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "推送电机扫描进度失败：{Message}", ex.Message);
        }
    }

    /// <summary>
    /// 并行探测阶段产生的候选结果（仅用于扫描内部，不暴露到 DTO）。
    /// </summary>
    private sealed record ScanCandidate(
        SerialPortConfig PortConfig,
        int BaudRate,
        int SlaveId,
        MotorBrand Brand,
        MotorStatus Status
    );

    /// <summary>
    /// 单次探测结果（包含业务状态与串口帧详情）。
    /// </summary>
    private sealed record ProbeAttemptResult(
        MotorStatus? Status,
        string TxHex,
        string? RxHex,
        string? ErrorMessage
    );

    /// <summary>
    /// 跨线程共享的扫描计数器，使用 Interlocked 保证读写安全。
    /// </summary>
    private sealed class ScanCounters
    {
        private int _tried;
        private int _found;
        private int _finishedPorts;

        public int TriedCount => Volatile.Read(ref _tried);

        public int FoundCount => Volatile.Read(ref _found);

        public int FinishedPorts => Volatile.Read(ref _finishedPorts);

        public int IncrementTried() => Interlocked.Increment(ref _tried);

        public int IncrementFound() => Interlocked.Increment(ref _found);

        public int IncrementFinishedPorts() => Interlocked.Increment(ref _finishedPorts);
    }

    /// <inheritdoc/>
    public async Task<MotorAxisDto> UpdateAsync(Guid id, UpdateMotorAxisDto input)
    {
        EnsureManualOrMaintenanceMode();
        MotorAxis axis = await _motorAxisRepository.GetAsync(id);
        axis.SetName(input.Name);
        axis.SetDescription(input.Description);
        axis.SetEnabled(input.IsEnabled);
        axis.SetModel(input.Model);
        await _motorAxisRepository.UpdateAsync(axis);
        await ReinitializeMotorControlAsync();
        return await GetAsync(id);
    }

    /// <inheritdoc/>
    public async Task<MotorAxisDto> SetRotationAngleRangeAsync(
        Guid id,
        SetMotorRotationAngleRangeDto input
    )
    {
        EnsureManualOrMaintenanceMode();
        MotorAxis axis = await _motorAxisRepository.GetAsync(id);
        EnsureKtechAxis(axis);
        SetRotationAngleRange(axis, input.MinRotationAngle, input.MaxRotationAngle);
        await _motorAxisRepository.UpdateAsync(axis);
        return await GetAsync(id);
    }

    /// <inheritdoc/>
    public async Task<MotorAxisDto> SetRotationAngleLimitFromCurrentAsync(
        Guid id,
        SetMotorRotationAngleLimitFromCurrentDto input
    )
    {
        EnsureManualOrMaintenanceMode();
        MotorAxis axis = await PrepareAxisForCommandAsync(id);
        EnsureKtechAxis(axis);

        MotorStatus status = await _motorControlService.QueryStatusAsync(axis.SlaveId);
        long currentSingleTurnAngle = NormalizeKtechSingleTurnAngle(status.CurrentPosition);

        switch (input.LimitKind)
        {
            case MotorRotationAngleLimitKind.Minimum:
                SetRotationAngleRange(axis, currentSingleTurnAngle, axis.MaxRotationAngle);
                break;
            case MotorRotationAngleLimitKind.Maximum:
                SetRotationAngleRange(axis, axis.MinRotationAngle, currentSingleTurnAngle);
                break;
            default:
                throw new UserFriendlyException("请选择要设置的角度边界");
        }

        axis.UpdateStatus(
            ToDeviceStatus(status),
            status.CurrentPosition,
            status.CurrentSpeed,
            status.IsHomed
        );
        await _motorAxisRepository.UpdateAsync(axis);
        return await GetAsync(id);
    }

    /// <inheritdoc/>
    public async Task<MotorAxisDto> RefreshStatusAsync(Guid id)
    {
        EnsureManualOrMaintenanceMode();
        MotorAxis axis = await _motorAxisRepository.GetAsync(id);
        await RefreshStatusCoreAsync(axis, throwOnFailure: true);
        return await GetAsync(id);
    }

    /// <inheritdoc/>
    public async Task<MotorAxisDto> EnableAsync(Guid id)
    {
        EnsureManualOrMaintenanceMode();
        MotorAxis axis = await PrepareAxisForCommandAsync(id);
        await _motorControlService.EnableAsync(axis.SlaveId);
        return await RefreshStatusAsync(id);
    }

    /// <inheritdoc/>
    public async Task<MotorAxisDto> DisableAsync(Guid id)
    {
        EnsureManualOrMaintenanceMode();
        MotorAxis axis = await PrepareAxisForCommandAsync(id);
        await _motorControlService.DisableAsync(axis.SlaveId);
        return await RefreshStatusAsync(id);
    }

    /// <inheritdoc/>
    public async Task<MotorAxisDto> MoveAbsoluteAsync(Guid id, MoveMotorInput input)
    {
        EnsureManualOrMaintenanceMode();
        MotorAxis axis = await PrepareAxisForCommandAsync(id);
        await _motorControlService.MoveAbsoluteAsync(axis.SlaveId, input.Position, input.SpeedRpm);
        return await RefreshStatusAsync(id);
    }

    /// <inheritdoc/>
    public async Task<MotorAxisDto> MoveRelativeAsync(Guid id, MoveMotorInput input)
    {
        EnsureManualOrMaintenanceMode();
        MotorAxis axis = await PrepareAxisForCommandAsync(id);
        await _motorControlService.MoveRelativeAsync(axis.SlaveId, input.Position, input.SpeedRpm);
        return await RefreshStatusAsync(id);
    }

    /// <inheritdoc/>
    public async Task<MotorAxisDto> StopAsync(Guid id)
    {
        EnsureManualOrMaintenanceMode();
        MotorAxis axis = await PrepareAxisForCommandAsync(id);
        await _motorControlService.StopAsync(axis.SlaveId);
        return await RefreshStatusAsync(id);
    }

    /// <inheritdoc/>
    public async Task<MotorAxisDto> EmergencyStopAsync(Guid id)
    {
        EnsureManualOrMaintenanceMode();
        MotorAxis axis = await PrepareAxisForCommandAsync(id);
        await _motorControlService.EmergencyStopAsync(axis.SlaveId);
        return await RefreshStatusAsync(id);
    }

    /// <inheritdoc/>
    public async Task<MotorAxisDto> HomeAsync(Guid id)
    {
        EnsureManualOrMaintenanceMode();
        MotorAxis axis = await PrepareAxisForCommandAsync(id);
        await _motorControlService.HomeAsync(axis.SlaveId);
        return await RefreshStatusAsync(id);
    }

    /// <inheritdoc/>
    public async Task<MotorAxisDto> ClearFaultAsync(Guid id)
    {
        EnsureManualOrMaintenanceMode();
        MotorAxis axis = await PrepareAxisForCommandAsync(id);
        await _motorControlService.ClearFaultAsync(axis.SlaveId);
        return await RefreshStatusAsync(id);
    }

    /// <inheritdoc/>
    public async Task<RegisterReadResultDto> ReadHoldingRegistersAsync(
        Guid id,
        ReadHoldingRegistersInput input
    )
    {
        EnsureManualOrMaintenanceMode();
        MotorAxis axis = await _motorAxisRepository.GetAsync(id);
        SerialPortConfig portConfig = await _serialPortConfigRepository.GetAsync(
            axis.SerialPortConfigId
        );
        byte[] request = ModbusRtuHelper.BuildReadHoldingRegisters(
            (byte)axis.SlaveId,
            input.StartAddress,
            input.Quantity
        );
        byte[] response = await _motorControlService.SendRawAsync(
            portConfig,
            request,
            ModbusRtuHelper.GetReadResponseLength(input.Quantity),
            500
        );
        ushort[] values = ModbusRtuHelper.ParseReadHoldingRegisters(response, (byte)axis.SlaveId);
        return new RegisterReadResultDto
        {
            RequestHex = Convert.ToHexString(request),
            ResponseHex = Convert.ToHexString(response),
            Values = values.ToList(),
        };
    }

    /// <inheritdoc/>
    public async Task<bool> WriteSingleRegisterAsync(Guid id, WriteSingleRegisterInput input)
    {
        EnsureManualOrMaintenanceMode();
        MotorAxis axis = await _motorAxisRepository.GetAsync(id);
        SerialPortConfig portConfig = await _serialPortConfigRepository.GetAsync(
            axis.SerialPortConfigId
        );
        byte[] request = ModbusRtuHelper.BuildWriteSingleRegister(
            (byte)axis.SlaveId,
            input.Address,
            input.Value
        );
        await _motorControlService.SendRawAsync(
            portConfig,
            request,
            ModbusRtuHelper.GetWriteResponseLength(),
            500
        );
        return true;
    }

    /// <summary>
    /// 校验当前运行模式必须为手动或检修，否则抛出业务异常。
    /// </summary>
    private void EnsureManualOrMaintenanceMode()
    {
        DeviceRunMode mode = _deviceStateManager.RunMode;
        if (mode is not (DeviceRunMode.Manual or DeviceRunMode.Maintenance))
        {
            throw new UserFriendlyException(
                $"当前运行模式为【{mode switch {
                    DeviceRunMode.Online => "联机",
                    DeviceRunMode.Auto   => "自动",
                    _                    => mode.ToString()
                }}】，电机操作仅允许在手动模式或检修模式下执行"
            );
        }
    }

    /// <summary>
    /// 获取电机扫描波特率顺序。
    /// </summary>
    private static int[] GetMotorScanBaudRates(IReadOnlyCollection<int> inputBaudRates)
    {
        if (inputBaudRates.Count == 0)
        {
            return DefaultMotorScanBaudRates.ToArray();
        }

        return inputBaudRates
            .Distinct()
            .OrderBy(GetMotorScanBaudRatePriority)
            .ThenBy(baudRate => baudRate)
            .ToArray();
    }

    /// <summary>
    /// 获取电机扫描波特率优先级。
    /// </summary>
    private static int GetMotorScanBaudRatePriority(int baudRate)
    {
        int priority = Array.IndexOf(DefaultMotorScanBaudRates, baudRate);
        return priority < 0 ? DefaultMotorScanBaudRates.Length : priority;
    }

    /// <summary>
    /// 按协议允许范围裁剪扫描从机地址。
    /// </summary>
    private static (int StartSlaveId, int EndSlaveId) GetClampedScanRange(
        int requestedStartSlaveId,
        int requestedEndSlaveId,
        int protocolMinSlaveId,
        int protocolMaxSlaveId
    )
    {
        int startSlaveId = Math.Max(requestedStartSlaveId, protocolMinSlaveId);
        int endSlaveId = Math.Min(requestedEndSlaveId, protocolMaxSlaveId);
        return (startSlaveId, endSlaveId);
    }

    /// <summary>
    /// 同步系统串口到数据库。
    /// </summary>
    private async Task SyncSystemSerialPortsAsync()
    {
        foreach (string portName in SerialPort.GetPortNames())
        {
            SerialPortConfig? existing = await _serialPortConfigRepository.FindByPortNameAsync(
                portName
            );
            if (existing is null)
            {
                SerialPortConfig config = new(
                    GuidGenerator.Create(),
                    $"串口 {portName}",
                    portName,
                    baudRate: 0
                );
                await _serialPortConfigRepository.InsertAsync(config);
            }
        }
    }

    /// <summary>
    /// 探测雷赛 Modbus RTU 状态寄存器。
    /// </summary>
    private static async Task<ProbeAttemptResult> ProbeLeisaiAsync(
        IRS485Port port,
        int slaveId,
        int timeoutMs
    )
    {
        byte[] request = ModbusRtuHelper.BuildReadHoldingRegisters((byte)slaveId, 0x1003, 1);
        string txHex = ToSpacedHex(request);
        string? rxHex = null;

        try
        {
            byte[] response = await port.SendAndReceiveAsync(
                request,
                ModbusRtuHelper.GetReadResponseLength(1),
                timeoutMs
            );
            rxHex = ToSpacedHex(response);
            ushort[] registers = ModbusRtuHelper.ParseReadHoldingRegisters(response, (byte)slaveId);
            ushort statusWord = registers[0];
            return new ProbeAttemptResult(
                new MotorStatus
                {
                    MotorId = slaveId,
                    Brand = "雷赛iCL-RS",
                    IsEnabled = (statusWord & 0x0002) != 0,
                    IsMoving = (statusWord & 0x0004) != 0,
                    HasFault = (statusWord & 0x0008) != 0,
                    IsHomed = (statusWord & 0x0010) != 0,
                    RawStatusWord = statusWord,
                },
                txHex,
                rxHex,
                null
            );
        }
        catch (Exception ex)
        {
            return new ProbeAttemptResult(null, txHex, rxHex, ex.Message);
        }
    }

    /// <summary>
    /// 探测瓴控 KTECH 查询状态帧。
    /// </summary>
    private static async Task<ProbeAttemptResult> ProbeKtechAsync(
        IRS485Port port,
        int slaveId,
        int timeoutMs
    )
    {
        byte[] request = KtechFrame.BuildQueryStatusFrame((byte)slaveId);
        string txHex = ToSpacedHex(request);
        string? rxHex = null;

        try
        {
            byte[] response = await port.SendAndReceiveAsync(
                request,
                KtechFrame.QueryResponseLength,
                timeoutMs
            );
            rxHex = ToSpacedHex(response);
            KtechStatusFrame frame = KtechFrame.ParseQueryStatusResponse(response, (byte)slaveId);
            return new ProbeAttemptResult(
                new MotorStatus
                {
                    MotorId = slaveId,
                    Brand = "瓴控KTECH",
                    IsEnabled = frame.IsEnabled,
                    IsMoving = frame.IsMoving,
                    HasFault = frame.HasFault,
                    CurrentPosition = frame.PositionRaw,
                    CurrentSpeed = frame.SpeedRaw,
                    RawStatusWord = frame.StatusFlags,
                },
                txHex,
                rxHex,
                null
            );
        }
        catch (Exception ex)
        {
            return new ProbeAttemptResult(null, txHex, rxHex, ex.Message);
        }
    }

    private static string ToSpacedHex(ReadOnlySpan<byte> data)
    {
        return data.Length == 0
            ? string.Empty
            : string.Join(" ", data.ToArray().Select(static b => b.ToString("X2")));
    }

    /// <summary>
    /// 写入或更新扫描发现的电机轴，并先保存从设备读到的状态。
    /// </summary>
    private async Task<Guid> UpsertDiscoveredAxisAsync(
        SerialPortConfig portConfig,
        int baudRate,
        int slaveId,
        MotorBrand brand,
        MotorStatus status,
        Func<int> nextAxisIndexFactory
    )
    {
        if (portConfig.BaudRate != baudRate)
        {
            portConfig.SetParameters(baudRate, 8, SerialPortParity.None, SerialPortStopBits.One);
            await _serialPortConfigRepository.UpdateAsync(portConfig);
        }

        MotorAxis? axis = await _motorAxisRepository.FindBySlaveIdAsync(portConfig.Id, slaveId);
        if (axis is null)
        {
            axis = new MotorAxis(
                GuidGenerator.Create(),
                $"{GetBrandText(brand)} #{slaveId}",
                nextAxisIndexFactory(),
                portConfig.Id,
                slaveId,
                brand
            );
            axis.SetModel(GetBrandText(brand));
            axis.UpdateStatus(
                ToDeviceStatus(status),
                status.CurrentPosition,
                status.CurrentSpeed,
                status.IsHomed
            );
            await _motorAxisRepository.InsertAsync(axis);
        }
        else
        {
            axis.SetModel(axis.Model ?? GetBrandText(brand));
            axis.UpdateStatus(
                ToDeviceStatus(status),
                status.CurrentPosition,
                status.CurrentSpeed,
                status.IsHomed
            );
            await _motorAxisRepository.UpdateAsync(axis);
        }

        return axis.Id;
    }

    /// <summary>
    /// 准备电机命令运行态，必要时从数据库重建电机控制服务。
    /// 同时检等并获取设备独占会话，防止多标签页并发操作电机。
    /// </summary>
    private async Task<MotorAxis> PrepareAxisForCommandAsync(Guid id)
    {
        MotorAxis axis = await _motorAxisRepository.GetAsync(id);

        // 设备独占会话检查：防止多标签页并发操作同一台电机
        string? clientSessionId = _currentClientSession.SessionId;
        if (!string.IsNullOrWhiteSpace(clientSessionId))
        {
            string? userId = CurrentUser.Id?.ToString();
            string userName =
                CurrentUser.Name ?? CurrentUser.UserName ?? clientSessionId[..8] + "...";
            _sessionManager.TryAcquire(
                id,
                DeviceType.Motor,
                clientSessionId,
                userId,
                userName,
                force: false
            );
        }

        if (!_motorControlService.IsMotorConfigured(axis.SlaveId))
        {
            await ReinitializeMotorControlAsync();
        }

        SerialPortConfig portConfig = await _serialPortConfigRepository.GetAsync(
            axis.SerialPortConfigId
        );
        if (!_motorControlService.IsSerialPortOpen(axis.SerialPortConfigId))
        {
            await _motorControlService.OpenSerialPortAsync(portConfig);
        }

        return axis;
    }

    /// <summary>
    /// 从设备读取状态，写入数据库。
    /// </summary>
    private async Task RefreshStatusCoreAsync(MotorAxis axis, bool throwOnFailure)
    {
        try
        {
            if (!_motorControlService.IsMotorConfigured(axis.SlaveId))
            {
                await ReinitializeMotorControlAsync();
            }

            MotorStatus status = await _motorControlService.QueryStatusAsync(axis.SlaveId);
            axis.UpdateStatus(
                ToDeviceStatus(status),
                status.CurrentPosition,
                status.CurrentSpeed,
                status.IsHomed
            );
            await _motorAxisRepository.UpdateAsync(axis);
        }
        catch
        {
            axis.UpdateStatus(
                MotorDeviceStatus.Offline,
                axis.LastKnownPosition,
                axis.LastKnownSpeed,
                axis.IsHomed
            );
            await _motorAxisRepository.UpdateAsync(axis);
            if (throwOnFailure)
            {
                throw;
            }
        }
    }

    /// <summary>
    /// 从数据库重新初始化串口和电机驱动映射。
    /// </summary>
    private async Task ReinitializeMotorControlAsync()
    {
        List<SerialPortConfig> ports = await _serialPortConfigRepository.GetListAsync(
            isEnabled: true
        );
        List<MotorAxis> axes = await _motorAxisRepository.GetEnabledListAsync();
        _motorControlService.Initialize(ports, axes);
        _motorControlService.SetAxisIdMapping(
            axes.GroupBy(axis => axis.SlaveId)
                .ToDictionary(group => group.Key, group => group.First().Id)
        );
    }

    /// <summary>
    /// 转换扫描发现结果 DTO。
    /// </summary>
    private static DiscoveredMotorDeviceDto ToDiscoveredDto(
        SerialPortConfig portConfig,
        int baudRate,
        int slaveId,
        MotorBrand brand,
        Guid axisId
    )
    {
        return new DiscoveredMotorDeviceDto
        {
            SerialPortConfigId = portConfig.Id,
            PortName = portConfig.PortName,
            BaudRate = baudRate,
            SlaveId = slaveId,
            Brand = brand,
            BrandText = GetBrandText(brand),
            MotorAxisId = axisId,
        };
    }

    /// <summary>
    /// 转换电机轴 DTO。
    /// </summary>
    private MotorAxisDto ToDto(MotorAxis axis, SerialPortConfig? portConfig)
    {
        return new MotorAxisDto
        {
            Id = axis.Id,
            Name = axis.Name,
            AxisIndex = axis.AxisIndex,
            Description = axis.Description,
            IsEnabled = axis.IsEnabled,
            SerialPortConfigId = axis.SerialPortConfigId,
            SerialPortDisplayName = portConfig?.DisplayName ?? string.Empty,
            PortName = portConfig?.PortName ?? string.Empty,
            BaudRate = portConfig?.BaudRate ?? 0,
            IsSerialPortOpen = _motorControlService.IsSerialPortOpen(axis.SerialPortConfigId),
            SlaveId = axis.SlaveId,
            Brand = axis.Brand,
            BrandText = GetBrandText(axis.Brand),
            Model = axis.Model,
            Status = axis.Status,
            StatusText = GetStatusText(axis.Status),
            IsHomed = axis.IsHomed,
            LastStatusUpdateAt = axis.LastStatusUpdateAt,
            MinRotationAngle = axis.MinRotationAngle,
            MaxRotationAngle = axis.MaxRotationAngle,
        };
    }

    /// <summary>
    /// 校验当前电机是否为瓴控协议。
    /// </summary>
    private static void EnsureKtechAxis(MotorAxis axis)
    {
        if (axis.Brand != MotorBrand.KtechKtech)
        {
            throw new UserFriendlyException("旋转角度范围设置仅支持瓴控KTECH电机");
        }
    }

    /// <summary>
    /// 设置旋转角度范围，并将领域校验异常转换为用户友好提示。
    /// </summary>
    private static void SetRotationAngleRange(
        MotorAxis axis,
        long? minRotationAngle,
        long? maxRotationAngle
    )
    {
        try
        {
            axis.SetRotationAngleRange(minRotationAngle, maxRotationAngle);
        }
        catch (ArgumentException ex)
        {
            throw new UserFriendlyException(ex.Message);
        }
    }

    /// <summary>
    /// 将瓴控当前位置归一化为单圈角度。
    /// </summary>
    private static long NormalizeKtechSingleTurnAngle(long position)
    {
        long angle = position % KtechSingleTurnAngleUnits;
        return angle < 0 ? angle + KtechSingleTurnAngleUnits : angle;
    }

    /// <summary>
    /// 将底层状态转换为领域状态。
    /// </summary>
    private static MotorDeviceStatus ToDeviceStatus(MotorStatus status)
    {
        if (status.HasFault)
        {
            return MotorDeviceStatus.Faulted;
        }
        if (status.IsMoving)
        {
            return MotorDeviceStatus.Moving;
        }
        if (status.IsEnabled)
        {
            return MotorDeviceStatus.Enabled;
        }
        return MotorDeviceStatus.Online;
    }

    /// <summary>
    /// 获取品牌显示文本。
    /// </summary>
    private static string GetBrandText(MotorBrand brand)
    {
        return brand switch
        {
            MotorBrand.KtechKtech => "瓴控KTECH",
            MotorBrand.LeisaiIclRs => "雷赛iCL-RS",
            _ => brand.ToString(),
        };
    }

    /// <summary>
    /// 获取状态显示文本。
    /// </summary>
    private static string GetStatusText(MotorDeviceStatus status)
    {
        return status switch
        {
            MotorDeviceStatus.Unknown => "未知",
            MotorDeviceStatus.Offline => "离线",
            MotorDeviceStatus.Online => "在线",
            MotorDeviceStatus.Enabled => "已使能",
            MotorDeviceStatus.Moving => "运动中",
            MotorDeviceStatus.Faulted => "故障",
            _ => status.ToString(),
        };
    }

    // ─── 操作日志 ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<PagedResultDto<MotorOperationLogDto>> GetLogsAsync(GetMotorLogListDto input)
    {
        if (!input.MotorAxisId.HasValue)
            return new PagedResultDto<MotorOperationLogDto>(0, new List<MotorOperationLogDto>());

        Guid axisId = input.MotorAxisId.Value;
        long totalCount = await _operationLogRepository.GetCountAsync(
            axisId,
            input.OperationType,
            input.IsFailedOnly ?? false,
            input.StartTime,
            input.EndTime
        );
        List<MotorOperationLog> logs = await _operationLogRepository.GetPagedListAsync(
            axisId,
            input.SkipCount,
            input.MaxResultCount,
            input.OperationType,
            input.IsFailedOnly ?? false,
            input.StartTime,
            input.EndTime
        );
        return new PagedResultDto<MotorOperationLogDto>(
            totalCount,
            logs.Select(x => new MotorOperationLogDto
                {
                    Id = x.Id,
                    MotorAxisId = x.MotorAxisId,
                    SlaveId = x.SlaveId,
                    OperationType = x.OperationType,
                    OccurredAt = x.OccurredAt,
                    IsSuccess = x.IsSuccess,
                    CommandCode = x.CommandCode,
                    ParameterSummary = x.ParameterSummary,
                    ErrorMessage = x.ErrorMessage,
                    RoundTripMs = x.RoundTripMs,
                })
                .ToList()
        );
    }
}
