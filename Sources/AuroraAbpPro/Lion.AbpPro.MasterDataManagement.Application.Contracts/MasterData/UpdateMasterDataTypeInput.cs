using System;
using System.ComponentModel.DataAnnotations;

namespace Lion.AbpPro.MasterDataManagement.MasterData;

/// <summary>
/// 更新主数据类型输入参数
/// </summary>
public class UpdateMasterDataTypeInput
{
    /// <summary>
    /// Id
    /// </summary>
    [Required]
    public Guid Id { get; set; }

    /// <summary>
    /// 名称
    /// </summary>
    [Required]
    public string Name { get; set; }

    /// <summary>
    /// 编码
    /// </summary>
    [Required]
    public string Code { get; set; }
}
