namespace AuroraStruct3D.Workflow.Dtos;

/// <summary>单个结果变量的摘要。</summary>
public class WorkflowVariableResultDto
{
    /// <summary>变量名。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>值类型令牌（string/int/long/double/bool/Mat/PointCloudData）。</summary>
    public string ValueType { get; set; } = string.Empty;

    /// <summary>标量值的文本表示（仅标量类型有值；Mat/点云为 null）。</summary>
    public string? ScalarValue { get; set; }

    /// <summary>若已写回 Redis 暂存，则为暂存 key（前端可经 variable-stage 接口取回）；否则 null。</summary>
    public string? StagedKey { get; set; }
}

/// <summary>工作流执行结果。</summary>
public class WorkflowRunResultDto
{
    /// <summary>工作流 ID。</summary>
    public Guid WorkflowId { get; set; }

    /// <summary>工作流名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>顶层变量数量。</summary>
    public int VariableCount { get; set; }

    /// <summary>执行耗时（毫秒）。</summary>
    public long DurationMs { get; set; }

    /// <summary>所有顶层变量的摘要。</summary>
    public List<WorkflowVariableResultDto> Variables { get; set; } = new();
}
