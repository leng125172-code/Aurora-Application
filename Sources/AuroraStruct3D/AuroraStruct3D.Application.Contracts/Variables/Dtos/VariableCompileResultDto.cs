namespace AuroraStruct3D.Variables.Dtos;

/// <summary>
/// 变量离线编译输出。
/// </summary>
public class VariableCompileResultDto
{
    /// <summary>本次生成的快照版本号。</summary>
    public long SnapshotVersion { get; set; }

    /// <summary>是否可发布（无 Error 级诊断）。</summary>
    public bool CanPublish { get; set; }

    /// <summary>可见变量（用于前端变量选择器）。</summary>
    public List<VariableDefinitionDto> VisibleVariables { get; set; } = new();

    /// <summary>诊断集合。</summary>
    public List<VariableDiagnosticDto> Diagnostics { get; set; } = new();
}
