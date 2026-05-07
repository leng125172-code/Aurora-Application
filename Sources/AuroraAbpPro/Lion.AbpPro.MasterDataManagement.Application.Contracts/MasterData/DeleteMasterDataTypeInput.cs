using System.ComponentModel.DataAnnotations;

namespace Lion.AbpPro.MasterDataManagement.MasterData;

/// <summary>
/// 删除主数据类型输入参数
/// </summary>
public class DeleteMasterDataTypeInput
{
    /// <summary>
    /// 编码
    /// </summary>
    [Required]
    public Guid Id { get; set; }
}
