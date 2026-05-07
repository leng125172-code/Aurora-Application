namespace Lion.AbpPro.CacheManagement.Cache;

[Authorize(CacheManagementPermissions.CacheManagement.Default)]
public class CacheAppService : CacheManagementAppService, ICacheAppService
{
    private readonly ICacheManager _cacheManager;

    public CacheAppService(ICacheManager cacheManager)
    {
        _cacheManager = cacheManager;
    }

    public virtual async Task<PagedResultDto<GetCacheKeysOutput>> GetKeysAsync(
        GetCacheKeysInput input
    )
    {
        var result = new PagedResultDto<GetCacheKeysOutput>();
        var keys = (await _cacheManager.GetKeysAsync(input.Key)).OrderBy(e => e);
        result.Items = keys.OrderBy(e => e)
            .Select(e => new GetCacheKeysOutput { Key = e })
            .ToList();
        result.TotalCount = keys.Count();
        return result;
    }

    [Authorize(CacheManagementPermissions.CacheManagement.LookValue)]
    public virtual async Task<GetCacheValueOutput> GetValueAsync(GetCacheValueInput input)
    {
        var result = await _cacheManager.GetValueAsync(input.Key);
        return result.Adapt<GetCacheValueOutput>();
    }

    [Authorize(CacheManagementPermissions.CacheManagement.Delete)]
    public virtual async Task RemoveAsync(RemoveCacheInput input)
    {
        await _cacheManager.RemoveAsync(input.Key);
    }
}
