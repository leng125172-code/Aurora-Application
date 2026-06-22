namespace AuroraStruct3D.OpenCV.Workflow;

/// <summary>
/// 工作流执行时异常，由 <see cref="WorkflowExecutor"/> 在语句执行失败时抛出。
/// 包含失败的工作流名称、语句索引和语句描述，便于定位错误位置。
/// </summary>
public sealed class WorkflowExecutionException : Exception
{
    /// <summary>失败的工作流名称。</summary>
    public string WorkflowName { get; }

    /// <summary>失败语句的序号（从 1 开始）。</summary>
    public int StatementIndex { get; }

    /// <summary>失败语句的文字描述。</summary>
    public string StatementDescription { get; }

    /// <param name="workflowName">工作流名称。</param>
    /// <param name="statementIndex">失败语句序号（从 1 开始）。</param>
    /// <param name="statementDescription">失败语句描述。</param>
    /// <param name="innerException">原始异常。</param>
    public WorkflowExecutionException(
        string workflowName,
        int statementIndex,
        string statementDescription,
        Exception innerException
    )
        : base(
            $"工作流 '{workflowName}' 第 {statementIndex} 条语句执行失败 "
                + $"[{statementDescription}]：{innerException.Message}",
            innerException
        )
    {
        WorkflowName = workflowName;
        StatementIndex = statementIndex;
        StatementDescription = statementDescription;
    }
}
