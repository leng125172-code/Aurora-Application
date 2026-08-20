using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

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

    /// <summary>运行 ID（可选，传入后可按运行进度解析目标工作流）。</summary>
    public Guid? RunId { get; set; }

    /// <summary>工作流 ID。</summary>
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
    /// 在线变量池读取超时毫秒（保留兼容性，当前版本内部不使用）。
    /// </summary>
    public int VariableReadTimeoutMs { get; set; } = 5000;
}

/// <summary>直接调试未保存的受限 C# 工作流脚本。</summary>
public class WorkflowSourceDebugInput : WorkflowExecutionTriggerInput
{
    [Required]
    public string SourceCode { get; set; } = string.Empty;

    /// <summary>启动前写入的断点节点 ID。</summary>
    public List<string> BreakpointNodeIds { get; set; } = new();
}

public class WorkflowDebugRunInput : WorkflowExecutionTriggerInput
{
    public List<string> BreakpointNodeIds { get; set; } = new();
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

public class WorkflowBreakpointDto
{
    public string? StatementId { get; set; }
    public string? NodeId { get; set; }
    public int? Line { get; set; }
    public bool Enabled { get; set; } = true;
    public string? Condition { get; set; }
    public int? HitCount { get; set; }
    public string? LogMessage { get; set; }
}

public class WorkflowBreakpointListDto
{
    public List<WorkflowBreakpointDto> Breakpoints { get; set; } = [];
}

public class WorkflowRunToInput
{
    public string? StatementId { get; set; }
    public string? NodeId { get; set; }
}

public class WorkflowWatchInput
{
    public List<string> Expressions { get; set; } = [];
}

public class WorkflowWatchResultDto
{
    public string Expression { get; set; } = string.Empty;
    public bool Found { get; set; }
    public object? Value { get; set; }
    public string? RuntimeType { get; set; }
    public string? Error { get; set; }
    public bool IsSummary { get; set; }
    public string? Handle { get; set; }
}

public class WorkflowStackFrameDto
{
    public int Index { get; set; }
    public string? NodeId { get; set; }
    public string? StatementId { get; set; }
    public string? DisplayName { get; set; }
}

public class WorkflowTraceEventDto
{
    public int Step { get; set; }
    public string? NodeId { get; set; }
    public string? StatementId { get; set; }
    public DateTime StartedAt { get; set; }
    public long DurationMs { get; set; }
    public string Status { get; set; } = "completed";
    public string? Error { get; set; }
    public string EventType { get; set; } = "statement-completed";
    public long QueueDurationMs { get; set; }
    public string? OutputSummary { get; set; }
}

public class WorkflowTraceQueryDto
{
    public int SkipCount { get; set; }
    public int MaxResultCount { get; set; } = 200;
    public string? NodeId { get; set; }
    public string? EventType { get; set; }
}

public class WorkflowTracePageDto
{
    public int TotalCount { get; set; }
    public List<WorkflowTraceEventDto> Items { get; set; } = [];
}

public class WorkflowNodePerformanceDto
{
    public string? NodeId { get; set; }
    public int Count { get; set; }
    public long TotalDurationMs { get; set; }
    public long MaxDurationMs { get; set; }
    public double AverageDurationMs { get; set; }
    public long P95DurationMs { get; set; }
    public bool IsSlow { get; set; }
    public int FaultCount { get; set; }
}

/// <summary>
/// 项目级工作流运行入队请求。
/// </summary>
public class WorkflowProjectRunEnqueueInput
{
    /// <summary>运行名称。</summary>
    [Required]
    [StringLength(128)]
    public string Name { get; set; } = string.Empty;

    /// <summary>关联项目 ID。</summary>
    [Required]
    public Guid ProjectId { get; set; }

    /// <summary>目标任务配置；为空时仅允许项目存在唯一启用任务。</summary>
    public Guid? TaskConfigId { get; set; }

    /// <summary>触发启动类型。</summary>
    public WorkflowProjectRunStartType StartType { get; set; } =
        WorkflowProjectRunStartType.Immediate;

    /// <summary>周期间隔（秒）；仅 Cyclic 有效，必须大于 0。</summary>
    [Range(1, 86400)]
    public int? CycleIntervalSeconds { get; set; }

    /// <summary>运行出错后的处理策略。</summary>
    public WorkflowProjectRunOnErrorAction OnErrorAction { get; set; } =
        WorkflowProjectRunOnErrorAction.StopRun;
}

/// <summary>
/// 项目级工作流运行入队结果。
/// </summary>
public class WorkflowProjectRunEnqueueResultDto
{
    /// <summary>运行 ID。</summary>
    public Guid RunId { get; set; }

    /// <summary>项目 ID。</summary>
    public Guid ProjectId { get; set; }

    public Guid TaskConfigId { get; set; }

    /// <summary>运行内工作流数。</summary>
    public int WorkflowCount { get; set; }

    /// <summary>Hangfire JobId。</summary>
    public string HangfireJobId { get; set; } = string.Empty;

    /// <summary>本次运行来源的部署快照 ID。</summary>
    public Guid DeploymentId { get; set; }

    /// <summary>本次运行来源的部署版本号。</summary>
    public int DeploymentRevision { get; set; }
}

public class WorkflowPlcTriggerDto
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid PlcDeviceId { get; set; }
    public Guid PlcTagId { get; set; }
    public string ExpectedValueJson { get; set; } = string.Empty;
    public double Tolerance { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime? LastTriggeredAt { get; set; }
    public string? LastSkipReason { get; set; }
}

public class SaveWorkflowPlcTriggerInput
{
    public Guid ProjectId { get; set; }
    public Guid PlcDeviceId { get; set; }
    public Guid PlcTagId { get; set; }
    [Required] public string ExpectedValueJson { get; set; } = string.Empty;
    [Range(0, double.MaxValue)] public double Tolerance { get; set; }
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// 项目工作流任务配置行项（单个工作流的启用与执行顺序）。
/// </summary>
public class WorkflowProjectTaskDto
{
    /// <summary>任务配置 ID。</summary>
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
    [Range(0, 100000)]
    public int OrderNo { get; set; }
}

/// <summary>
/// 项目工作流任务配置（项目级触发配置 + 工作流行项）。
/// <para>查询与更新统一使用该对象；更新时以 <see cref="Items"/> 整体覆盖项目任务配置。</para>
/// </summary>
public class WorkflowProjectTaskBatchDto
{
    /// <summary>项目 ID。</summary>
    [Required]
    public Guid ProjectId { get; set; }

    /// <summary>项目级触发类型。</summary>
    public WorkflowProjectTaskType TaskType { get; set; } = WorkflowProjectTaskType.Immediate;

    /// <summary>周期间隔（秒）；仅 Cyclic 有效。</summary>
    [Range(1, 86400)]
    public int? CycleIntervalSeconds { get; set; }

    /// <summary>提供正式任务 OK/NG 判定的工作流。</summary>
    public Guid? ResultWorkflowId { get; set; }

    /// <summary>最终布尔判定变量名。</summary>
    [StringLength(128)]
    public string? ResultVariableName { get; set; }

    public WorkflowProjectRunOnErrorAction OnErrorAction { get; set; } =
        WorkflowProjectRunOnErrorAction.StopRun;

    /// <summary>任务配置行项（各工作流的启用与顺序）。</summary>
    public List<WorkflowProjectTaskDto> Items { get; set; } = new();
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

    public int SnapshotSchemaVersion { get; set; }
    public WorkflowProjectTaskType TaskType { get; set; }
    public int? CycleIntervalSeconds { get; set; }
    public Guid? ResultWorkflowId { get; set; }
    public string? ResultVariableName { get; set; }
    public WorkflowProjectRunOnErrorAction OnErrorAction { get; set; }
}

public class CreateWorkflowProjectTaskInput
{
    [Required]
    public Guid ProjectId { get; set; }
    [Required, StringLength(128)]
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public SaveWorkflowPlcHandshakeConfigInput? PlcHandshake { get; set; }
}

public class UpdateWorkflowProjectTaskInput
{
    [Required, StringLength(128)]
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public SaveWorkflowPlcHandshakeConfigInput? PlcHandshake { get; set; }
}

public class UpdateWorkflowProjectTaskEnabledInput
{
    public bool IsEnabled { get; set; }
}

public class WorkflowProjectTaskRegistrationDto
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public DateTime CreationTime { get; set; }
    public WorkflowPlcHandshakeConfigDto? PlcHandshake { get; set; }
}

public class WorkflowProjectDeploymentHistoryInput
{
    [Range(0, int.MaxValue)] public int SkipCount { get; set; }
    [Range(1, 100)] public int MaxResultCount { get; set; } = 20;
}

public class ApplyWorkflowProjectInput
{
    public Guid? ExpectedActiveDeploymentId { get; set; }
    [Required]
    public WorkflowProjectTaskBatchDto TaskConfig { get; set; } = new();
}

public class ApplyWorkflowProjectResultDto
{
    public Guid ProjectId { get; set; }
    public WorkflowProjectDeploymentDto Deployment { get; set; } = new();
    public bool CreatedNewRevision { get; set; }
    public bool ActivationChanged { get; set; }
    public bool Unchanged { get; set; }
    public DateTime AppliedAt { get; set; }
}

public class WorkflowProjectApplicationStatusDto
{
    public Guid ProjectId { get; set; }
    public WorkflowProjectDeploymentDto? ActiveDeployment { get; set; }
    public WorkflowProjectDeploymentDto? LatestDeployment { get; set; }
    public bool HasUnappliedChanges { get; set; }
    public bool CanApply { get; set; }
    public List<string> ValidationMessages { get; set; } = [];
}

/// <summary>
/// 项目运行列表查询请求。
/// </summary>
public class WorkflowProjectRunListInput
{
    /// <summary>运行名称（可选，按名称模糊匹配）。</summary>
    [StringLength(128)]
    public string? Name { get; set; }

    /// <summary>项目 ID（可选，不传则查询全部项目运行）。</summary>
    public Guid ProjectId { get; set; }

    /// <summary>跳过条数。</summary>
    [Range(0, int.MaxValue)]
    public int SkipCount { get; set; }

    /// <summary>最大返回条数。</summary>
    [Range(1, 200)]
    public int MaxResultCount { get; set; } = 20;
}

/// <summary>
/// 项目运行修改请求。
/// </summary>
public class WorkflowProjectRunUpdateInput
{
    /// <summary>运行名称。</summary>
    [Required]
    [StringLength(128)]
    public string Name { get; set; } = string.Empty;

    /// <summary>运行出错后的处理策略。</summary>
    public WorkflowProjectRunOnErrorAction OnErrorAction { get; set; } =
        WorkflowProjectRunOnErrorAction.StopRun;
}

/// <summary>
/// 运行内单工作流执行结果 DTO。
/// </summary>
public class WorkflowProjectRunItemDto
{
    /// <summary>工作流 ID。</summary>
    public Guid WorkflowId { get; set; }

    /// <summary>状态。</summary>
    public WorkflowProjectRunItemStatus Status { get; set; }

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
/// 项目运行状态 DTO。
/// </summary>
public class WorkflowProjectRunStatusDto
{
    /// <summary>运行 ID。</summary>
    public Guid RunId { get; set; }

    /// <summary>项目 ID。</summary>
    public Guid ProjectId { get; set; }

    /// <summary>运行名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Hangfire JobId。</summary>
    public string? HangfireJobId { get; set; }

    /// <summary>运行状态。</summary>
    public WorkflowProjectRunStatus Status { get; set; }

    /// <summary>触发启动类型。</summary>
    public WorkflowProjectRunStartType StartType { get; set; }

    /// <summary>周期间隔（秒）；仅 Cyclic 有效。</summary>
    public int? CycleIntervalSeconds { get; set; }

    /// <summary>执行出错后的动作选择。</summary>
    public WorkflowProjectRunOnErrorAction OnErrorAction { get; set; }

    /// <summary>本次运行来源的部署快照 ID。</summary>
    public Guid? DeploymentId { get; set; }

    /// <summary>本次运行来源的部署版本号。</summary>
    public int? DeploymentRevision { get; set; }

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

    public WorkflowInspectionDecision InspectionDecision { get; set; }
    public WorkflowPlcHandshakeErrorCode InspectionErrorCode { get; set; }
    public string? InspectionErrorMessage { get; set; }

    /// <summary>运行内各工作流结果。</summary>
    public List<WorkflowProjectRunItemDto> Items { get; set; } = new();
}

public class WorkflowPlcHandshakeConfigDto
{
    public Guid Id { get; set; }
    public Guid TaskConfigId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid PlcDeviceId { get; set; }
    public string CaptureRequestAddress { get; set; } = string.Empty;
    public string RequestIdAddress { get; set; } = string.Empty;
    public string ResultAckAddress { get; set; } = string.Empty;
    public string ResultAckIdAddress { get; set; } = string.Empty;
    public string HeartbeatAddress { get; set; } = string.Empty;
    public string DeviceStatusAddress { get; set; } = string.Empty;
    public string TaskStatusAddress { get; set; } = string.Empty;
    public string CanCaptureAddress { get; set; } = string.Empty;
    public string CaptureAckAddress { get; set; } = string.Empty;
    public string AckRequestIdAddress { get; set; } = string.Empty;
    public string ResultValidAddress { get; set; } = string.Empty;
    public string ResultRequestIdAddress { get; set; } = string.Empty;
    public string ResultCodeAddress { get; set; } = string.Empty;
    public string ErrorCodeAddress { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
}

public class SaveWorkflowPlcHandshakeConfigInput
{
    public Guid ProjectId { get; set; }
    public Guid PlcDeviceId { get; set; }
    public string CaptureRequestAddress { get; set; } = string.Empty;
    public string RequestIdAddress { get; set; } = string.Empty;
    public string ResultAckAddress { get; set; } = string.Empty;
    public string ResultAckIdAddress { get; set; } = string.Empty;
    public string HeartbeatAddress { get; set; } = string.Empty;
    public string DeviceStatusAddress { get; set; } = string.Empty;
    public string TaskStatusAddress { get; set; } = string.Empty;
    public string CanCaptureAddress { get; set; } = string.Empty;
    public string CaptureAckAddress { get; set; } = string.Empty;
    public string AckRequestIdAddress { get; set; } = string.Empty;
    public string ResultValidAddress { get; set; } = string.Empty;
    public string ResultRequestIdAddress { get; set; } = string.Empty;
    public string ResultCodeAddress { get; set; } = string.Empty;
    public string ErrorCodeAddress { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
}

public class WorkflowPlcHandshakeStatusDto
{
    public Guid ProjectId { get; set; }
    public bool IsConfigured { get; set; }
    public bool IsEnabled { get; set; }
    public WorkflowPlcHandshakePhase Phase { get; set; }
    public int CurrentRequestId { get; set; }
    public int LastCompletedRequestId { get; set; }
    public Guid? CurrentRunId { get; set; }
    public WorkflowInspectionDecision ResultCode { get; set; }
    public WorkflowPlcHandshakeErrorCode ErrorCode { get; set; }
    public DateTime? LastRequestAt { get; set; }
    public DateTime? LastResultAt { get; set; }
    public string? LastError { get; set; }
}

/// <summary>
/// 执行触发结果。
/// </summary>
public class WorkflowExecutionTriggerResultDto
{
    /// <summary>是否发生业务错误。</summary>
    public bool Error { get; set; }

    /// <summary>机器可识别的错误码；成功时为空。</summary>
    public string? ErrorCode { get; set; }

    /// <summary>结果图片 URL 列表（支持多张图，如俯视图、倾斜视图等）。</summary>
    public List<string> ResultImageUrls { get; set; } = new();

    /// <summary>结果消息；失败时为错误信息。</summary>
    public string? Message { get; set; }

    /// <summary>执行会话 ID。</summary>
    public Guid ExecutionId { get; set; }

    /// <summary>输出变量列表（由 configure_output 算子配置，与 status 同级）。</summary>
    public List<WorkflowVariableResultDto> Variables { get; set; } = new();

    /// <summary>当前状态。</summary>
    public WorkflowExecutionStatusDto Status { get; set; } = new();
}

/// <summary>
/// 调试运行启动结果。debug-run 仅负责创建并触发后台会话，
/// 结束节点正式输出通过 executions/{executionId}/result 单独查询。
/// </summary>
public class WorkflowDebugRunTriggerResultDto
{
    /// <summary>是否启动失败。</summary>
    public bool Error { get; set; }

    /// <summary>机器可识别的错误码；成功时为空。</summary>
    public string? ErrorCode { get; set; }

    /// <summary>启动结果消息；失败时为错误信息。</summary>
    public string? Message { get; set; }

    /// <summary>已创建的执行会话 ID。</summary>
    public Guid ExecutionId { get; set; }
}

/// <summary>
/// 执行完成后由结束节点声明的单个正式输出。
/// </summary>
public class WorkflowExecutionOutputResultDto
{
    private object? _value;

    /// <summary>运行时变量名称或变量路径。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>显示名称；结束节点未设置时等于变量名称。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 稳定值类型令牌（int/long/float/double/decimal/bool/string/blob/object/array/datetime/guid）。
    /// 原始 Mat/PointCloudData 仅用于提示调用方先存 Blob。
    /// </summary>
    public string ValueType { get; set; } = string.Empty;

    /// <summary>
    /// 原生 JSON 值。文件类输出的 valueType 为 blob，此处直接返回 Blob Key。
    /// 原始 Mat/PointCloudData 始终返回 null，避免序列化非托管指针及大型数据。
    /// </summary>
    public object? Value
    {
        get =>
            string.Equals(ValueType, "Mat", StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                ValueType,
                "OpenCvSharp.Mat",
                StringComparison.OrdinalIgnoreCase
            )
            || string.Equals(ValueType, "PointCloudData", StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                ValueType,
                "AuroraStruct3D.PointCloudData",
                StringComparison.OrdinalIgnoreCase
            )
                ? null
                : _value;
        set => _value = value;
    }
}

/// <summary>
/// 单步步进结果。
/// </summary>
public class WorkflowExecutionStepResultDto
{
    /// <summary>是否发生业务错误。</summary>
    public bool Error { get; set; }

    /// <summary>机器可识别的错误码；成功时为空。</summary>
    public string? ErrorCode { get; set; }

    /// <summary>结果消息；失败时为错误信息。</summary>
    public string? Message { get; set; }

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

    /// <summary>本状态快照的服务端生成时间，用于丢弃乱序状态。</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>会话内单调递增的状态快照版本。</summary>
    public long StateVersion { get; set; }

    public bool IsPaused { get; set; }

    /// <summary>
    /// 面向调试器 UI 的明确阶段：ready、running、paused、completed、faulted、stopped。
    /// </summary>
    public string DebugState { get; set; } = "ready";

    /// <summary>是否已经进入不可继续的终态。</summary>
    public bool IsTerminal { get; set; }

    /// <summary>当前是否允许继续或运行到节点。</summary>
    public bool CanContinue { get; set; }

    /// <summary>当前是否允许单步。</summary>
    public bool CanStep { get; set; }

    /// <summary>当前是否允许请求暂停。</summary>
    public bool CanPause { get; set; }

    /// <summary>当前是否允许停止会话。</summary>
    public bool CanStop { get; set; }

    public string? CurrentStatementId { get; set; }

    /// <summary>当前节点显示名称。</summary>
    public string? CurrentNodeName { get; set; }

    /// <summary>当前节点已经执行的毫秒数。</summary>
    public long CurrentNodeDurationMs { get; set; }

    /// <summary>运行 ID（运行调试场景下返回）。</summary>
    public Guid? RunId { get; set; }

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

    /// <summary>当前工作流在任务列表中的序号（从 1 开始；0 表示未知）。</summary>
    public int WorkflowOrderNo { get; set; }

    /// <summary>当前节点序号（从 1 开始；0 表示尚未进入）。</summary>
    public int CurrentNodeOrderNo { get; set; }

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

    /// <summary>结束节点声明的正式输出。</summary>
    public List<WorkflowConfiguredOutputDto> ConfiguredOutputs { get; set; } = new();

    /// <summary>当前可读取的正式输出；终态时为冻结快照。</summary>
    public List<WorkflowVariableResultDto> Outputs { get; set; } = new();

    /// <summary>变量快照。</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<WorkflowVariableResultDto>? Variables { get; set; }
}

/// <summary>工作流结束输出声明。</summary>
public class WorkflowConfiguredOutputDto
{
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? ValueType { get; set; }
    public bool HasValue { get; set; }
}
