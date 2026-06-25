namespace AuroraStruct3D.VariableStage.Dtos;

/// <summary>
/// 暂存变量输入 DTO。
/// 用于将一个变量值（二进制）暂存到 Redis，后续根据变量名（key）读取。
/// </summary>
public class SetVariableInput
{
    /// <summary>变量名（key），区分大小写。</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>值类型，用于反序列化时还原正确的 CLR 类型，如 "Mat"、"PointCloudData"、"string"。</summary>
    public string ValueType { get; set; } = string.Empty;
}

/// <summary>
/// 暂存变量值 DTO。
/// </summary>
public class VariableValueDto
{
    /// <summary>变量名（key）。</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>值类型，如 "Mat"、"PointCloudData"、"string"。</summary>
    public string ValueType { get; set; } = string.Empty;

    /// <summary>值内容（Base64 编码的二进制数据）。</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>暂存时间（UTC）。</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>数据大小（字节）。</summary>
    public long SizeBytes { get; set; }
}
