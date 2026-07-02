namespace AuroraStruct3D.Variables.Dtos;

/// <summary>
/// 离线变量定义视图。
/// </summary>
public class VariableDefinitionDto
{
    /// <summary>变量定义 ID。</summary>
    public Guid VariableId { get; set; }

    /// <summary>所属工作流 ID。</summary>
    public Guid OwnerWorkflowId { get; set; }

    /// <summary>变量名。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>变量类型名。</summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>可见性（Private/PublicRead）。</summary>
    public VariableVisibility Visibility { get; set; }

    /// <summary>可变性（Mutable/Readonly/ConstInit）。</summary>
    public VariableMutability Mutability { get; set; }

    /// <summary>是否要求实例执行前初始化。</summary>
    public bool IsRequiredInit { get; set; }

    /// <summary>默认值（JSON）。</summary>
    public string? DefaultValueJson { get; set; }
}
