namespace AuroraStruct3D.Ktech.Dtos;

/// <summary>
/// 瓴控 KTECH 设备能力描述 DTO。
/// 后端按设备型号下发：哪些运动模式可用、哪些设置/标定字段只读、各字段量程上下限。
/// 前端据此禁用控件、过滤下拉项、限定输入范围，与 Demo（FormMainSetting.cs）UI 行为对齐。
/// </summary>
public class KtechCapabilitiesDto
{
    /// <summary>设备类型代码（如 MS=8209, MF=8225, MG=8241, MGE=8242, MH=8257）</summary>
    public int DeviceTypeCode { get; set; }

    /// <summary>设备型号名称（如 "MS"）</summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>该型号支持的运动控制模式列表（前端控制模式下拉据此过滤）</summary>
    public List<KtechMotionMode> SupportedControlModes { get; set; } = new();

    /// <summary>
    /// 该型号只读的设置参数字段名（KtechSettingDto 属性名，camelCase 形式）。
    /// 前端对应控件 :disabled="true"。
    /// </summary>
    public List<string> ReadOnlySettingFields { get; set; } = new();

    /// <summary>
    /// 该型号只读的标定参数字段名（KtechCalibDto 属性名，camelCase 形式）。
    /// </summary>
    public List<string> ReadOnlyCalibFields { get; set; } = new();

    /// <summary>
    /// 字段量程映射：key = 字段属性名（camelCase），value = 量程描述。
    /// 前端用于 input 的 min/max/step 与提交前校验。
    /// </summary>
    public Dictionary<string, KtechFieldRange> FieldRanges { get; set; } = new();

    /// <summary>
    /// 字段标签覆盖：key = 字段属性名（camelCase），value = 显示名称。
    /// 比如 MS 型 maxTorque 应显示为 "Max Power"。
    /// </summary>
    public Dictionary<string, string> FieldLabelOverrides { get; set; } = new();
}

/// <summary>
/// 字段量程描述。
/// </summary>
public class KtechFieldRange
{
    /// <summary>下限（含），null 表示无下限</summary>
    public double? Min { get; set; }

    /// <summary>上限（含），null 表示无上限</summary>
    public double? Max { get; set; }

    /// <summary>步进，null 表示默认（整数 1，浮点 0.01）</summary>
    public double? Step { get; set; }

    /// <summary>单位（如 "V"、"A"、"dps"、"0.01°"），仅展示用</summary>
    public string? Unit { get; set; }
}
