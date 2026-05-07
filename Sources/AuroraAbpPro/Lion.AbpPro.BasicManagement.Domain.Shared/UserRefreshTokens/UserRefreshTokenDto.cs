namespace Lion.AbpPro.UserRefreshTokens;

/// <summary>
/// 用户token
/// </summary>
public class UserRefreshTokenDto
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
    /// 刷新token
    /// </summary>
    public string RefreshToken { get; set; }

    /// <summary>
    /// 用户id
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 是否使用
    /// </summary>
    public bool IsUsed { get; set; }

    /// <summary>
    /// 过期时间
    /// </summary>
    public DateTime ExpirationTime { get; set; }

    /// <summary>
    /// Token
    /// </summary>
    public string Token { get; set; }

    private const string CacheKeyFormat = "i:{0}";

    public static string CalculateCacheKey(Guid id)
    {
        return string.Format(CacheKeyFormat, id);
    }
}
