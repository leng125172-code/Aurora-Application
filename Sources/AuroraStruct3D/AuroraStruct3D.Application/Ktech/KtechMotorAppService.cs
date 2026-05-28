using System.Buffers.Binary;
using AuroraStruct3D.Ktech.Dtos;
using AuroraStruct3D.Motors;
using AuroraStruct3D.RS485;
using AuroraStruct3D.RS485.Ktech;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Content;

namespace AuroraStruct3D.Ktech;

/// <summary>
/// 瓴控 KTECH 电机操作台应用服务实现。
/// 通过 <see cref="IMotorControlService"/> 获取已注册的 <see cref="KtechMotorDriver"/>，
/// 与现有 <c>MotorDeviceAppService</c> 解耦，只服务 KTECH 品牌轴。
/// </summary>
public class KtechMotorAppService : AuroraStruct3DAppService, IKtechMotorAppService
{
    private readonly IMotorAxisRepository _motorAxisRepository;
    private readonly IMotorControlService _motorControlService;
    private readonly IKtechMotorNotifier _notifier;
    private readonly KtechSamplerStateStore _samplerStateStore;
    private readonly ILogger<KtechMotorAppService> _logger;

    public KtechMotorAppService(
        IMotorAxisRepository motorAxisRepository,
        IMotorControlService motorControlService,
        IKtechMotorNotifier notifier,
        KtechSamplerStateStore samplerStateStore,
        ILogger<KtechMotorAppService> logger
    )
    {
        _motorAxisRepository = motorAxisRepository;
        _motorControlService = motorControlService;
        _notifier = notifier;
        _samplerStateStore = samplerStateStore;
        _logger = logger;
    }

    // ====== Info ======

    /// <inheritdoc/>
    public async Task<int> GetDeviceTypeAsync(Guid id)
    {
        (MotorAxis axis, KtechMotorDriver driver) = await ResolveAsync(id);
        KtechDeviceType deviceType = await driver.ReadDeviceTypeAsync();
        int code = (int)deviceType;
        axis.SetKtechDeviceInfo(deviceTypeCode: code);
        await _motorAxisRepository.UpdateAsync(axis, autoSave: true);
        return code;
    }

    /// <inheritdoc/>
    public async Task<KtechProductInfoDto> GetProductInfoAsync(Guid id)
    {
        (MotorAxis axis, KtechMotorDriver driver) = await ResolveAsync(id);

        // 同时读取设备类型，便于前端显示
        KtechDeviceType deviceType = await driver.ReadDeviceTypeAsync();
        KtechProductInfo info = await driver.ReadProductInfoAsync();

        // 回写持久化字段
        axis.SetKtechDeviceInfo(
            deviceTypeCode: (int)deviceType,
            driverName: info.DriverName,
            motorName: info.MotorName,
            chipId: info.ChipId,
            hardwareVersion: info.HardwareVersion,
            motorVersion: info.MotorVersion,
            firmwareVersion: info.FirmwareVersion
        );
        await _motorAxisRepository.UpdateAsync(axis, autoSave: true);

        return new KtechProductInfoDto
        {
            DeviceTypeCode = (int)deviceType,
            DeviceTypeName = deviceType.ToString(),
            DriverName = info.DriverName,
            MotorName = info.MotorName,
            ChipId = info.ChipId,
            HardwareVersion = info.HardwareVersion,
            MotorVersion = info.MotorVersion,
            FirmwareVersion = info.FirmwareVersion,
        };
    }

    /// <inheritdoc/>
    public async Task<KtechCapabilitiesDto> GetCapabilitiesAsync(Guid id)
    {
        // 尽量不触发硬件读取：直接基于实体已保存的 DeviceTypeCode 推导能力。
        // 实体里没有则降级为"未知型号全开放"。
        MotorAxis axis = await _motorAxisRepository.GetAsync(id);
        int code = axis.KtechDeviceTypeCode ?? 0;
        return KtechCapabilityProvider.Get(code);
    }

    /// <inheritdoc/>
    public async Task ConnectAsync(Guid id)
    {
        (_, KtechMotorDriver driver) = await ResolveAsync(id);
        await driver.ConnectAsync();
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync(Guid id)
    {
        (_, KtechMotorDriver driver) = await ResolveAsync(id);
        await driver.DisconnectAsync();
    }

    /// <inheritdoc/>
    public async Task RebootDeviceAsync(Guid id)
    {
        (_, KtechMotorDriver driver) = await ResolveAsync(id);
        await driver.RebootAsync();
    }

    // ====== Calib ======

    /// <inheritdoc/>
    public async Task<KtechCalibDto> GetCalibAsync(Guid id)
    {
        (_, KtechMotorDriver driver) = await ResolveAsync(id);
        IKtechCalib calib = await driver.ReadCalibAsync();
        return MapToCalibDto(calib);
    }

    /// <inheritdoc/>
    public async Task UpdateCalibAsync(Guid id, KtechCalibDto input)
    {
        Check.NotNull(input, nameof(input));
        (_, KtechMotorDriver driver) = await ResolveAsync(id);

        // 先读一次以保留未变的字段（如 SavedFlag、MG 专有字段）
        IKtechCalib current = await driver.ReadCalibAsync();
        IKtechCalib merged = MergeCalibFromDto(current, input);
        await driver.WriteCalibAsync(merged);
    }

    /// <inheritdoc/>
    public async Task<KtechCalibDto> AlignMotorEncoderAsync(Guid id)
    {
        (_, KtechMotorDriver driver) = await ResolveAsync(id);
        _ = await driver.CalibrateAsync();
        // 对齐后立即读取完整 Calib 给前端
        IKtechCalib calib = await driver.ReadCalibAsync();
        return MapToCalibDto(calib);
    }

    /// <inheritdoc/>
    public async Task<KtechCalibDto> SetEncoderZeroAsync(Guid id)
    {
        (_, KtechMotorDriver driver) = await ResolveAsync(id);
        _ = await driver.SetEncoderOffsetAsync();
        IKtechCalib calib = await driver.ReadCalibAsync();
        return MapToCalibDto(calib);
    }

    /// <inheritdoc/>
    public async Task ResetCalibAsync(Guid id)
    {
        (_, KtechMotorDriver driver) = await ResolveAsync(id);
        await driver.ResetCalibAsync();
    }

    // ====== Setting ======

    /// <inheritdoc/>
    public async Task<KtechSettingDto> GetSettingAsync(Guid id)
    {
        (_, KtechMotorDriver driver) = await ResolveAsync(id);
        KtechSetting setting = await driver.ReadSettingAsync();
        return MapToSettingDto(setting);
    }

    /// <inheritdoc/>
    public async Task UpdateSettingAsync(Guid id, KtechSettingDto input)
    {
        Check.NotNull(input, nameof(input));
        (_, KtechMotorDriver driver) = await ResolveAsync(id);

        KtechSetting current = await driver.ReadSettingAsync();
        MergeSettingFromDto(current, input);
        await driver.WriteSettingAsync(current);
    }

    /// <inheritdoc/>
    public async Task PersistSettingAsync(Guid id)
    {
        (_, KtechMotorDriver driver) = await ResolveAsync(id);
        // 先读取当前 RAM 中的 Setting，再调用 PersistParameterToRomAsync 持久化
        KtechSetting current = await driver.ReadSettingAsync();
        await driver.PersistParameterToRomAsync(current);
    }

    /// <inheritdoc/>
    public async Task ResetSettingAsync(Guid id)
    {
        (_, KtechMotorDriver driver) = await ResolveAsync(id);
        await driver.ResetSettingAsync();
    }

    /// <inheritdoc/>
    public async Task WritePidRamAsync(Guid id, KtechWritePidRamInputDto input)
    {
        Check.NotNull(input, nameof(input));
        (_, KtechMotorDriver driver) = await ResolveAsync(id);

        // 9 字节入参：角度 Kp/Ki/Kd + 速度 Kp/Ki/Kd + 电流 Kp/Ki/Kd
        byte[] payload =
        [
            input.AngleKp,
            input.AngleKi,
            input.AngleKd,
            input.SpeedKp,
            input.SpeedKi,
            input.SpeedKd,
            input.CurrentKp,
            input.CurrentKi,
            input.CurrentKd,
        ];
        await driver.SendCommandAsync(
            KtechCommands.WritePidRam,
            payload,
            expectedResponsePayloadLength: 0,
            timeoutMs: 800
        );
    }

    // ====== Basic ======

    /// <inheritdoc/>
    public async Task MotorOnAsync(Guid id)
    {
        (_, KtechMotorDriver d) = await ResolveAsync(id);
        await d.MotorOnAsync();
    }

    /// <inheritdoc/>
    public async Task MotorOffAsync(Guid id)
    {
        (_, KtechMotorDriver d) = await ResolveAsync(id);
        await d.MotorOffAsync();
    }

    /// <inheritdoc/>
    public async Task MotorStopAsync(Guid id)
    {
        (_, KtechMotorDriver d) = await ResolveAsync(id);
        await d.MotorStopAsync();
    }

    /// <inheritdoc/>
    public async Task MotorRestoreAsync(Guid id)
    {
        (_, KtechMotorDriver d) = await ResolveAsync(id);
        await d.MotorRestoreAsync();
    }

    /// <inheritdoc/>
    public async Task BrakeAsync(Guid id, bool release)
    {
        (_, KtechMotorDriver d) = await ResolveAsync(id);
        await d.BrakeAsync(release);
    }

    /// <inheritdoc/>
    public async Task ClearLoopsAsync(Guid id)
    {
        (_, KtechMotorDriver d) = await ResolveAsync(id);
        await d.ClearMotorLoopsAsync();
    }

    /// <inheritdoc/>
    public async Task SetMotorZeroRamAsync(Guid id)
    {
        (_, KtechMotorDriver d) = await ResolveAsync(id);
        await d.SetMotorZeroRamAsync();
    }

    /// <inheritdoc/>
    public async Task ClearErrorAsync(Guid id)
    {
        (_, KtechMotorDriver d) = await ResolveAsync(id);
        _ = await d.ClearMotorErrorAsync();
    }

    // ====== Motion ======

    /// <inheritdoc/>
    public async Task<KtechMotionResponseDto> MotionAsync(Guid id, KtechMotionInputDto input)
    {
        Check.NotNull(input, nameof(input));
        (_, KtechMotorDriver d) = await ResolveAsync(id);

        KtechMotionResponse resp = input.Mode switch
        {
            KtechMotionMode.Open => await d.OpenControlAsync(input.Voltage),
            KtechMotionMode.TorqueOrPower => await d.TorqueOrPowerControlAsync(input.TorqueOrPower),
            KtechMotionMode.Speed => await d.SpeedControlAsync(
                (int)Math.Round(input.SpeedSignedDps * 100)
            ),
            KtechMotionMode.MultiAngle => await d.MultiAngleAsync(
                (long)Math.Round(input.AngleDeg * 100)
            ),
            KtechMotionMode.MultiAngleWithSpeed => await d.MultiAngleWithSpeedAsync(
                (long)Math.Round(input.AngleDeg * 100),
                (uint)Math.Round(input.SpeedDps * 100)
            ),
            KtechMotionMode.SingleAngle => await d.SingleAngleAsync(
                input.Direction,
                (uint)Math.Round(input.SingleAngleDeg * 100)
            ),
            KtechMotionMode.SingleAngleWithSpeed => await d.SingleAngleWithSpeedAsync(
                input.Direction,
                (uint)Math.Round(input.SingleAngleDeg * 100),
                (uint)Math.Round(input.SpeedDps * 100)
            ),
            KtechMotionMode.IncrementAngle => await d.IncrementAngleAsync(
                (int)Math.Round(input.AngleDeg * 100)
            ),
            KtechMotionMode.IncrementAngleWithSpeed => await d.IncrementAngleWithSpeedAsync(
                (int)Math.Round(input.AngleDeg * 100),
                (uint)Math.Round(input.SpeedDps * 100)
            ),
            _ => throw new UserFriendlyException($"不支持的运动模式：{input.Mode}"),
        };

        return new KtechMotionResponseDto
        {
            MotorTemperature = resp.MotorTemperature,
            TorqueOrPower = resp.TorqueOrPower,
            Speed = resp.Speed,
            EncoderValue = resp.EncoderValue,
        };
    }

    // ====== State 单次 ======

    /// <inheritdoc/>
    public async Task<KtechStateSnapshotDto> ReadStateOnceAsync(Guid id)
    {
        (MotorAxis axis, KtechMotorDriver d) = await ResolveAsync(id);
        KtechState1 s1 = await d.ReadState1Async();
        KtechState2 s2 = await d.ReadState2Async();
        long angle = await d.ReadMultiAngleAsync();

        return new KtechStateSnapshotDto
        {
            AxisId = id,
            SlaveId = axis.SlaveId,
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ElapsedMs = 0,
            IsSuccess = true,
            MotorTemperature = s1.MotorTemperature,
            BusVoltage = s1.BusVoltage,
            BusCurrent = s1.BusCurrent,
            ErrorFlags = MapErrorFlags(s1.ErrorFlags),
            TorqueOrPower = s2.TorqueOrPower,
            Speed = s2.Speed,
            EncoderValue = s2.EncoderValue,
            MultiTurnAngle = angle / 100.0,
        };
    }

    /// <inheritdoc/>
    public async Task<KtechState3Dto> ReadState3Async(Guid id)
    {
        (_, KtechMotorDriver d) = await ResolveAsync(id);
        KtechState3 s3 = await d.ReadState3Async();
        return new KtechState3Dto
        {
            MotorTemperature = s3.MotorTemperature,
            Ia = s3.Ia,
            Ib = s3.Ib,
            Ic = s3.Ic,
        };
    }

    /// <inheritdoc/>
    public async Task<long> ReadMultiAngleAsync(Guid id)
    {
        (_, KtechMotorDriver d) = await ResolveAsync(id);
        return await d.ReadMultiAngleAsync();
    }

    /// <inheritdoc/>
    public async Task<uint> ReadSingleAngleAsync(Guid id)
    {
        (_, KtechMotorDriver d) = await ResolveAsync(id);
        return await d.ReadSingleAngleAsync();
    }

    // ====== Firmware ======

    /// <inheritdoc/>
    public async Task UploadFirmwareAsync(Guid id, IRemoteStreamContent file)
    {
        Check.NotNull(file, nameof(file));
        (MotorAxis axis, KtechMotorDriver driver) = await ResolveAsync(id);

        // 读取整个固件文件到内存（一般 < 256KB）
        await using Stream stream = file.GetStream();
        using MemoryStream ms = new();
        await stream.CopyToAsync(ms);
        byte[] bytes = ms.ToArray();
        string fileName = file.FileName ?? "firmware.bin";

        _logger.LogInformation(
            "[KTECH-IAP] axis={Axis} slave={Slave} file={File} size={Size}B",
            axis.Name,
            axis.SlaveId,
            fileName,
            bytes.Length
        );

        // 1) 发起 IAP，设备进入 Bootloader
        await driver.BeginIapAsync();

        // 2) Ymodem 传输
        Progress<KtechUpgradeProgress> progress = new(p =>
        {
            _ = _notifier.NotifyUpgradeProgressAsync(MapToUpgradeProgressDto(id, p));
        });
        KtechYmodemUploader uploader = new(driver.Port, _logger);
        try
        {
            await uploader.UploadAsync(fileName, bytes, progress, default);
            await _notifier.NotifyUpgradeProgressAsync(
                new KtechUpgradeProgressDto
                {
                    AxisId = id,
                    Stage = KtechUpgradeStage.Completed,
                    BytesSent = bytes.Length,
                    TotalBytes = bytes.Length,
                    Percent = 100,
                    Message = "升级完成",
                }
            );
        }
        catch (KtechYmodemException ex)
        {
            await _notifier.NotifyUpgradeProgressAsync(
                new KtechUpgradeProgressDto
                {
                    AxisId = id,
                    Stage = KtechUpgradeStage.Failed,
                    BytesSent = 0,
                    TotalBytes = bytes.Length,
                    Percent = 0,
                    Message = ex.Message,
                    ErrorStep = ex.ErrorStep,
                }
            );
            throw new UserFriendlyException($"固件升级失败（步骤 {ex.ErrorStep}）：{ex.Message}");
        }
    }

    // ====== Sampling Control ======

    /// <inheritdoc/>
    public Task<bool> GetSamplingEnabledAsync(Guid id) =>
        Task.FromResult(_samplerStateStore.IsPollingEnabled(id));

    /// <inheritdoc/>
    public Task EnableSamplingAsync(Guid id)
    {
        _samplerStateStore.SetPollingEnabled(id, true);
        _logger.LogInformation("[KTECH] 轴 {AxisId} 实时采样已启用", id);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DisableSamplingAsync(Guid id)
    {
        _samplerStateStore.SetPollingEnabled(id, false);
        _logger.LogInformation("[KTECH] 轴 {AxisId} 实时采样已暂停", id);
        return Task.CompletedTask;
    }

    // ====== 私有辅助 ======

    private async Task<(MotorAxis Axis, KtechMotorDriver Driver)> ResolveAsync(Guid id)
    {
        MotorAxis axis = await _motorAxisRepository.GetAsync(id);
        if (axis.Brand != MotorBrand.KtechKtech)
        {
            throw new UserFriendlyException($"电机 [{axis.Name}] 不是 KTECH 品牌，操作台不可用");
        }

        KtechMotorDriver? driver = _motorControlService.GetKtechMotorDriver(axis.SlaveId);
        if (driver is null)
        {
            throw new UserFriendlyException(
                $"未找到从机地址 {axis.SlaveId} 对应的 KTECH 驱动，请确认串口已打开且已使能"
            );
        }
        return (axis, driver);
    }

    private static KtechCalibDto MapToCalibDto(IKtechCalib c)
    {
        KtechCalibDto dto = new()
        {
            DeviceTypeCode = (int)c.DeviceType,
            MotorPoles = c.MotorPoles,
            EncoderType = c.EncoderType,
            EncoderPos = c.EncoderPos,
            MotorPhaseSequence = c.MotorPhaseSequence,
            MotorEncoderAlignBias = c.MotorEncoderAlignBias,
            MotorEncoderAlignRatio = c.MotorEncoderAlignRatio,
            MotorEncoderAlignVoltage = c.MotorEncoderAlignVoltage / 100.0,
            MotorEncoderAlignFlag = c.MotorEncoderAlignFlag,
            EncoderOffset = c.EncoderOffset,
            EncoderOffsetFlag = c.EncoderOffsetFlag,
            SavedFlag = c.SavedFlag,
        };
        if (c is KtechCalibMg mg)
        {
            dto.Mg = new KtechCalibMgExtraDto
            {
                AlignValueList = mg.AlignValueList is { Length: 32 }
                    ? (ushort[])mg.AlignValueList.Clone()
                    : new ushort[32],
                ReductionRatio = mg.ReductionRatio,
                EncoderRelateValue = mg.EncoderRelateValue,
                EncoderRelateValueFlag = mg.EncoderRelateValueFlag,
                Encoder2Offset = mg.Encoder2Offset,
                Encoder2OffsetFlag = mg.Encoder2OffsetFlag,
            };
        }
        return dto;
    }

    /// <summary>
    /// 将 DTO 字段合并到现有的标定对象上（保留未在 DTO 中暴露的字段，如 SavedFlag、MG 内部数组）。
    /// </summary>
    private static IKtechCalib MergeCalibFromDto(IKtechCalib target, KtechCalibDto dto)
    {
        target.MotorPoles = dto.MotorPoles;
        target.EncoderType = dto.EncoderType;
        target.EncoderPos = dto.EncoderPos;
        target.MotorPhaseSequence = dto.MotorPhaseSequence;
        target.MotorEncoderAlignBias = dto.MotorEncoderAlignBias;
        target.MotorEncoderAlignRatio = dto.MotorEncoderAlignRatio;
        target.MotorEncoderAlignVoltage = (ushort)Math.Round(dto.MotorEncoderAlignVoltage * 100);
        target.MotorEncoderAlignFlag = dto.MotorEncoderAlignFlag;
        target.EncoderOffset = dto.EncoderOffset;
        target.EncoderOffsetFlag = dto.EncoderOffsetFlag;
        // SavedFlag 一般保留设备返回值；若 DTO 显式传入非 0，允许覆盖
        if (dto.SavedFlag != 0)
        {
            target.SavedFlag = dto.SavedFlag;
        }
        if (target is KtechCalibMg mg && dto.Mg is { } mgDto)
        {
            if (mgDto.AlignValueList is { Length: 32 })
            {
                mg.AlignValueList = (ushort[])mgDto.AlignValueList.Clone();
            }
            mg.ReductionRatio = mgDto.ReductionRatio;
            mg.EncoderRelateValue = mgDto.EncoderRelateValue;
            mg.EncoderRelateValueFlag = mgDto.EncoderRelateValueFlag;
            mg.Encoder2Offset = mgDto.Encoder2Offset;
            mg.Encoder2OffsetFlag = mgDto.Encoder2OffsetFlag;
        }
        return target;
    }

    private static KtechSettingDto MapToSettingDto(KtechSetting s) =>
        new()
        {
            DriverId = s.DriverId,
            BusType = s.BusType,
            Rs485BaudRate = s.Rs485BaudRate,
            CanBaudRate = s.CanBaudRate,
            BroadcastMode = s.BroadcastMode,
            SpinDirection = s.SpinDirection,
            ProtectMotorTempEnable = s.ProtectMotorTempEnable,
            ProtectDriverTempEnable = s.ProtectDriverTempEnable,
            ProtectUnderVoltageEnable = s.ProtectUnderVoltageEnable,
            ProtectOverVoltageEnable = s.ProtectOverVoltageEnable,
            ProtectOverCurrentEnable = s.ProtectOverCurrentEnable,
            ProtectShortCircuitEnable = s.ProtectShortCircuitEnable,
            ProtectStallEnable = s.ProtectStallEnable,
            ProtectLostInputEnable = s.ProtectLostInputEnable,
            ProtectMotorTemp = s.ProtectMotorTemp,
            ProtectDriverTemp = s.ProtectDriverTemp,
            ProtectUnderVoltage = s.ProtectUnderVoltage / 100.0,
            ProtectOverVoltage = s.ProtectOverVoltage / 100.0,
            ProtectOverCurrent = s.ProtectOverCurrent,
            ProtectOverCurrentTime = s.ProtectOverCurrentTime,
            ProtectStallTime = s.ProtectStallTime,
            ProtectLostInputTime = s.ProtectLostInputTime,
            BrakeResEnable = s.BrakeResEnable,
            BrakeResOnVoltage = s.BrakeResOnVoltage / 100.0,
            InputType = s.InputType,
            PwmInputControlMode = s.PwmInputControlMode,
            PwmInputMinValue = s.PwmInputMinValue,
            PwmInputMaxValue = s.PwmInputMaxValue,
            PwmInputCenterValue = s.PwmInputCenterValue,
            PwmInputDeadband = s.PwmInputDeadband,
            PwmToTorqueRatio = s.PwmToTorqueRatio,
            PwmToSpeedRatio = s.PwmToSpeedRatio,
            PwmToAngleRatio = s.PwmToAngleRatio,
            PulsesPerCircle = s.PulsesPerCircle,
            AnglePidKp = s.AnglePidKp,
            AnglePidKi = s.AnglePidKi,
            AnglePidKd = s.AnglePidKd,
            SpeedPidKp = s.SpeedPidKp,
            SpeedPidKi = s.SpeedPidKi,
            SpeedPidKd = s.SpeedPidKd,
            CurrentPidKp = s.CurrentPidKp,
            CurrentPidKi = s.CurrentPidKi,
            CurrentPidKd = s.CurrentPidKd,
            MaxTorque = s.MaxTorque,
            MaxSpeed = s.MaxSpeed / 100.0,
            MaxAngle = s.MaxAngle / 100.0,
            CurrentRamp = s.CurrentRamp,
            SpeedRamp = s.SpeedRamp,
            UniqueId = s.UniqueId,
            SavedFlag = s.SavedFlag,
        };

    private static void MergeSettingFromDto(KtechSetting target, KtechSettingDto dto)
    {
        target.DriverId = dto.DriverId;
        target.BusType = dto.BusType;
        target.Rs485BaudRate = dto.Rs485BaudRate;
        target.CanBaudRate = dto.CanBaudRate;
        target.BroadcastMode = dto.BroadcastMode;
        target.SpinDirection = dto.SpinDirection;
        target.ProtectMotorTempEnable = dto.ProtectMotorTempEnable;
        target.ProtectDriverTempEnable = dto.ProtectDriverTempEnable;
        target.ProtectUnderVoltageEnable = dto.ProtectUnderVoltageEnable;
        target.ProtectOverVoltageEnable = dto.ProtectOverVoltageEnable;
        target.ProtectOverCurrentEnable = dto.ProtectOverCurrentEnable;
        target.ProtectShortCircuitEnable = dto.ProtectShortCircuitEnable;
        target.ProtectStallEnable = dto.ProtectStallEnable;
        target.ProtectLostInputEnable = dto.ProtectLostInputEnable;
        target.ProtectMotorTemp = dto.ProtectMotorTemp;
        target.ProtectDriverTemp = dto.ProtectDriverTemp;
        target.ProtectUnderVoltage = (ushort)Math.Round(dto.ProtectUnderVoltage * 100);
        target.ProtectOverVoltage = (ushort)Math.Round(dto.ProtectOverVoltage * 100);
        target.ProtectOverCurrent = dto.ProtectOverCurrent;
        target.ProtectOverCurrentTime = dto.ProtectOverCurrentTime;
        target.ProtectStallTime = dto.ProtectStallTime;
        target.ProtectLostInputTime = dto.ProtectLostInputTime;
        target.BrakeResEnable = dto.BrakeResEnable;
        target.BrakeResOnVoltage = (ushort)Math.Round(dto.BrakeResOnVoltage * 100);
        target.InputType = dto.InputType;
        target.PwmInputControlMode = dto.PwmInputControlMode;
        target.PwmInputMinValue = dto.PwmInputMinValue;
        target.PwmInputMaxValue = dto.PwmInputMaxValue;
        target.PwmInputCenterValue = dto.PwmInputCenterValue;
        target.PwmInputDeadband = dto.PwmInputDeadband;
        target.PwmToTorqueRatio = dto.PwmToTorqueRatio;
        target.PwmToSpeedRatio = dto.PwmToSpeedRatio;
        target.PwmToAngleRatio = dto.PwmToAngleRatio;
        target.PulsesPerCircle = dto.PulsesPerCircle;
        target.AnglePidKp = dto.AnglePidKp;
        target.AnglePidKi = dto.AnglePidKi;
        target.AnglePidKd = dto.AnglePidKd;
        target.SpeedPidKp = dto.SpeedPidKp;
        target.SpeedPidKi = dto.SpeedPidKi;
        target.SpeedPidKd = dto.SpeedPidKd;
        target.CurrentPidKp = dto.CurrentPidKp;
        target.CurrentPidKi = dto.CurrentPidKi;
        target.CurrentPidKd = dto.CurrentPidKd;
        target.MaxTorque = dto.MaxTorque;
        target.MaxSpeed = (int)Math.Round(dto.MaxSpeed * 100);
        target.MaxAngle = (long)Math.Round(dto.MaxAngle * 100);
        target.CurrentRamp = dto.CurrentRamp;
        target.SpeedRamp = dto.SpeedRamp;
        // UniqueId / SavedFlag 一般由设备维护；DTO 若显式传入非 0 才覆盖，避免误清零
        if (dto.UniqueId != 0)
        {
            target.UniqueId = dto.UniqueId;
        }
        if (dto.SavedFlag != 0)
        {
            target.SavedFlag = dto.SavedFlag;
        }
    }

    /// <summary>错误位映射（公开供 Sampler 调用）。</summary>
    internal static KtechErrorFlagsDto MapErrorFlags(KtechErrorFlags flags) =>
        new()
        {
            UnderVoltage = flags.HasFlag(KtechErrorFlags.UnderVoltage),
            OverVoltage = flags.HasFlag(KtechErrorFlags.OverVoltage),
            DriverOverTemp = flags.HasFlag(KtechErrorFlags.DriverOverTemperature),
            MotorOverTemp = flags.HasFlag(KtechErrorFlags.MotorOverTemperature),
            OverCurrent = flags.HasFlag(KtechErrorFlags.OverCurrent),
            ShortCircuit = flags.HasFlag(KtechErrorFlags.ShortCircuit),
            Stall = flags.HasFlag(KtechErrorFlags.Stall),
            LostInput = flags.HasFlag(KtechErrorFlags.LostInput),
            Raw = (byte)flags,
        };

    private static KtechUpgradeProgressDto MapToUpgradeProgressDto(
        Guid id,
        KtechUpgradeProgress p
    ) =>
        new()
        {
            AxisId = id,
            Stage = MapStage(p.Stage),
            BytesSent = p.BytesSent,
            TotalBytes = p.TotalBytes,
            Percent = p.Percent,
            Message = p.Stage,
        };

    /// <summary>把上传器内部的字符串阶段映射为对前端友好的枚举。</summary>
    private static KtechUpgradeStage MapStage(string stage) =>
        stage switch
        {
            "WaitInitialC" => KtechUpgradeStage.WaitingHandshake,
            "HeaderSent" => KtechUpgradeStage.HeaderSent,
            "SendingData" => KtechUpgradeStage.Transferring,
            "Finishing" => KtechUpgradeStage.Finalizing,
            "Done" => KtechUpgradeStage.Completed,
            _ => KtechUpgradeStage.Started,
        };
}
