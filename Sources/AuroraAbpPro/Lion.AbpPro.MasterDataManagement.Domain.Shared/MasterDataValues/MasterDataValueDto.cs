namespace Lion.AbpPro.MasterDataManagement.MasterDataValues;

/// <summary>
/// 主数据值
/// </summary>
public class MasterDataValueDto
{
    /// <summary>
    /// 主键Id
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }

    public string ConcurrencyStamp { get; set; }

    /// <summary>
    /// 主数据Id(MasterData的Id)
    /// </summary>
    public Guid MasterDataId { get; set; }

    /// <summary>
    /// 主属性属性Id(MasterDataAttribute表的主键Id)
    /// </summary>
    public Guid MasterDataAttributeId { get; set; }

    /// <summary>
    /// 属性值
    /// </summary>
    public string Value { get; set; }

    private const string CacheKeyFormat = "i:{0}";

    public static string CalculateCacheKey(Guid id)
    {
        return string.Format(CacheKeyFormat, id);
    }
}
