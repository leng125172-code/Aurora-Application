using System.Buffers.Binary;
using System.Text;

namespace AuroraStruct3D.RS485.Ktech;

/// <summary>
/// 瓴控 KTECH 结构体二进制编解码（小端字节序）
/// </summary>
/// <remarks>
/// 跨架构安全：使用 <see cref="BinaryPrimitives"/> 显式按小端读写，
/// 避免在 linux-arm64 部署目标上出现 <c>Marshal.PtrToStructure</c> 的对齐/字节序坑。
/// </remarks>
public static class KtechStructCodec
{
    /// <summary>
    /// 解析产品信息（CMD 0x12 响应 58 字节）。
    /// 布局：[0..20) DriverName | [20..40) MotorName | [40..52) ChipId(12B) | [52..54) Hardware | [54..56) Motor | [56..58) Firmware
    /// </summary>
    public static KtechProductInfo DecodeProductInfo(ReadOnlySpan<byte> data)
    {
        if (data.Length < 58)
        {
            throw new InvalidDataException(
                $"KTECH 产品信息长度不足，期望 58 字节，实际 {data.Length} 字节"
            );
        }

        return new KtechProductInfo
        {
            DriverName = ReadFixedString(data.Slice(0, 20)),
            MotorName = ReadFixedString(data.Slice(20, 20)),
            ChipId = Convert.ToHexString(data.Slice(40, 12)),
            HardwareVersion = FormatVersion(
                BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(52, 2))
            ),
            MotorVersion = FormatVersion(
                BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(54, 2))
            ),
            FirmwareVersion = FormatVersion(
                BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(56, 2))
            ),
        };
    }

    /// <summary>
    /// 解析设备类型（CMD 0x1F 响应 2 字节小端）
    /// </summary>
    public static KtechDeviceType DecodeDeviceType(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
        {
            throw new InvalidDataException("KTECH 设备类型响应至少 2 字节");
        }
        ushort raw = BinaryPrimitives.ReadUInt16LittleEndian(data);
        return Enum.IsDefined(typeof(KtechDeviceType), raw)
            ? (KtechDeviceType)raw
            : KtechDeviceType.Unknown;
    }

    /// <summary>
    /// 解析 State1（CMD 0x9A 响应 7 字节）。
    /// 布局：[0] MotorTemp(s8) | [1..3] Voltage(s16) | [3..5] Current(s16) | [5] 保留 | [6] ErrorFlags
    /// </summary>
    public static KtechState1 DecodeState1(ReadOnlySpan<byte> data)
    {
        if (data.Length < 7)
        {
            throw new InvalidDataException(
                $"KTECH State1 响应长度不足，期望 7 字节，实际 {data.Length} 字节"
            );
        }
        short voltage = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(1, 2));
        short current = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(3, 2));
        return new KtechState1
        {
            MotorTemperature = (sbyte)data[0],
            RawVoltage = voltage,
            RawCurrent = current,
            BusVoltage = voltage / 100.0,
            BusCurrent = current / 100.0,
            ErrorFlags = (KtechErrorFlags)data[6],
        };
    }

    /// <summary>
    /// 解析 State2（CMD 0x9C 响应 7 字节）
    /// 布局：[0] MotorTemp(s8) | [1..3] TorqueOrPower(s16) | [3..5] Speed(s16) | [5..7] Encoder(u16)
    /// </summary>
    public static KtechState2 DecodeState2(ReadOnlySpan<byte> data)
    {
        if (data.Length < 7)
        {
            throw new InvalidDataException(
                $"KTECH State2 响应长度不足，期望 7 字节，实际 {data.Length} 字节"
            );
        }
        return new KtechState2
        {
            MotorTemperature = (sbyte)data[0],
            TorqueOrPower = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(1, 2)),
            Speed = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(3, 2)),
            EncoderValue = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2)),
        };
    }

    /// <summary>
    /// 解析 State3（CMD 0x9D 响应 7 字节）
    /// 布局：[0] MotorTemp(s8) | [1..3] Ia(s16) | [3..5] Ib(s16) | [5..7] Ic(s16)
    /// </summary>
    public static KtechState3 DecodeState3(ReadOnlySpan<byte> data)
    {
        if (data.Length < 7)
        {
            throw new InvalidDataException(
                $"KTECH State3 响应长度不足，期望 7 字节，实际 {data.Length} 字节"
            );
        }
        return new KtechState3
        {
            MotorTemperature = (sbyte)data[0],
            Ia = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(1, 2)),
            Ib = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(3, 2)),
            Ic = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(5, 2)),
        };
    }

    /// <summary>
    /// 解析运动响应（多数运动控制命令返回 7 字节，结构同 State2）
    /// </summary>
    public static KtechMotionResponse DecodeMotionResponse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 7)
        {
            throw new InvalidDataException(
                $"KTECH 运动响应长度不足，期望 7 字节，实际 {data.Length} 字节"
            );
        }
        return new KtechMotionResponse
        {
            MotorTemperature = (sbyte)data[0],
            TorqueOrPower = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(1, 2)),
            Speed = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(3, 2)),
            EncoderValue = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2)),
        };
    }

    /// <summary>
    /// 解析多圈角度（CMD 0x92 响应 8 字节 int64，单位 0.01°）
    /// </summary>
    public static long DecodeMultiAngle(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8)
        {
            throw new InvalidDataException(
                $"KTECH 多圈角度响应长度不足，期望 8 字节，实际 {data.Length} 字节"
            );
        }
        return BinaryPrimitives.ReadInt64LittleEndian(data.Slice(0, 8));
    }

    /// <summary>
    /// 解析单圈角度（CMD 0x94 响应 4 字节 uint32，单位 0.01°）
    /// </summary>
    public static uint DecodeSingleAngle(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
        {
            throw new InvalidDataException(
                $"KTECH 单圈角度响应长度不足，期望 4 字节，实际 {data.Length} 字节"
            );
        }
        return BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(0, 4));
    }

    /// <summary>
    /// MS/MF/MH 标定结构 wire size（含 Pack=8 对齐填充）
    /// </summary>
    public const int CalibMsMfMhSize = 28;

    /// <summary>
    /// MG/MG_E 标定结构 wire size（含 Pack=8 对齐填充）
    /// </summary>
    public const int CalibMgSize = 108;

    /// <summary>
    /// 设置结构 wire size（含 Pack=8 对齐填充）
    /// </summary>
    public const int SettingSize = 104;

    /// <summary>
    /// 根据设备类型自动派发标定解码（MS/MF/MH/MG/MG_E）
    /// </summary>
    public static IKtechCalib DecodeCalibAuto(KtechDeviceType deviceType, ReadOnlySpan<byte> data)
    {
        return deviceType switch
        {
            KtechDeviceType.Mg or KtechDeviceType.MgE => DecodeCalibMg(data, deviceType),
            KtechDeviceType.Ms or KtechDeviceType.Mf or KtechDeviceType.Mh => DecodeCalibMsMfMh(
                data,
                deviceType
            ),
            _ => DecodeCalibMsMfMh(data, deviceType),
        };
    }

    /// <summary>
    /// 根据标定对象具体类型自动派发编码
    /// </summary>
    public static byte[] EncodeCalibAuto(IKtechCalib calib)
    {
        return calib switch
        {
            KtechCalibMg mg => EncodeCalibMg(mg),
            KtechCalib c => EncodeCalibMsMfMh(c),
            _ => throw new NotSupportedException($"未知 KTECH 标定类型: {calib.GetType().Name}"),
        };
    }

    /// <summary>
    /// 解析 MS/MF/MH 标定结构（28 字节带对齐填充，对应 Demo saveCalibMsMfMh_struct）
    /// </summary>
    public static KtechCalib DecodeCalibMsMfMh(
        ReadOnlySpan<byte> data,
        KtechDeviceType deviceType = KtechDeviceType.Ms
    )
    {
        if (data.Length < CalibMsMfMhSize)
        {
            throw new InvalidDataException(
                $"KTECH 标定(MS/MF/MH)数据长度不足，期望 {CalibMsMfMhSize} 字节，实际 {data.Length} 字节"
            );
        }
        return new KtechCalib
        {
            DeviceType = deviceType,
            MotorPoles = data[0],
            EncoderType = data[1],
            EncoderPos = data[2],
            MotorPhaseSequence = data[3],
            MotorEncoderAlignBias = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4, 4)),
            MotorEncoderAlignRatio = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8, 2)),
            MotorEncoderAlignVoltage = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(10, 2)),
            MotorEncoderAlignFlag = data[12],
            // 13..15 PADDING
            EncoderOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(16, 4)),
            EncoderOffsetFlag = data[20],
            // 21..23 PADDING
            SavedFlag = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(24, 4)),
        };
    }

    /// <summary>
    /// 编码 MS/MF/MH 标定结构为 28 字节
    /// </summary>
    public static byte[] EncodeCalibMsMfMh(KtechCalib calib)
    {
        byte[] buf = new byte[CalibMsMfMhSize];
        buf[0] = calib.MotorPoles;
        buf[1] = calib.EncoderType;
        buf[2] = calib.EncoderPos;
        buf[3] = calib.MotorPhaseSequence;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4, 4), calib.MotorEncoderAlignBias);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(8, 2), calib.MotorEncoderAlignRatio);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(10, 2), calib.MotorEncoderAlignVoltage);
        buf[12] = calib.MotorEncoderAlignFlag;
        // 13..15 PADDING（保持 0）
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(16, 4), calib.EncoderOffset);
        buf[20] = calib.EncoderOffsetFlag;
        // 21..23 PADDING（保持 0）
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(24, 4), calib.SavedFlag);
        return buf;
    }

    /// <summary>
    /// 解析 MG/MG_E 标定结构（108 字节带对齐填充，对应 Demo saveCalibMg_struct）
    /// </summary>
    public static KtechCalibMg DecodeCalibMg(
        ReadOnlySpan<byte> data,
        KtechDeviceType deviceType = KtechDeviceType.Mg
    )
    {
        if (data.Length < CalibMgSize)
        {
            throw new InvalidDataException(
                $"KTECH 标定(MG)数据长度不足，期望 {CalibMgSize} 字节，实际 {data.Length} 字节"
            );
        }
        KtechCalibMg calib = new()
        {
            DeviceType = deviceType,
            MotorPoles = data[0],
            EncoderType = data[1],
            EncoderPos = data[2],
            MotorPhaseSequence = data[3],
            MotorEncoderAlignBias = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4, 4)),
            MotorEncoderAlignRatio = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(8, 2)),
            MotorEncoderAlignVoltage = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(10, 2)),
            MotorEncoderAlignFlag = data[12],
            // 13 PADDING
            // 14..77: alignValueList[32]，每个 u16
            EncoderOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(80, 4)),
            EncoderOffsetFlag = data[84],
            ReductionRatio = data[85],
            // 86..87 PADDING
            EncoderRelateValue = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(88, 4)),
            EncoderRelateValueFlag = data[92],
            // 93..95 PADDING
            Encoder2Offset = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(96, 4)),
            Encoder2OffsetFlag = data[100],
            // 101..103 PADDING
            SavedFlag = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(104, 4)),
        };
        ushort[] list = new ushort[32];
        for (int i = 0; i < 32; i++)
        {
            list[i] = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(14 + i * 2, 2));
        }
        calib.AlignValueList = list;
        return calib;
    }

    /// <summary>
    /// 编码 MG/MG_E 标定结构为 108 字节
    /// </summary>
    public static byte[] EncodeCalibMg(KtechCalibMg calib)
    {
        byte[] buf = new byte[CalibMgSize];
        buf[0] = calib.MotorPoles;
        buf[1] = calib.EncoderType;
        buf[2] = calib.EncoderPos;
        buf[3] = calib.MotorPhaseSequence;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(4, 4), calib.MotorEncoderAlignBias);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(8, 2), calib.MotorEncoderAlignRatio);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(10, 2), calib.MotorEncoderAlignVoltage);
        buf[12] = calib.MotorEncoderAlignFlag;
        // 13 PADDING（保持 0）
        ushort[] list = calib.AlignValueList ?? new ushort[32];
        int n = Math.Min(32, list.Length);
        for (int i = 0; i < n; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(14 + i * 2, 2), list[i]);
        }
        // 78..79 PADDING（保持 0）
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(80, 4), calib.EncoderOffset);
        buf[84] = calib.EncoderOffsetFlag;
        buf[85] = calib.ReductionRatio;
        // 86..87 PADDING
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(88, 4), calib.EncoderRelateValue);
        buf[92] = calib.EncoderRelateValueFlag;
        // 93..95 PADDING
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(96, 4), calib.Encoder2Offset);
        buf[100] = calib.Encoder2OffsetFlag;
        // 101..103 PADDING
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(104, 4), calib.SavedFlag);
        return buf;
    }

    /// <summary>
    /// 解析设置参数结构（104 字节带对齐填充，对应 Demo saveSetting_t）
    /// </summary>
    public static KtechSetting DecodeSetting(ReadOnlySpan<byte> data)
    {
        if (data.Length < SettingSize)
        {
            throw new InvalidDataException(
                $"KTECH 设置数据长度不足，期望 {SettingSize} 字节，实际 {data.Length} 字节"
            );
        }

        return new KtechSetting
        {
            DriverId = data[0],
            BusType = data[1],
            Rs485BaudRate = data[2],
            CanBaudRate = data[3],
            BroadcastMode = data[4],
            SpinDirection = data[5],
            ProtectMotorTempEnable = data[6],
            ProtectDriverTempEnable = data[7],
            ProtectUnderVoltageEnable = data[8],
            ProtectOverVoltageEnable = data[9],
            ProtectOverCurrentEnable = data[10],
            ProtectShortCircuitEnable = data[11],
            ProtectStallEnable = data[12],
            ProtectLostInputEnable = data[13],
            ProtectMotorTemp = data[14],
            ProtectDriverTemp = data[15],
            ProtectUnderVoltage = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(16, 2)),
            ProtectOverVoltage = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(18, 2)),
            ProtectOverCurrent = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(20, 2)),
            ProtectOverCurrentTime = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(22, 2)),
            ProtectStallTime = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(24, 2)),
            ProtectLostInputTime = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(26, 2)),
            BrakeResEnable = data[28],
            // 29 PADDING
            BrakeResOnVoltage = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(30, 2)),
            InputType = data[32],
            PwmInputControlMode = data[33],
            PwmInputMinValue = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(34, 2)),
            PwmInputMaxValue = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(36, 2)),
            PwmInputCenterValue = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(38, 2)),
            PwmInputDeadband = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(40, 2)),
            PwmToTorqueRatio = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(42, 2)),
            PwmToSpeedRatio = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(44, 2)),
            PwmToAngleRatio = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(46, 2)),
            PulsesPerCircle = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(48, 2)),
            AnglePidKp = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(50, 2)),
            AnglePidKi = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(52, 2)),
            AnglePidKd = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(54, 2)),
            SpeedPidKp = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(56, 2)),
            SpeedPidKi = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(58, 2)),
            SpeedPidKd = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(60, 2)),
            CurrentPidKp = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(62, 2)),
            CurrentPidKi = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(64, 2)),
            CurrentPidKd = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(66, 2)),
            MaxTorque = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(68, 2)),
            // 70..71 PADDING
            MaxSpeed = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(72, 4)),
            // 76..79 PADDING (align s64)
            MaxAngle = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(80, 8)),
            CurrentRamp = BinaryPrimitives.ReadInt16LittleEndian(data.Slice(88, 2)),
            // 90..91 PADDING
            SpeedRamp = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(92, 4)),
            UniqueId = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(96, 4)),
            SavedFlag = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(100, 4)),
        };
    }

    /// <summary>
    /// 编码设置结构为 104 字节
    /// </summary>
    public static byte[] EncodeSetting(KtechSetting s)
    {
        byte[] buf = new byte[SettingSize];
        buf[0] = s.DriverId;
        buf[1] = s.BusType;
        buf[2] = s.Rs485BaudRate;
        buf[3] = s.CanBaudRate;
        buf[4] = s.BroadcastMode;
        buf[5] = s.SpinDirection;
        buf[6] = s.ProtectMotorTempEnable;
        buf[7] = s.ProtectDriverTempEnable;
        buf[8] = s.ProtectUnderVoltageEnable;
        buf[9] = s.ProtectOverVoltageEnable;
        buf[10] = s.ProtectOverCurrentEnable;
        buf[11] = s.ProtectShortCircuitEnable;
        buf[12] = s.ProtectStallEnable;
        buf[13] = s.ProtectLostInputEnable;
        buf[14] = s.ProtectMotorTemp;
        buf[15] = s.ProtectDriverTemp;
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(16, 2), s.ProtectUnderVoltage);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(18, 2), s.ProtectOverVoltage);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(20, 2), s.ProtectOverCurrent);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(22, 2), s.ProtectOverCurrentTime);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(24, 2), s.ProtectStallTime);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(26, 2), s.ProtectLostInputTime);
        buf[28] = s.BrakeResEnable;
        // 29 PADDING
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(30, 2), s.BrakeResOnVoltage);
        buf[32] = s.InputType;
        buf[33] = s.PwmInputControlMode;
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(34, 2), s.PwmInputMinValue);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(36, 2), s.PwmInputMaxValue);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(38, 2), s.PwmInputCenterValue);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(40, 2), s.PwmInputDeadband);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(42, 2), s.PwmToTorqueRatio);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(44, 2), s.PwmToSpeedRatio);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(46, 2), s.PwmToAngleRatio);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(48, 2), s.PulsesPerCircle);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(50, 2), s.AnglePidKp);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(52, 2), s.AnglePidKi);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(54, 2), s.AnglePidKd);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(56, 2), s.SpeedPidKp);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(58, 2), s.SpeedPidKi);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(60, 2), s.SpeedPidKd);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(62, 2), s.CurrentPidKp);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(64, 2), s.CurrentPidKi);
        BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(66, 2), s.CurrentPidKd);
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(68, 2), s.MaxTorque);
        // 70..71 PADDING
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(72, 4), s.MaxSpeed);
        // 76..79 PADDING
        BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(80, 8), s.MaxAngle);
        BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(88, 2), s.CurrentRamp);
        // 90..91 PADDING
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(92, 4), s.SpeedRamp);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(96, 4), s.UniqueId);
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(100, 4), s.SavedFlag);
        return buf;
    }

    /// <summary>
    /// 读取固定长度字符串（去除尾部空字符与空白）
    /// </summary>
    private static string ReadFixedString(ReadOnlySpan<byte> data)
    {
        int len = data.IndexOf((byte)0);
        if (len < 0)
        {
            len = data.Length;
        }
        return Encoding.ASCII.GetString(data.Slice(0, len)).TrimEnd();
    }

    /// <summary>
    /// 将版本号格式化为字符串。版本号以 uint16 LE 编码，单位 0.01。
    /// 例：200 → V2.0，310 → V3.1，235 → V2.35
    /// </summary>
    private static string FormatVersion(ushort raw)
    {
        double version = raw / 100.0;
        return $"V{version:0.0#}";
    }
}
