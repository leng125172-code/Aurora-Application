using AuroraStruct3D.Workflow.Dtos;

namespace AuroraStruct3D.Workflow;

/// <summary>
/// 工作流节点面板应用服务接口。
/// ABP 自动生成对应 REST 端点：<c>GET /api/app/workflow-node-palette</c>
/// <para>
/// 返回完整节点面板定义，供前端工作流编辑器渲染节点库面板。
/// 包含：
/// <list type="number">
///   <item>内置结构节点（ForLoop、IfElse、Assign 等）</item>
///   <item>算子节点（由程序集扫描自动发现，来自 IOperatorRegistry）</item>
/// </list>
/// </para>
/// </summary>
public interface IWorkflowNodePaletteAppService : IApplicationService
{
    /// <summary>
    /// 获取完整节点面板数据。
    /// 结果包含所有内置结构节点和已注册算子节点，按 Category/SubCategory/DisplayName 分组排序。
    /// GET /api/app/workflow-node-palette
    /// </summary>
    /// <returns>节点面板数据，按分组组织。</returns>
    Task<NodePaletteDto> GetAsync();
}
