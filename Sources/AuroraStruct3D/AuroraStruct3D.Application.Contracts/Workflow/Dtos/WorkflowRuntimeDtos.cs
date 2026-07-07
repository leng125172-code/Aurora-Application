using System.ComponentModel.DataAnnotations;

namespace AuroraStruct3D.Workflow.Dtos;

/// <summary>
/// 工作流执行模式。
/// </summary>
public enum WorkflowExecutionMode
{
    /// <summary>单次完整执行。</summary>
    RunOnce = 0,

    /// <summary>单步调试模式。</summary>
    DebugStep = 1,

    /// <summary>指定次数循环执行。</summary>
    Loop = 2,
}

/// <summary>
/// 工作流会话状态。
/// </summary>
public enum WorkflowExecutionStatus
{
    /// <summary>已创建，等待执行。</summary>
    Pending = 0,

    /// <summary>执行中。</summary>
    Running = 1,

    /// <summary>已完成。</summary>
    Completed = 2,

    /// <summary>已故障。</summary>
    Faulted = 3,

    /// <summary>已停止。</summary>
    Stopped = 4,
}

/// <summary>
/// 工作流执行触发请求。
/// </summary>
public class WorkflowExecutionTriggerInput
{
    /// <summary>项目 ID。</summary>
    [Required]
    public Guid ProjectId { get; set; }

    /// <summary>工作流 ID。</summary>
    [Required]
    public Guid WorkflowId { get; set; }

    /// <summary>执行模式。</summary>
    public WorkflowExecutionMode Mode { get; set; } = WorkflowExecutionMode.RunOnce;

    /// <summary>
    /// 循环模式的执行次数。
    /// </summary>
    [Range(1, 100000)]
    public int LoopCount { get; set; } = 1;

    /// <summary>
    /// 显式输入变量绑定键。
    /// </summary>
    public List<WorkflowVariableBindingKeyDto>? InputVariableBindings { get; set; }

    /// <summary>
    /// 显式输出变量绑定键。
    /// </summary>
    public List<WorkflowVariableBindingKeyDto>? OutputVariableBindings { get; set; }

    /// <summary>
    /// 输入变量键（回退默认推导）。
    /// </summary>
    public List<string>? InputVariableKeys { get; set; }

    /// <summary>
    /// 输出变量名（回退默认推导）。
    /// </summary>
    public List<string>? OutputVariableNames { get; set; }

    /// <summary>
    /// 在线变量池实例 ID。
    /// </summary>
    public Guid? RuntimeInstanceId { get; set; }

    /// <summary>
    /// 在线变量池读取超时毫秒。
    /// </summary>
    public int VariableReadTimeoutMs { get; set; } = 5000;
}

/// <summary>
/// 单步步进请求。
/// </summary>
public class WorkflowExecutionStepInput
{
    /// <summary>
    /// 本次步进的语句数。
    /// </summary>
    [Range(1, 1000)]
    public int Steps { get; set; } = 1;

    /// <summary>
    /// 是否返回变量快照。
    /// </summary>
    public bool IncludeVariables { get; set; } = true;
}

/// <summary>
/// 项目级工作流任务入队请求。
/// </summary>
public class WorkflowProjectTaskEnqueueInput
{
    /// <summary>任务名称。</summary>
    [Required]
    [StringLength(128)]
    public string Name { get; set; } = string.Empty;

    /// <summary>关联项目 ID。</summary>
    [Required]
    public Guid ProjectId { get; set; }

    /// <summary>触发启动类型。</summary>
    public WorkflowProjectTaskStartType StartType { get; set; } =
        WorkflowProjectTaskStartType.Immediate;

    /// <summary>运行出错后的处理策略。</summary>
    public WorkflowProjectTaskOnErrorAction OnErrorAction { get; set; } =
        WorkflowProjectTaskOnErrorAction.StopTask;
}

/// <summary>
/// 项目任务运行出错后的处理策略。
/// </summary>
public enum WorkflowProjectTaskOnErrorAction
{
    /// <summary>遇错即停止任务。</summary>
    StopTask = 0,

    /// <summary>跳过失败工作流并继续执行后续工作流。</summary>
    ContinueTask = 1,
}

/// <summary>
/// 项目级工作流任务入队结果。
/// </summary>
public class WorkflowProjectTaskEnqueueResultDto
{
    /// <summary>任务 ID。</summary>
    public Guid TaskId { get; set; }

    /// <summary>项目 ID。</summary>
    public Guid ProjectId { get; set; }

    /// <summary>任务内工作流数。</summary>
    public int WorkflowCount { get; set; }

    /// <summary>Hangfire JobId。</summary>
    public string HangfireJobId { get; set; } = string.Empty;

    /// <summary>本次任务绑定的部署快照 ID；legacy 回退模式下为 null。</summary>
    public Guid? DeploymentId { get; set; }

    /// <summary>本次任务绑定的部署版本号；legacy 回退模式下为 null。</summary>
    public int? DeploymentRevision { get; set; }
}

/// <summary>
/// 项目工作流绑定 DTO。
/// </summary>
public class WorkflowProjectBindingDto
{
    /// <summary>绑定 ID。</summary>
    public Guid Id { get; set; }

    /// <summary>项目 ID。</summary>
    public Guid ProjectId { get; set; }

    /// <summary>工作流 ID。</summary>
    public Guid WorkflowId { get; set; }

    /// <summary>工作流名称。</summary>
    public string WorkflowName { get; set; } = string.Empty;

    /// <summary>是否启用。</summary>
    public bool IsEnabled { get; set; }

    /// <summary>执行顺序。</summary>
    public int OrderNo { get; set; }
}

/// <summary>
/// 项目工作流绑定批量更新项。
/// </summary>
public class WorkflowProjectBindingUpdateItemInput
{
    /// <summary>工作流 ID。</summary>
    [Required]
    public Guid WorkflowId { get; set; }

    /// <summary>是否启用。</summary>
    public bool IsEnabled { get; set; }

    /// <summary>执行顺序。</summary>
    [Range(0, 100000)]
    public int OrderNo { get; set; }
}

/// <summary>
/// 项目工作流绑定批量更新请求。
/// </summary>
public class WorkflowProjectBindingBatchUpdateInput
{
    /// <summary>项目 ID。</summary>
    [Required]
    public Guid ProjectId { get; set; }

    /// <summary>绑定项。</summary>
    [Required]
    public List<WorkflowProjectBindingUpdateItemInput> Items { get; set; } = new();
}

/// <summary>
/// 项目部署快照 DTO。
/// </summary>
public class WorkflowProjectDeploymentDto
{
    /// <summary>部署 ID。</summary>
    public Guid Id { get; set; }

    /// <summary>项目 ID。</summary>
    public Guid ProjectId { get; set; }

    /// <summary>版本号。</summary>
    public int Revision { get; set; }

    /// <summary>部署状态。</summary>
    public WorkflowProjectDeploymentStatus Status { get; set; }

    /// <summary>是否为当前激活版本。</summary>
    public bool IsCurrentActive { get; set; }

    /// <summary>是否允许激活。</summary>
    public bool CanActivate { get; set; }

    /// <summary>是否允许重新激活。</summary>
    public bool CanReactivate { get; set; }

    /// <summary>是否允许回滚到该版本。</summary>
    public bool CanRollback { get; set; }

    /// <summary>快照哈希。</summary>
    public string SnapshotHash { get; set; } = string.Empty;

    /// <summary>快照项数量。</summary>
    public int ItemCount { get; set; }

    /// <summary>激活时间。</summary>
    public DateTime? ActivatedAt { get; set; }

    /// <summary>激活人。</summary>
    public Guid? ActivatedBy { get; set; }

    /// <summary>创建时间。</summary>
    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 项目任务列表查询请求。
/// </summary>
public class WorkflowProjectTaskListInput
{
    /// <summary>项目 ID（可选，不传则查询全部项目任务）。</summary>
    public Guid ProjectId { get; set; }

    /// <summary>跳过条数。</summary>
    [Range(0, int.MaxValue)]
    public int SkipCount { get; set; }

    /// <summary>最大返回条数。</summary>
    [Range(1, 200)]
    public int MaxResultCount { get; set; } = 20;
}

/// <summary>
/// 项目任务修改请求。
/// </summary>
public class WorkflowProjectTaskUpdateInput
{
    /// <summary>任务名称。</summary>
    [Required]
    [StringLength(128)]
    public string Name { get; set; } = string.Empty;

    /// <summary>运行出错后的处理策略。</summary>
    public WorkflowProjectTaskOnErrorAction OnErrorAction { get; set; } =
        WorkflowProjectTaskOnErrorAction.StopTask;
}

/// <summary>
/// 任务内单工作流执行结果 DTO。
/// </summary>
public class WorkflowProjectTaskItemDto
{
    /// <summary>工作流 ID。</summary>
    public Guid WorkflowId { get; set; }

    /// <summary>状态。</summary>
    public WorkflowProjectTaskItemStatus Status { get; set; }

    /// <summary>执行会话 ID。</summary>
    public Guid? ExecutionId { get; set; }

    /// <summary>错误信息。</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>开始时间。</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>结束时间。</summary>
    public DateTime? FinishedAt { get; set; }
}

/// <summary>
/// 项目任务状态 DTO。
/// </summary>
public class WorkflowProjectTaskStatusDto
{
    /// <summary>任务 ID。</summary>
    public Guid TaskId { get; set; }

    /// <summary>项目 ID。</summary>
    public Guid ProjectId { get; set; }

    /// <summary>任务名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Hangfire JobId。</summary>
    public string? HangfireJobId { get; set; }

    /// <summary>任务状态。</summary>
    public WorkflowProjectTaskStatus Status { get; set; }

    /// <summary>任务类型（触发启动类型）。</summary>
    public WorkflowProjectTaskStartType StartType { get; set; }

    /// <summary>执行出错后的动作选择。</summary>
    public WorkflowProjectTaskOnErrorAction OnErrorAction { get; set; }

    /// <summary>本次任务绑定的部署快照 ID；legacy 回退模式下为 null。</summary>
    public Guid? DeploymentId { get; set; }

    /// <summary>本次任务绑定的部署版本号；legacy 回退模式下为 null。</summary>
    public int? DeploymentRevision { get; set; }

    /// <summary>是否为 legacy 回退解析模式。</summary>
    public bool IsLegacyResolution { get; set; }

    /// <summary>在线变量池实例 ID。</summary>
    public Guid? RuntimeInstanceId { get; set; }

    /// <summary>工作流总数。</summary>
    public int WorkflowCount { get; set; }

    /// <summary>已执行数。</summary>
    public int ExecutedCount { get; set; }

    /// <summary>成功数。</summary>
    public int SuccessCount { get; set; }

    /// <summary>失败数。</summary>
    public int FailedCount { get; set; }

    /// <summary>是否请求取消。</summary>
    public bool IsCancelRequested { get; set; }

    /// <summary>创建时间。</summary>
    public DateTime CreationTime { get; set; }

    /// <summary>开始时间。</summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>结束时间。</summary>
    public DateTime? FinishedAt { get; set; }

    /// <summary>错误信息。</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>任务内各工作流结果。</summary>
    public List<WorkflowProjectTaskItemDto> Items { get; set; } = new();
}

/// <summary>
/// 执行触发结果。
/// </summary>
public class WorkflowExecutionTriggerResultDto
{
    /// <summary>执行会话 ID。</summary>
    public Guid ExecutionId { get; set; }

    /// <summary>当前状态。</summary>
    public WorkflowExecutionStatusDto Status { get; set; } = new();
}

/// <summary>
/// 单步步进结果。
/// </summary>
public class WorkflowExecutionStepResultDto
{
    /// <summary>执行会话 ID。</summary>
    public Guid ExecutionId { get; set; }

    /// <summary>当前状态。</summary>
    public WorkflowExecutionStatusDto Status { get; set; } = new();
}

/// <summary>
/// 执行状态 DTO。
/// </summary>
public class WorkflowExecutionStatusDto
{
    /// <summary>执行会话 ID。</summary>
    public Guid ExecutionId { get; set; }

    /// <summary>项目 ID。</summary>
    public Guid ProjectId { get; set; }

    /// <summary>工作流 ID。</summary>
    public Guid WorkflowId { get; set; }

    /// <summary>工作流名称。</summary>
    public string WorkflowName { get; set; } = string.Empty;

    /// <summary>执行模式。</summary>
    public WorkflowExecutionMode Mode { get; set; }

    /// <summary>执行状态。</summary>
    public WorkflowExecutionStatus Status { get; set; }

    /// <summary>已执行语句数。</summary>
    public int ExecutedSteps { get; set; }

    /// <summary>总语句数。</summary>
    public int TotalSteps { get; set; }

    /// <summary>当前节点 ID。</summary>
    public string? CurrentNodeId { get; set; }

    /// <summary>故障节点 ID。</summary>
    public string? FaultNodeId { get; set; }

    /// <summary>循环模式的总轮次。</summary>
    public int LoopCount { get; set; }

    /// <summary>已完成轮次。</summary>
    public int CompletedLoops { get; set; }

    /// <summary>错误信息。</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>耗时毫秒。</summary>
    public long DurationMs { get; set; }

    /// <summary>
    /// 在线变量池实例 ID（启用在线变量池时返回）。
    /// </summary>
    public Guid? RuntimeInstanceId { get; set; }

    /// <summary>变量快照。</summary>
    public List<WorkflowVariableResultDto> Variables { get; set; } = new();
}
