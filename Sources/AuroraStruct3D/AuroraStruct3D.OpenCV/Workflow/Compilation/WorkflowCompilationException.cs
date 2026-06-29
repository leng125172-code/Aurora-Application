namespace AuroraStruct3D.OpenCV.Workflow.Compilation;

/// <summary>
/// 工作流编译失败异常：结构非法、算子未注册、静态校验未通过等。
/// 校验未通过时 <see cref="Validation"/> 携带完整诊断。
/// </summary>
public sealed class WorkflowCompilationException : Exception
{
    /// <summary>触发失败的校验结果（仅校验阶段失败时非 null）。</summary>
    public WorkflowValidationResult? Validation { get; }

    /// <param name="message">错误信息。</param>
    public WorkflowCompilationException(string message)
        : base(message) { }

    /// <param name="validation">未通过的校验结果。</param>
    public WorkflowCompilationException(WorkflowValidationResult validation)
        : base(BuildMessage(validation))
    {
        Validation = validation;
    }

    private static string BuildMessage(WorkflowValidationResult validation)
    {
        IEnumerable<string> lines = validation.Errors.Select(e => "  - " + e);
        return "工作流校验未通过：" + Environment.NewLine + string.Join(Environment.NewLine, lines);
    }
}
