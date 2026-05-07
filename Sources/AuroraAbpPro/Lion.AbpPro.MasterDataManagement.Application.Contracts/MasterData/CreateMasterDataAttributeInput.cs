using System.ComponentModel.DataAnnotations;

namespace Lion.AbpPro.MasterDataManagement.MasterData;

/// <summary>
/// 创建主数据属性输入参数
/// </summary>
public class CreateMasterDataAttributeInput
{
    /// <summary>
    /// 主数据类型Id
    /// </summary>
    [Required]
    public Guid MasterDataTypeId { get; set; }

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

    /// <summary>
    /// 属性类型(string,date,long等)
    /// </summary>
    [Required]
    public string AttributeType { get; set; }
}
