using AuroraStruct3D.Workflow.Dtos;
using Volo.Abp.Application.Services;

namespace AuroraStruct3D.Workflow;

/// <summary>
/// 工作流运行时执行应用服务。
/// 路由统一挂载在 <c>/api/app/workflow</c> 下，提供执行触发、调试步进、状态查询与调试停止能力。
/// </summary>
public interface IWorkflowRuntimeAppService : IApplicationService
{
    /// <summary>
    /// 查询项目工作流绑定配置。
    /// </summary>
    /// <param name="projectId">项目 ID。</param>
    /// <returns>绑定列表。</returns>
    Task<List<WorkflowProjectBindingDto>> GetProjectBindingsAsync(Guid projectId);

    /// <summary>
    /// 批量更新项目工作流绑定配置。
    /// </summary>
    /// <param name="input">更新请求。</param>
    /// <returns>更新后的绑定列表。</returns>
    Task<List<WorkflowProjectBindingDto>> UpdateProjectBindingsAsync(
        WorkflowProjectBindingBatchUpdateInput input
    );

    /// <summary>
    /// 查询项目部署历史。
    /// </summary>
    /// <param name="projectId">项目 ID。</param>
    /// <returns>部署列表。</returns>
    Task<List<WorkflowProjectDeploymentDto>> GetProjectDeploymentsAsync(Guid projectId);

    /// <summary>
    /// 根据当前绑定发布新的项目部署快照。
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
    /// 创建并入队项目级工作流任务（1 任务 = 1 项目 = 多个工作流）。
    /// </summary>
    /// <param name="input">任务请求。</param>
    /// <returns>入队结果。</returns>
    Task<WorkflowProjectTaskEnqueueResultDto> EnqueueProjectTaskAsync(
        WorkflowProjectTaskEnqueueInput input
    );

    /// <summary>
    /// 查询项目任务列表。
    /// </summary>
    /// <param name="input">查询参数。</param>
    /// <returns>任务列表。</returns>
    Task<List<WorkflowProjectTaskStatusDto>> GetProjectTasksAsync(
        WorkflowProjectTaskListInput input
    );

    /// <summary>
    /// 查询单个项目任务状态。
    /// </summary>
    /// <param name="taskId">任务 ID。</param>
    /// <returns>任务状态。</returns>
    Task<WorkflowProjectTaskStatusDto> GetProjectTaskStatusAsync(Guid taskId);

    /// <summary>
    /// 请求取消项目任务。
    /// </summary>
    /// <param name="taskId">任务 ID。</param>
    Task CancelProjectTaskAsync(Guid taskId);

    /// <summary>
    /// 修改项目任务。
    /// </summary>
    /// <param name="taskId">任务 ID。</param>
    /// <param name="input">修改请求。</param>
    Task<WorkflowProjectTaskStatusDto> UpdateProjectTaskAsync(
        Guid taskId,
        WorkflowProjectTaskUpdateInput input
    );

    /// <summary>
    /// 删除项目任务。
    /// </summary>
    /// <param name="taskId">任务 ID。</param>
    Task DeleteProjectTaskAsync(Guid taskId);

    /// <summary>
    /// 触发工作流执行。
    /// </summary>
    /// <param name="input">执行请求。</param>
    /// <returns>触发结果与当前状态快照。</returns>
    Task<WorkflowExecutionTriggerResultDto> ExecuteAsync(WorkflowExecutionTriggerInput input);

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
