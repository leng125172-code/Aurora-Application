using AuroraStruct3D.Workflow.Dtos;
using Volo.Abp.Application.Services;

namespace AuroraStruct3D.Workflow;

/// <summary>
/// 工作流执行应用服务接口。
/// ABP 自动生成 REST 端点：<c>POST /api/app/workflow-execution/run</c>。
/// 加载工作流定义 → 编译 → 注入 Redis 暂存初始变量 → 执行 → 写回指定结果变量 → 返回摘要。
/// </summary>
public interface IWorkflowExecutionAppService : IApplicationService
{
    /// <summary>
    /// 执行一个已保存的工作流。
    /// </summary>
    /// <param name="input">执行参数（项目 / 工作流 ID + 变量进出约定）。</param>
    Task<WorkflowRunResultDto> RunAsync(RunWorkflowInput input);
}
