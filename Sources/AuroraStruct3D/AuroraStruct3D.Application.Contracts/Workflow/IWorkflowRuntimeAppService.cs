using AuroraStruct3D.Workflow.Dtos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AuroraStruct3D.Workflow;

/// <summary>
/// 工作流运行时执行应用服务。
/// 路由统一挂载在 <c>/api/app/workflow</c> 下，提供执行触发、调试步进、状态查询与调试停止能力。
/// </summary>
public interface IWorkflowRuntimeAppService : IApplicationService
{
    /// <summary>
    /// 查询项目工作流任务配置。
    /// </summary>
    /// <param name="projectId">项目 ID。</param>
    /// <returns>任务配置批量结果。</returns>
    Task<WorkflowProjectTaskBatchDto> GetProjectTasksAsync(Guid projectId);

    /// <summary>
    /// 更新项目工作流任务配置（整体覆盖）。
    /// </summary>
    /// <param name="input">任务配置（项目级触发 + 工作流行项）。</param>
    /// <returns>更新后的任务配置。</returns>
    Task<WorkflowProjectTaskBatchDto> UpdateProjectTasksAsync(WorkflowProjectTaskBatchDto input);
    Task<List<WorkflowProjectTaskRegistrationDto>> GetProjectTaskRegistrationsAsync(Guid? projectId = null);
    Task<WorkflowProjectTaskRegistrationDto> CreateProjectTaskAsync(CreateWorkflowProjectTaskInput input);
    Task<WorkflowProjectTaskRegistrationDto> UpdateProjectTaskAsync(Guid taskId, UpdateWorkflowProjectTaskInput input);
    Task<WorkflowProjectTaskRegistrationDto> SetProjectTaskEnabledAsync(
        Guid taskId, UpdateWorkflowProjectTaskEnabledInput input);

    /// <summary>
    /// 查询项目部署。
    /// </summary>
    /// <param name="projectId">项目 ID。</param>
    /// <returns>部署信息。</returns>
    Task<WorkflowProjectDeploymentDto?> GetProjectDeploymentsAsync(Guid projectId);
    Task<PagedResultDto<WorkflowProjectDeploymentDto>> GetProjectDeploymentHistoryAsync(
        Guid projectId, WorkflowProjectDeploymentHistoryInput input);
    Task<WorkflowProjectApplicationStatusDto> GetProjectApplicationStatusAsync(Guid projectId);
    Task<ApplyWorkflowProjectResultDto> ApplyProjectToDeviceAsync(
        Guid projectId, ApplyWorkflowProjectInput input);

    /// <summary>
    /// 根据当前配置发布项目部署快照（按内容幂等并支持原地更新）。
    /// </summary>
    /// <param name="projectId">项目 ID。</param>
    /// <returns>发布后的部署快照。</returns>
    Task<WorkflowProjectDeploymentDto> PublishProjectDeploymentAsync(Guid projectId);

    /// <summary>
    /// 激活项目部署快照。
    /// </summary>
    /// <param name="deploymentId">部署快照 ID。</param>
    /// <returns>激活后的部署快照。</returns>
    Task<WorkflowProjectDeploymentDto> ActivateProjectDeploymentAsync(Guid deploymentId);

    /// <summary>
    /// 重新激活项目部署快照。
    /// </summary>
    /// <param name="deploymentId">部署快照 ID。</param>
    /// <returns>重新激活后的部署快照。</returns>
    Task<WorkflowProjectDeploymentDto> ReactivateProjectDeploymentAsync(Guid deploymentId);

    /// <summary>
    /// 回滚到指定项目部署快照。
    /// </summary>
    /// <param name="deploymentId">目标部署快照 ID。</param>
    /// <returns>回滚后激活的部署快照。</returns>
    Task<WorkflowProjectDeploymentDto> RollbackProjectDeploymentAsync(Guid deploymentId);

    /// <summary>
    /// 删除项目部署快照。
    /// </summary>
    /// <param name="deploymentId">部署快照 ID。</param>
    Task DeleteProjectDeploymentAsync(Guid deploymentId);

    /// <summary>
    /// 创建并入队项目级工作流运行（1 运行 = 1 项目 = 多个工作流，来源于激活部署快照）。
    /// </summary>
    /// <param name="input">运行请求。</param>
    /// <returns>入队结果。</returns>
    Task<WorkflowProjectRunEnqueueResultDto> EnqueueProjectRunAsync(
        WorkflowProjectRunEnqueueInput input
    );
    Task<List<WorkflowPlcTriggerDto>> GetPlcTriggersAsync(Guid projectId);
    Task<WorkflowPlcTriggerDto> SavePlcTriggerAsync(Guid? id, SaveWorkflowPlcTriggerInput input);
    Task DeletePlcTriggerAsync(Guid id);
    Task<WorkflowPlcHandshakeConfigDto> GetPlcHandshakeAsync(Guid projectId);
    Task<WorkflowPlcHandshakeConfigDto> SavePlcHandshakeAsync(Guid projectId, SaveWorkflowPlcHandshakeConfigInput input);
    Task<WorkflowPlcHandshakeStatusDto> GetPlcHandshakeStatusAsync(Guid projectId);
    Task<WorkflowPlcHandshakeStatusDto> ResetPlcHandshakeAsync(Guid projectId);

    /// <summary>
    /// 查询项目运行列表。
    /// </summary>
    /// <param name="input">查询参数。</param>
    /// <returns>运行列表。</returns>
    Task<List<WorkflowProjectRunStatusDto>> GetProjectRunsAsync(WorkflowProjectRunListInput input);

    /// <summary>
    /// 查询单个项目运行状态。
    /// </summary>
    /// <param name="runId">运行 ID。</param>
    /// <returns>运行状态。</returns>
    Task<WorkflowProjectRunStatusDto> GetProjectRunStatusAsync(Guid runId);

    /// <summary>
    /// 请求取消项目运行。
    /// </summary>
    /// <param name="runId">运行 ID。</param>
    Task CancelProjectRunAsync(Guid runId);

    /// <summary>
    /// 修改项目运行。
    /// </summary>
    /// <param name="runId">运行 ID。</param>
    /// <param name="input">修改请求。</param>
    Task<WorkflowProjectRunStatusDto> UpdateProjectRunAsync(
        Guid runId,
        WorkflowProjectRunUpdateInput input
    );

    /// <summary>
    /// 删除项目运行。
    /// </summary>
    /// <param name="runId">运行 ID。</param>
    Task DeleteProjectRunAsync(Guid runId);

    /// <summary>
    /// 触发工作流执行。
    /// </summary>
    /// <param name="input">执行请求。</param>
    /// <returns>触发结果与当前状态快照。</returns>
    Task<WorkflowExecutionTriggerResultDto> ExecuteAsync(WorkflowExecutionTriggerInput input);

    Task<WorkflowExecutionTriggerResultDto> DebugSavedSourceAsync(
        Guid id,
        WorkflowExecutionTriggerInput input
    );

    Task<WorkflowExecutionTriggerResultDto> DebugSourceAsync(WorkflowSourceDebugInput input);
    Task<WorkflowDebugRunTriggerResultDto> DebugAndRunSourceAsync(WorkflowSourceDebugInput input);
    Task<WorkflowDebugRunTriggerResultDto> DebugAndRunSavedSourceAsync(
        Guid id,
        WorkflowDebugRunInput input
    );

    /// <summary>
    /// 查询已完成的调试运行正式输出。
    /// </summary>
    /// <param name="executionId">执行会话 ID。</param>
    /// <returns>结束节点选中的输出列表。</returns>
    Task<List<WorkflowExecutionOutputResultDto>> GetDebugResultAsync(Guid executionId);

    /// <summary>
    /// 生成 ROI 编辑底图（预运行到目标 ROI 节点的祖先子图，只执行其真正依赖的上游算子）。
    /// 用于替代上传阶段的 XY/XZ/YZ 底图：ROI 底图与实际裁剪的数据严格同源同尺寸。
    /// </summary>
    /// <param name="input">ROI 底图预运行请求。</param>
    /// <returns>底图 Blob、真实尺寸与投影映射。</returns>
    Task<RoiBaseImageResultDto> GenerateRoiBaseImageAsync(GenerateRoiBaseImageInput input);

    /// <summary>
    /// 对调试会话执行单步步进。
    /// </summary>
    /// <param name="executionId">执行会话 ID。</param>
    /// <param name="input">步进参数。</param>
    /// <returns>步进后的状态快照。</returns>
    Task<WorkflowExecutionStepResultDto> StepAsync(
        Guid executionId,
        WorkflowExecutionStepInput input
    );

    Task<WorkflowBreakpointListDto> SetBreakpointsAsync(
        Guid executionId,
        WorkflowBreakpointListDto input
    );
    Task<WorkflowBreakpointListDto> GetBreakpointsAsync(Guid executionId);
    Task<WorkflowExecutionStatusDto> ContinueAsync(Guid executionId);
    Task<WorkflowExecutionStatusDto> PauseAsync(Guid executionId);
    Task<WorkflowExecutionStatusDto> RunToAsync(Guid executionId, WorkflowRunToInput input);
    Task<List<WorkflowStackFrameDto>> GetStackAsync(Guid executionId);
    Task<List<WorkflowVariableResultDto>> GetVariablesAsync(Guid executionId);
    Task<List<WorkflowWatchResultDto>> WatchAsync(Guid executionId, WorkflowWatchInput input);
    Task<List<WorkflowTraceEventDto>> GetTraceAsync(Guid executionId);
    Task<WorkflowExecutionStatusDto> StepIntoAsync(Guid executionId);
    Task<WorkflowExecutionStatusDto> StepOverAsync(Guid executionId);
    Task<WorkflowExecutionStatusDto> StepOutAsync(Guid executionId);
    Task<WorkflowTracePageDto> GetTracePageAsync(Guid executionId, WorkflowTraceQueryDto input);
    Task<List<WorkflowNodePerformanceDto>> GetPerformanceAsync(Guid executionId);

    /// <summary>
    /// 查询调试会话列表。
    /// </summary>
    /// <param name="projectId">项目 ID（可选）。</param>
    /// <param name="runId">运行 ID（可选）。</param>
    /// <param name="includeVariables">是否返回变量快照。</param>
    /// <returns>调试会话状态列表。</returns>
    Task<List<WorkflowExecutionStatusDto>> GetDebugSessionsAsync(
        Guid projectId = default,
        Guid runId = default,
        bool includeVariables = false
    );

    /// <summary>
    /// 查询执行/调试会话状态。
    /// </summary>
    /// <param name="executionId">执行会话 ID。</param>
    /// <param name="includeVariables">是否返回变量快照。</param>
    /// <returns>状态信息。</returns>
    Task<WorkflowExecutionStatusDto> GetStatusAsync(Guid executionId, bool includeVariables = true);

    /// <summary>
    /// 停止调试会话并释放资源。
    /// </summary>
    /// <param name="executionId">执行会话 ID。</param>
    Task StopDebugAsync(Guid executionId);
}
