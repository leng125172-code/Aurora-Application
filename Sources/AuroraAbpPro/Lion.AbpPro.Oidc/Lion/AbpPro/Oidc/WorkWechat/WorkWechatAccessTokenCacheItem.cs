namespace Lion.AbpPro.Oidc.WorkWechat;

public class WorkWechatAccessTokenCacheItem
{
    public string AccessToken { get; set; }

    private const string CacheKeyFormat = "WorkWechat:CorpId:{0}";

    public static string CalculateCacheKey(string corpId)
    {
        return string.Format(CacheKeyFormat, corpId);
    }
}
