using System;
using System.ComponentModel.DataAnnotations;

namespace Lion.AbpPro.MasterDataManagement.MasterData;

/// <summary>
/// 删除主数据属性输入参数
/// </summary>
public class DeleteMasterDataAttributeInput
{
    /// <summary>
    /// 主数据属性Id
    /// </summary>
    [Required]
    public Guid Id { get; set; }
}
