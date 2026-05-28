using AuroraStruct3D.Ktech.Dtos;

namespace AuroraStruct3D.Ktech;

/// <summary>
/// 瓴控 KTECH 设备能力静态映射。
/// 按设备类型代码（MS=8209、MF=8225、MG=8241、MGE=8242、MH=8257）返回：
/// 支持的运动模式、只读字段、字段量程、字段标签覆盖。
/// 规则严格对照 Demo Documents/瓴控Demo/serovMotor/FormMainSetting.cs 的
/// formUi_UpdateFromDeviceType（L1200-1330）与控件初始化。
/// </summary>
/// <remarks>
/// 设计约定：
///   - 字段名使用 KtechSettingDto / KtechCalibDto 的属性名 camelCase 形式（前端 JSON 序列化默认）。
///   - 未列举的型号返回"全开放"能力，保证向后兼容未知设备。
///   - 8 种运动模式枚举值见 KtechMotionMode。
/// </remarks>
public static class KtechCapabilityProvider
{
    private const int DeviceMs = 8209;
    private const int DeviceMf = 8225;
    private const int DeviceMg = 8241;
    private const int DeviceMgE = 8242;
    private const int DeviceMh = 8257;

    /// <summary>
    /// 根据设备类型代码获取能力描述。
    /// </summary>
    /// <param name="deviceTypeCode">设备类型代码</param>
    public static KtechCapabilitiesDto Get(int deviceTypeCode)
    {
        return deviceTypeCode switch
        {
            DeviceMs => BuildMs(),
            DeviceMf => BuildMf(),
            DeviceMg => BuildMg(deviceTypeCode, "MG", includeReducer: false),
            DeviceMgE => BuildMg(deviceTypeCode, "MGE", includeReducer: true),
            DeviceMh => BuildMh(),
            _ => BuildDefault(deviceTypeCode),
        };
    }

    // ─────────────────────────── MS（功率控制，无电流环 PID）───────────────────────────
    private static KtechCapabilitiesDto BuildMs()
    {
        KtechCapabilitiesDto cap = BuildCommonOpen(DeviceMs, "MS");

        // MS 型电流环 PID 不可用（Demo 中 numericUpDownCurrentKp.Enabled=false）
        cap.ReadOnlySettingFields.AddRange(
            new[]
            {
                nameof(KtechSettingDto.CurrentPidKp).ToCamel(),
                nameof(KtechSettingDto.CurrentPidKi).ToCamel(),
                nameof(KtechSettingDto.CurrentPidKd).ToCamel(),
            }
        );

        // 校准字段统一只读基线（对齐 Demo textBoxXxx.ReadOnly=true 常驻状态）
        cap.ReadOnlyCalibFields.AddRange(CommonReadOnlyCalibFields());
        cap.ReadOnlySettingFields.AddRange(CommonReadOnlySettingFields());

        // MaxTorque 在 MS 型表示"最大功率（W）"，上限 850
        cap.FieldRanges[nameof(KtechSettingDto.MaxTorque).ToCamel()] = new KtechFieldRange
        {
            Min = -850,
            Max = 850,
            Step = 1,
            Unit = "W",
        };
        cap.FieldLabelOverrides[nameof(KtechSettingDto.MaxTorque).ToCamel()] = "最大功率";

        // 运动入参 TorqueOrPower 同样按 ±850 限制
        cap.FieldRanges[nameof(KtechMotionInputDto.TorqueOrPower).ToCamel()] = new KtechFieldRange
        {
            Min = -850,
            Max = 850,
            Step = 1,
            Unit = "W",
        };

        return cap;
    }

    // ─────────────────────────── MF ───────────────────────────
    private static KtechCapabilitiesDto BuildMf()
    {
        KtechCapabilitiesDto cap = BuildCommonOpen(DeviceMf, "MF");

        // 校准字段统一只读基线
        cap.ReadOnlyCalibFields.AddRange(CommonReadOnlyCalibFields());
        cap.ReadOnlySettingFields.AddRange(CommonReadOnlySettingFields());

        // MF/MG 系列 MaxTorque 上限 2000（mA）
        cap.FieldRanges[nameof(KtechSettingDto.MaxTorque).ToCamel()] = new KtechFieldRange
        {
            Min = -2000,
            Max = 2000,
            Step = 1,
            Unit = "mA",
        };
        cap.FieldRanges[nameof(KtechMotionInputDto.TorqueOrPower).ToCamel()] = new KtechFieldRange
        {
            Min = -2000,
            Max = 2000,
            Step = 1,
            Unit = "mA",
        };
        return cap;
    }

    // ─────────────────────────── MG / MGE ───────────────────────────
    private static KtechCapabilitiesDto BuildMg(
        int deviceTypeCode,
        string modelName,
        bool includeReducer
    )
    {
        KtechCapabilitiesDto cap = BuildCommonOpen(deviceTypeCode, modelName);

        // 校准字段统一只读基线
        cap.ReadOnlyCalibFields.AddRange(CommonReadOnlyCalibFields());
        cap.ReadOnlySettingFields.AddRange(CommonReadOnlySettingFields());

        // MG_E 型号电机零位由减速机零位接管（Demo 中 textBoxMotorZeroPosition.Enabled=false）
        if (includeReducer)
        {
            cap.ReadOnlyCalibFields.Add(nameof(KtechCalibDto.EncoderOffset).ToCamel());
        }

        cap.FieldRanges[nameof(KtechSettingDto.MaxTorque).ToCamel()] = new KtechFieldRange
        {
            Min = -2000,
            Max = 2000,
            Step = 1,
            Unit = "mA",
        };
        cap.FieldRanges[nameof(KtechMotionInputDto.TorqueOrPower).ToCamel()] = new KtechFieldRange
        {
            Min = -2000,
            Max = 2000,
            Step = 1,
            Unit = "mA",
        };

        // 非 MGE 型号没有减速比/第二编码器（Demo 中相关控件 Enabled=false）
        // 当前 KtechCalibDto 暂未对应字段，保留扩展位；前端按 ReadOnlyCalibFields 处理
        if (!includeReducer)
        {
            // 占位字段名（如后续 KtechCalibDto 新增减速比字段，请同步加到这里）
            // cap.ReadOnlyCalibFields.Add("reducerRatio");
            // cap.ReadOnlyCalibFields.Add("secondEncoderOffset");
        }
        return cap;
    }

    // ─────────────────────────── MH ───────────────────────────
    private static KtechCapabilitiesDto BuildMh()
    {
        KtechCapabilitiesDto cap = BuildCommonOpen(DeviceMh, "MH");

        // 校准字段统一只读基线
        cap.ReadOnlyCalibFields.AddRange(CommonReadOnlyCalibFields());
        cap.ReadOnlySettingFields.AddRange(CommonReadOnlySettingFields());

        cap.FieldRanges[nameof(KtechSettingDto.MaxTorque).ToCamel()] = new KtechFieldRange
        {
            Min = -2000,
            Max = 2000,
            Step = 1,
            Unit = "mA",
        };
        cap.FieldRanges[nameof(KtechMotionInputDto.TorqueOrPower).ToCamel()] = new KtechFieldRange
        {
            Min = -2000,
            Max = 2000,
            Step = 1,
            Unit = "mA",
        };
        return cap;
    }

    // ─────────────────────────── 未知型号兜底 ───────────────────────────
    private static KtechCapabilitiesDto BuildDefault(int deviceTypeCode)
    {
        return new KtechCapabilitiesDto
        {
            DeviceTypeCode = deviceTypeCode,
            ModelName = deviceTypeCode == 0 ? string.Empty : $"Unknown({deviceTypeCode})",
            SupportedControlModes = AllControlModes(),
            FieldRanges = CommonRanges(),
        };
    }

    // ─────────────────────────── 通用基线 ───────────────────────────
    private static KtechCapabilitiesDto BuildCommonOpen(int deviceTypeCode, string modelName)
    {
        return new KtechCapabilitiesDto
        {
            DeviceTypeCode = deviceTypeCode,
            ModelName = modelName,
            SupportedControlModes = AllControlModes(),
            FieldRanges = CommonRanges(),
        };
    }

    /// <summary>全部 9 种运动模式（含 Open）。具体型号按需在 Build 方法里裁剪。</summary>
    private static List<KtechMotionMode> AllControlModes() =>
        new()
        {
            KtechMotionMode.Open,
            KtechMotionMode.TorqueOrPower,
            KtechMotionMode.Speed,
            KtechMotionMode.MultiAngle,
            KtechMotionMode.MultiAngleWithSpeed,
            KtechMotionMode.SingleAngle,
            KtechMotionMode.SingleAngleWithSpeed,
            KtechMotionMode.IncrementAngle,
            KtechMotionMode.IncrementAngleWithSpeed,
        };

    /// <summary>
    /// 各型号共享的"校准字段只读基线"。
    /// 这些字段在 Demo（FormMainSetting.cs）中均为只读 TextBox 常驻显示，
    /// 实际修改通过专用命令（编码器对齐/置零等）触发，不通过手动输入。
    /// </summary>
    private static string[] CommonReadOnlyCalibFields() =>
        new[]
        {
            nameof(KtechCalibDto.EncoderType).ToCamel(),
            nameof(KtechCalibDto.EncoderPos).ToCamel(),
            nameof(KtechCalibDto.MotorPhaseSequence).ToCamel(),
            nameof(KtechCalibDto.MotorPoles).ToCamel(),
            nameof(KtechCalibDto.MotorEncoderAlignBias).ToCamel(),
            nameof(KtechCalibDto.MotorEncoderAlignRatio).ToCamel(),
            nameof(KtechCalibDto.MotorEncoderAlignFlag).ToCamel(),
            nameof(KtechCalibDto.EncoderOffsetFlag).ToCamel(),
        };

    /// <summary>
    /// 各型号共享的"设置字段只读基线"。
    /// Demo 中以下控件 ComboBox.Enabled=false 长期只读（连接后不可改）：
    /// 总线类型 / RS485 波特率 / CAN 波特率 / 唯一 ID / 保存标志。
    /// 修改这些字段须通过专用厂家工具或物理拨码，不允许 UI 修改。
    /// </summary>
    private static string[] CommonReadOnlySettingFields() =>
        new[]
        {
            nameof(KtechSettingDto.BusType).ToCamel(),
            nameof(KtechSettingDto.Rs485BaudRate).ToCamel(),
            nameof(KtechSettingDto.CanBaudRate).ToCamel(),
            nameof(KtechSettingDto.UniqueId).ToCamel(),
            nameof(KtechSettingDto.SavedFlag).ToCamel(),
        };

    /// <summary>各型号共享的字段量程基线（运动入参 + 部分设置项）。</summary>
    private static Dictionary<string, KtechFieldRange> CommonRanges()
    {
        return new Dictionary<string, KtechFieldRange>
        {
            // 运动入参
            [nameof(KtechMotionInputDto.Voltage).ToCamel()] = new()
            {
                Min = short.MinValue,
                Max = short.MaxValue,
                Step = 1,
                Unit = "raw",
            },
            [nameof(KtechMotionInputDto.SpeedDps).ToCamel()] = new()
            {
                Min = 0,
                Max = 10_000, // 最大 10000 dps
                Step = 0.01,
                Unit = "dps",
            },
            [nameof(KtechMotionInputDto.SpeedSignedDps).ToCamel()] = new()
            {
                Min = -10_000,
                Max = 10_000,
                Step = 0.01,
                Unit = "dps",
            },
            [nameof(KtechMotionInputDto.AngleDeg).ToCamel()] = new()
            {
                Min = -36_000_000, // ±10000 圈
                Max = 36_000_000,
                Step = 0.01,
                Unit = "°",
            },
            [nameof(KtechMotionInputDto.SingleAngleDeg).ToCamel()] = new()
            {
                Min = 0,
                Max = 359.99, // 单圈 0~359.99°
                Step = 0.01,
                Unit = "°",
            },
            [nameof(KtechMotionInputDto.Direction).ToCamel()] = new()
            {
                Min = 0,
                Max = 1,
                Step = 1,
            },
            // 设置项基线
            [nameof(KtechSettingDto.MaxSpeed).ToCamel()] = new()
            {
                Min = 0,
                Max = 10_000,
                Step = 0.01,
                Unit = "dps",
            },
            [nameof(KtechSettingDto.MaxAngle).ToCamel()] = new()
            {
                Min = 0,
                Max = 36_000,
                Step = 0.01,
                Unit = "°",
            },
            // 电压类字段（已换算为 V）
            [nameof(KtechSettingDto.ProtectUnderVoltage).ToCamel()] = new()
            {
                Min = 0,
                Max = 60,
                Step = 0.01,
                Unit = "V",
            },
            [nameof(KtechSettingDto.ProtectOverVoltage).ToCamel()] = new()
            {
                Min = 0,
                Max = 60,
                Step = 0.01,
                Unit = "V",
            },
            [nameof(KtechSettingDto.BrakeResOnVoltage).ToCamel()] = new()
            {
                Min = 0,
                Max = 60,
                Step = 0.01,
                Unit = "V",
            },
            [nameof(KtechCalibDto.MotorEncoderAlignVoltage).ToCamel()] = new()
            {
                Min = 0,
                Max = 60,
                Step = 0.01,
                Unit = "V",
            },
        };
    }
}

/// <summary>
/// 字符串首字母转 camelCase 的小工具，便于 nameof + 序列化对齐。
/// </summary>
internal static class CamelCaseExtensions
{
    public static string ToCamel(this string s)
    {
        if (string.IsNullOrEmpty(s) || char.IsLower(s[0]))
        {
            return s;
        }
        return char.ToLowerInvariant(s[0]) + s.Substring(1);
    }
}
