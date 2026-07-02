namespace AuroraStruct3D.Variables.Dtos;

/// <summary>
/// 读取运行时变量结果。
/// </summary>
public class ReadVariableResultDto
{
    /// <summary>变量状态。</summary>
    public VariableValueState State { get; set; }

    /// <summary>变量类型名。</summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>变量值 JSON（未初始化时为 null）。</summary>
    public string? ValueJson { get; set; }

    /// <summary>值版本号（用于乐观并发写）。</summary>
    public long ValueVersion { get; set; }
}
