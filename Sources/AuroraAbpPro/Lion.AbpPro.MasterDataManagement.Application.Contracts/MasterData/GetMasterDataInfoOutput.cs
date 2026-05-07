namespace Lion.AbpPro.MasterDataManagement.MasterData;

/// <summary>
/// 主数据信息输出参数
/// </summary>
public class GetMasterDataInfoOutput
{
    public Guid Id { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 主数据编码
    /// </summary>
    public string Code { get; set; }

    /// <summary>
    /// 主数据名称
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 主数据属性列表
    /// </summary>
    public List<GetMasterDataInfoAttributeOutput> Attributes { get; set; } =
        new List<GetMasterDataInfoAttributeOutput>();
}

/// <summary>
/// 主数据信息属性输出参数
/// </summary>
public class GetMasterDataInfoAttributeOutput
{
    public GetMasterDataInfoAttributeOutput(string value, string code, string name)
    {
        Value = value;
        Code = code;
        Name = name;
    }

    /// <summary>
    /// 属性值
    /// </summary>
    public string Value { get; set; }

    /// <summary>
    /// 主数据编码
    /// </summary>
    public string Code { get; set; }

    /// <summary>
    /// 主数据名称
    /// </summary>
    public string Name { get; set; }
}
