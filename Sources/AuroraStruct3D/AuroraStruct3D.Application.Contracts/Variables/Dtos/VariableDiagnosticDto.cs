namespace AuroraStruct3D.Variables.Dtos;

/// <summary>
/// 离线校验诊断信息。
/// </summary>
public class VariableDiagnosticDto
{
    /// <summary>诊断级别。</summary>
    public VariableDiagnosticSeverity Severity { get; set; }

    /// <summary>错误码（例如 VAR1002）。</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>诊断消息。</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>相关变量所属工作流 ID。</summary>
    public Guid? OwnerWorkflowId { get; set; }

    /// <summary>相关变量名。</summary>
    public string? VariableName { get; set; }

    /// <summary>诊断位置（节点 ID / 字段路径）。</summary>
    public string? Location { get; set; }
}
