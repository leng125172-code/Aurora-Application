using System.ComponentModel.DataAnnotations;

namespace AuroraStruct3D.Variables.Dtos;

/// <summary>
/// 变量声明（归属于某个工作流）。
/// </summary>
public class VariableDeclarationDto
{
    /// <summary>变量名（在所属工作流内唯一）。</summary>
    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    /// <summary>变量类型名（例如：string/int/double/bool/Mat/PointCloudData）。</summary>
    [Required]
    [MaxLength(128)]
    public string TypeName { get; set; } = string.Empty;

    /// <summary>可见性（Private/PublicRead）。</summary>
    public VariableVisibility Visibility { get; set; } = VariableVisibility.Private;

    /// <summary>可变性（Mutable/Readonly/ConstInit）。</summary>
    public VariableMutability Mutability { get; set; } = VariableMutability.Mutable;

    /// <summary>是否要求实例执行前完成初始化。</summary>
    public bool IsRequiredInit { get; set; }

    /// <summary>默认值（JSON 字符串，可为空）。</summary>
    [MaxLength(4096)]
    public string? DefaultValueJson { get; set; }
}
