namespace AuroraStruct3D.Workflow.Dtos;

/// <summary>
/// 节点一级分组（Category），对应算子的 [Category] 特性值，或内置节点分组名。
/// </summary>
public sealed class NodeCategoryDto
{
    /// <summary>一级分组名称，如 "文件操作"、"流程控制"、"参数赋值"。</summary>
    public required string Name { get; init; }

    /// <summary>
    /// 属于本分组的节点列表，按 DisplayName 排序。
    /// </summary>
    public IReadOnlyList<NodeDefinitionDto>? Nodes { get; init; }
}
