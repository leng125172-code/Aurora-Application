namespace AuroraStruct3D.OpenCV.Workflow.Compilation;

/// <summary>工作流静态校验结果汇总。</summary>
public sealed class WorkflowValidationResult
{
    /// <summary>全部诊断（错误 + 警告）。</summary>
    public IReadOnlyList<WorkflowDiagnostic> Diagnostics { get; }

    /// <param name="diagnostics">诊断集合。</param>
    public WorkflowValidationResult(IEnumerable<WorkflowDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        Diagnostics = diagnostics.ToList().AsReadOnly();
    }

    /// <summary>错误级诊断。</summary>
    public IEnumerable<WorkflowDiagnostic> Errors =>
        Diagnostics.Where(d => d.Severity == WorkflowDiagnosticSeverity.Error);

    /// <summary>警告级诊断。</summary>
    public IEnumerable<WorkflowDiagnostic> Warnings =>
        Diagnostics.Where(d => d.Severity == WorkflowDiagnosticSeverity.Warning);

    /// <summary>是否存在阻断编译的错误。</summary>
    public bool HasErrors => Diagnostics.Any(d => d.Severity == WorkflowDiagnosticSeverity.Error);
}
