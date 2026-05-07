namespace Lion.AbpPro.MasterDataManagement.MasterData;

/// <summary>
/// 主数据类型输出参数
/// </summary>
public class MasterDataTypeOutput
{
    /// <summary>
    /// 主键Id
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 编码
    /// </summary>
    public string Code { get; set; }
}
