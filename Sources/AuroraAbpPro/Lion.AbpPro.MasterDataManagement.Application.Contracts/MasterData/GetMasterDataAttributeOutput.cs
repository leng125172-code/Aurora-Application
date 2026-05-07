namespace Lion.AbpPro.MasterDataManagement.MasterData;

/// <summary>
/// 主数据属性输出参数
/// </summary>
public class GetMasterDataAttributeOutput
{
    /// <summary>
    /// 主键Id
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }

    /// <summary>
    /// 主数据类型Id
    /// </summary>
    public Guid MasterDataTypeId { get; set; }

    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 编码
    /// </summary>
    public string Code { get; set; }

    /// <summary>
    /// 属性类型(string,date,long等)
    /// </summary>
    public string AttributeType { get; set; }
}
