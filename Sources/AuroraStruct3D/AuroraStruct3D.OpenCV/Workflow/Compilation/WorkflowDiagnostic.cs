namespace AuroraStruct3D.OpenCV.Workflow.Compilation;

/// <summary>校验诊断严重级别。</summary>
public enum WorkflowDiagnosticSeverity
{
    /// <summary>错误：阻断编译。</summary>
    Error,

    /// <summary>警告：不阻断，提示潜在问题（如类型可能不匹配）。</summary>
    Warning,
}

/// <summary>单条工作流静态校验诊断。</summary>
public sealed class WorkflowDiagnostic
{
    /// <summary>严重级别。</summary>
    public required WorkflowDiagnosticSeverity Severity { get; init; }

    /// <summary>相关节点 ID（图级问题为 <c>(graph)</c>）。</summary>
    public required string NodeId { get; init; }

    /// <summary>相关端口 / 参数 / 变量名（可选）。</summary>
    public string? Target { get; init; }

    /// <summary>诊断信息。</summary>
    public required string Message { get; init; }

    /// <inheritdoc/>
    public override string ToString()
    {
        string target = Target is null ? string.Empty : $" [{Target}]";
        return $"{Severity} @ {NodeId}{target}: {Message}";
    }
}
