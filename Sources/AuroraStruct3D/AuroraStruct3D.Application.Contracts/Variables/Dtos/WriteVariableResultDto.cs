namespace AuroraStruct3D.Variables.Dtos;

/// <summary>
/// 写入运行时变量结果。
/// </summary>
public class WriteVariableResultDto
{
    /// <summary>写入后状态。</summary>
    public VariableValueState State { get; set; }

    /// <summary>写入后版本号。</summary>
    public long ValueVersion { get; set; }
}
