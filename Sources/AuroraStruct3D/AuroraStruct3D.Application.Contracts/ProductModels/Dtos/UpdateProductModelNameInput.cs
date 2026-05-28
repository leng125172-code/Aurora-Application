using System.ComponentModel.DataAnnotations;
using AuroraStruct3D.ProductModels;

namespace AuroraStruct3D.ProductModels.Dtos;

/// <summary>
/// 修改数模名称的输入 DTO
/// </summary>
public class UpdateProductModelNameInput
{
    /// <summary>新的显示名称（必填，最大 256 字符）</summary>
    [Required]
    [MaxLength(ProductModelConsts.MaxNameLength)]
    public string Name { get; set; } = string.Empty;
}
