namespace Lion.AbpPro.TwoFactory;

public class TwoFactoryCacheItem
{
    public TwoFactoryCacheItem(bool isValid = true)
    {
        IsValid = isValid;
    }

    public bool IsValid { get; set; }

    private const string CacheKeyFormat = "t:{0}";

    public static string CalculateCacheKey(long timeStepMatched)
    {
        return string.Format(CacheKeyFormat, timeStepMatched);
    }
}
