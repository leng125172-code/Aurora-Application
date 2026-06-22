namespace AuroraStruct3D.Workflow.Dtos;

/// <summary>
/// 工作流节点面板完整数据，作为 <c>GET /api/app/workflow-node-palette</c> 接口的响应根对象。
/// <para>
/// 前端渲染节点面板时使用此 DTO，结构为：
/// Category → NodeDefinition。
/// </para>
/// </summary>
public sealed class NodePaletteDto
{
    /// <summary>所有节点分组列表，顺序固定：内置节点组在前，算子组按 Category 名排序在后。</summary>
    public required IReadOnlyList<NodeCategoryDto> Categories { get; init; }
}
