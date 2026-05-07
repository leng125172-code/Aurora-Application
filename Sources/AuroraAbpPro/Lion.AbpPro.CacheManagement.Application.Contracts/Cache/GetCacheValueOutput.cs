namespace Lion.AbpPro.CacheManagement.Cache;

public class GetCacheValueOutput
{
    /// <summary>
    /// 类型
    /// </summary>
    public string Type { get; set; }

    /// <summary>
    /// 大小
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// 过期时间
    /// </summary>
    public long Ttl { get; set; }

    /// <summary>
    /// 值
    /// </summary>
    public IDictionary<string, object> Values { get; set; }
}
