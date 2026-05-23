using System.Diagnostics;
using System.IO.Ports;
using AuroraStruct3D.Motors.Dtos;
using AuroraStruct3D.RS485;
using AuroraStruct3D.RS485.Ktech;
using AuroraStruct3D.RS485.Modbus;
using AuroraStruct3D.RS485.Protocol;
using AuroraStruct3D.SerialPorts;
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

    public MotorDeviceAppService(
        IMotorAxisRepository motorAxisRepository,
        ISerialPortConfigRepository serialPortConfigRepository,
        IMotorControlService motorControlService,
        ILoggerFactory loggerFactory
    )
    {
        _motorAxisRepository = motorAxisRepository;
        _serialPortConfigRepository = serialPortConfigRepository;
        _motorControlService = motorControlService;
        _loggerFactory = loggerFactory;
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
        if (input.StartSlaveId > input.EndSlaveId)
        {
            throw new UserFriendlyException("起始从机地址不能大于结束从机地址");
        }

        await SyncSystemSerialPortsAsync();
        List<SerialPortConfig> portConfigs = await _serialPortConfigRepository.GetListAsync();
        int[] baudRates = GetMotorScanBaudRates(input.BaudRates);

        Stopwatch stopwatch = Stopwatch.StartNew();
        int triedCount = 0;
        List<DiscoveredMotorDeviceDto> discovered = new();
        List<MotorAxis> allAxes = await _motorAxisRepository.GetListAsync();
        int nextAxisIndex = allAxes.Count == 0 ? 0 : allAxes.Max(axis => axis.AxisIndex) + 1;

        foreach (SerialPortConfig portConfig in portConfigs)
        {
            foreach (int baudRate in baudRates)
            {
                bool foundOnCurrentBaud = false;
                _motorControlService.CloseSerialPort(portConfig.Id);

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
                    probePort.Open();
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(
                        ex,
                        "串口 {PortName} 以 {BaudRate} 打开失败，跳过该串口",
                        portConfig.PortName,
                        baudRate
                    );
                    break;
                }

                (int ktechStartSlaveId, int ktechEndSlaveId) = GetClampedScanRange(
                    input.StartSlaveId,
                    input.EndSlaveId,
                    KtechMinSlaveId,
                    KtechMaxSlaveId
                );
                for (int slaveId = ktechStartSlaveId; slaveId <= ktechEndSlaveId; slaveId++)
                {
                    triedCount++;
                    MotorStatus? ktechStatus = await ProbeKtechAsync(
                        probePort,
                        slaveId,
                        input.ProbeTimeoutMs
                    );
                    if (ktechStatus is not null)
                    {
                        Guid axisId = await UpsertDiscoveredAxisAsync(
                            portConfig,
                            baudRate,
                            slaveId,
                            MotorBrand.KtechKtech,
                            ktechStatus,
                            () => nextAxisIndex++
                        );
                        discovered.Add(
                            ToDiscoveredDto(
                                portConfig,
                                baudRate,
                                slaveId,
                                MotorBrand.KtechKtech,
                                axisId
                            )
                        );
                        foundOnCurrentBaud = true;
                    }

                    await Task.Delay(ScanProbeIntervalMs);
                }

                (int leisaiStartSlaveId, int leisaiEndSlaveId) = GetClampedScanRange(
                    input.StartSlaveId,
                    input.EndSlaveId,
                    LeisaiMinSlaveId,
                    LeisaiMaxSlaveId
                );
                for (int slaveId = leisaiStartSlaveId; slaveId <= leisaiEndSlaveId; slaveId++)
                {
                    triedCount++;
                    MotorStatus? leisaiStatus = await ProbeLeisaiAsync(
                        probePort,
                        slaveId,
                        input.ProbeTimeoutMs
                    );
                    if (leisaiStatus is not null)
                    {
                        Guid axisId = await UpsertDiscoveredAxisAsync(
                            portConfig,
                            baudRate,
                            slaveId,
                            MotorBrand.LeisaiIclRs,
                            leisaiStatus,
                            () => nextAxisIndex++
                        );
                        discovered.Add(
                            ToDiscoveredDto(
                                portConfig,
                                baudRate,
                                slaveId,
                                MotorBrand.LeisaiIclRs,
                                axisId
                            )
                        );
                        foundOnCurrentBaud = true;
                    }

                    await Task.Delay(ScanProbeIntervalMs);
                }

                if (foundOnCurrentBaud)
                {
                    break;
                }
            }
        }

        await ReinitializeMotorControlAsync();
        stopwatch.Stop();

        return new ScanMotorDevicesResultDto
        {
            TriedCount = triedCount,
            FoundCount = discovered.Count,
            ElapsedMs = stopwatch.ElapsedMilliseconds,
            Items = discovered,
        };
    }

    /// <inheritdoc/>
    public async Task<MotorAxisDto> UpdateAsync(Guid id, UpdateMotorAxisDto input)
    {
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
        MotorAxis axis = await _motorAxisRepository.GetAsync(id);
        await RefreshStatusCoreAsync(axis, throwOnFailure: true);
        return await GetAsync(id);
    }

    /// <inheritdoc/>
    public async Task<MotorAxisDto> EnableAsync(Guid id)
    {
        MotorAxis axis = await PrepareAxisForCommandAsync(id);
        await _motorControlService.EnableAsync(axis.SlaveId);
        return await RefreshStatusAsync(id);
    }

    /// <inheritdoc/>
    public async Task<MotorAxisDto> DisableAsync(Guid id)
    {
        MotorAxis axis = await PrepareAxisForCommandAsync(id);
        await _motorControlService.DisableAsync(axis.SlaveId);
        return await RefreshStatusAsync(id);
    }

    /// <inheritdoc/>
    public async Task<MotorAxisDto> MoveAbsoluteAsync(Guid id, MoveMotorInput input)
    {
        MotorAxis axis = await PrepareAxisForCommandAsync(id);
        await _motorControlService.MoveAbsoluteAsync(axis.SlaveId, input.Position, input.SpeedRpm);
        return await RefreshStatusAsync(id);
    }

    /// <inheritdoc/>
    public async Task<MotorAxisDto> MoveRelativeAsync(Guid id, MoveMotorInput input)
    {
        MotorAxis axis = await PrepareAxisForCommandAsync(id);
        await _motorControlService.MoveRelativeAsync(axis.SlaveId, input.Position, input.SpeedRpm);
        return await RefreshStatusAsync(id);
    }

    /// <inheritdoc/>
    public async Task<MotorAxisDto> StopAsync(Guid id)
    {
        MotorAxis axis = await PrepareAxisForCommandAsync(id);
        await _motorControlService.StopAsync(axis.SlaveId);
        return await RefreshStatusAsync(id);
    }

    /// <inheritdoc/>
    public async Task<MotorAxisDto> EmergencyStopAsync(Guid id)
    {
        MotorAxis axis = await PrepareAxisForCommandAsync(id);
        await _motorControlService.EmergencyStopAsync(axis.SlaveId);
        return await RefreshStatusAsync(id);
    }

    /// <inheritdoc/>
    public async Task<MotorAxisDto> HomeAsync(Guid id)
    {
        MotorAxis axis = await PrepareAxisForCommandAsync(id);
        await _motorControlService.HomeAsync(axis.SlaveId);
        return await RefreshStatusAsync(id);
    }

    /// <inheritdoc/>
    public async Task<MotorAxisDto> ClearFaultAsync(Guid id)
    {
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
    private static async Task<MotorStatus?> ProbeLeisaiAsync(
        IRS485Port port,
        int slaveId,
        int timeoutMs
    )
    {
        try
        {
            byte[] request = ModbusRtuHelper.BuildReadHoldingRegisters((byte)slaveId, 0x1003, 1);
            byte[] response = await port.SendAndReceiveAsync(
                request,
                ModbusRtuHelper.GetReadResponseLength(1),
                timeoutMs
            );
            ushort[] registers = ModbusRtuHelper.ParseReadHoldingRegisters(response, (byte)slaveId);
            ushort statusWord = registers[0];
            return new MotorStatus
            {
                MotorId = slaveId,
                Brand = "雷赛iCL-RS",
                IsEnabled = (statusWord & 0x0002) != 0,
                IsMoving = (statusWord & 0x0004) != 0,
                HasFault = (statusWord & 0x0008) != 0,
                IsHomed = (statusWord & 0x0010) != 0,
                RawStatusWord = statusWord,
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 探测瓴控 KTECH 查询状态帧。
    /// </summary>
    private static async Task<MotorStatus?> ProbeKtechAsync(
        IRS485Port port,
        int slaveId,
        int timeoutMs
    )
    {
        try
        {
            byte[] request = KtechFrame.BuildQueryStatusFrame((byte)slaveId);
            byte[] response = await port.SendAndReceiveAsync(
                request,
                KtechFrame.QueryResponseLength,
                timeoutMs
            );
            KtechStatusFrame frame = KtechFrame.ParseQueryStatusResponse(response, (byte)slaveId);
            return new MotorStatus
            {
                MotorId = slaveId,
                Brand = "瓴控KTECH",
                IsEnabled = frame.IsEnabled,
                IsMoving = frame.IsMoving,
                HasFault = frame.HasFault,
                CurrentPosition = frame.PositionRaw,
                CurrentSpeed = frame.SpeedRaw,
                RawStatusWord = frame.StatusFlags,
            };
        }
        catch
        {
            return null;
        }
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
    /// </summary>
    private async Task<MotorAxis> PrepareAxisForCommandAsync(Guid id)
    {
        MotorAxis axis = await _motorAxisRepository.GetAsync(id);
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
            LastKnownPosition = axis.LastKnownPosition,
            LastKnownSpeed = axis.LastKnownSpeed,
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
}
