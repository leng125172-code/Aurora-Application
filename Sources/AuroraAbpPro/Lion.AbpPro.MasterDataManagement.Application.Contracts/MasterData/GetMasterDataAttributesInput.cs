namespace Lion.AbpPro.MasterDataManagement.MasterData;

/// <summary>
/// 获取主数据属性输入参数
/// </summary>
public class GetMasterDataAttributesInput
{
    /// <summary>
    /// 主数据类型Id
    /// </summary>
    public Guid? MasterDataTypeId { get; set; }

    /// <summary>
    /// 主数据类型编码
    /// </summary>
    public string MasterDataTypeCode { get; set; }
}
