namespace Lion.AbpPro.CacheManagement.Cache;

public interface ICacheAppService : IApplicationService
{
    Task<PagedResultDto<GetCacheKeysOutput>> GetKeysAsync(GetCacheKeysInput input);

    Task<GetCacheValueOutput> GetValueAsync(GetCacheValueInput input);

    Task RemoveAsync(RemoveCacheInput input);
}
