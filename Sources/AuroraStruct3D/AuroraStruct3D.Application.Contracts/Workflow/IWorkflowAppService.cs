using System.Text.Json;
using AuroraStruct3D.Workflow.Dtos;
using Volo.Abp.Application.Services;

namespace AuroraStruct3D.Workflow;

/// <summary>
/// 工作流应用服务接口。
/// ABP 自动生成 REST 端点，路由基础路径：<c>/api/app/workflow</c>。
/// 面向前端「离线编辑、整体提交」的工作流编辑器，提供新建 / 回显 / 修改保存能力。
/// <para>
/// 新建 / 修改的请求体直接为完整 WorkflowPayload（顶层含 <c>projectId</c> / <c>name</c> / <c>graphData</c>），
/// 无需外层 <c>{ projectId, name, content }</c> 包装。
/// </para>
/// </summary>
public interface IWorkflowAppService : IApplicationService
{
    /// <summary>
    /// 获取指定项目下的所有工作流列表（轻量，不含 GraphData 全文）。
    /// <c>GET /api/app/workflow/by-project/{projectId}</c>
    /// </summary>
    /// <param name="projectId">所属项目 ID</param>
    /// <returns>工作流简要列表</returns>
    Task<List<WorkflowBriefDto>> GetListByProjectIdAsync(Guid projectId);

    /// <summary>
    /// 新建工作流。请求体为完整 WorkflowPayload，后端从中读取 projectId / name。
    /// <c>POST /api/app/workflow</c>
    /// </summary>
    /// <param name="payload">完整 WorkflowPayload</param>
    /// <returns>新建后的工作流（含后端生成的工作流 ID）</returns>
    Task<WorkflowDto> CreateAsync(JsonElement payload);

    /// <summary>
    /// 回显工作流：通过所属项目 ID + 工作流 ID 获取完整内容。
    /// <c>GET /api/app/workflow/{id}?projectId={projectId}</c>
    /// </summary>
    /// <param name="projectId">所属项目 ID（用于归属校验）</param>
    /// <param name="id">工作流 ID</param>
    Task<WorkflowDto> GetAsync(Guid projectId, Guid id);

    /// <summary>
    /// 修改保存工作流：路由指定工作流 ID，请求体为完整 WorkflowPayload，
    /// 后端从中读取 projectId（归属校验）/ name，覆盖内容。
    /// <c>PUT /api/app/workflow/{id}</c>
    /// </summary>
    /// <param name="id">工作流 ID</param>
    /// <param name="payload">完整 WorkflowPayload</param>
    Task<WorkflowDto> UpdateAsync(Guid id, JsonElement payload);

    /// <summary>
    /// 删除工作流。先校验归属，再软删除。
    /// <c>DELETE /api/app/workflow/{id}?projectId={projectId}</c>
    /// </summary>
    /// <param name="projectId">所属项目 ID（用于归属校验）</param>
    /// <param name="id">工作流 ID</param>
    Task DeleteAsync(Guid projectId, Guid id);

    /// <summary>
    /// 静态校验工作流图：根据项目 ID 和工作流 ID 加载已持久化的工作流，返回诊断结果。
    /// <c>GET /api/app/workflow/{id}/validate?projectId={projectId}</c>
    /// </summary>
    /// <param name="projectId">所属项目 ID（用于归属校验）。</param>
    /// <param name="id">工作流 ID。</param>
    Task<WorkflowValidateResultDto> ValidateAsync(Guid projectId, Guid id);

    /// <summary>
    /// 数据流仿真：根据项目 ID 和工作流 ID 加载已持久化的工作流，不执行算子，
    /// 仅模拟变量绑定与流动路径，返回仿真报告。
    /// <c>GET /api/app/workflow/{id}/simulate?projectId={projectId}</c>
    /// </summary>
    /// <param name="projectId">所属项目 ID（用于归属校验）。</param>
    /// <param name="id">工作流 ID。</param>
    Task<WorkflowDataFlowReportDto> SimulateAsync(Guid projectId, Guid id);
}
