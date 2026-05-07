namespace Lion.AbpPro.MasterDataManagement.MasterDatas;

/// <summary>
/// 主数据
/// </summary>
public class MasterDataDto
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
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; }

    private const string CacheKeyFormat = "i:{0}";

    public static string CalculateCacheKey(Guid id)
    {
        return string.Format(CacheKeyFormat, id);
    }
}
