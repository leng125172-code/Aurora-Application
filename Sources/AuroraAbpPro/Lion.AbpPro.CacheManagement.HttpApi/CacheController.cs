using Lion.AbpPro.CacheManagement.Cache;

namespace Lion.AbpPro.CacheManagement;

[Route("Caches")]
public class CacheController : CacheManagementController, ICacheAppService
{
    private readonly ICacheAppService _cacheAppService;

    public CacheController(ICacheAppService cacheAppService)
    {
        _cacheAppService = cacheAppService;
    }

    [HttpPost("keys")]
    [SwaggerOperation(summary: "获取缓存keys", Tags = new[] { "Caches" })]
    public Task<PagedResultDto<GetCacheKeysOutput>> GetKeysAsync(GetCacheKeysInput input)
    {
        return _cacheAppService.GetKeysAsync(input);
    }

    [HttpPost("value")]
    [SwaggerOperation(summary: "获取缓存value", Tags = new[] { "Caches" })]
    public Task<GetCacheValueOutput> GetValueAsync(GetCacheValueInput input)
    {
        return _cacheAppService.GetValueAsync(input);
    }

    [HttpPost("remove")]
    [SwaggerOperation(summary: "删除缓存", Tags = new[] { "Caches" })]
    public Task RemoveAsync(RemoveCacheInput input)
    {
        return _cacheAppService.RemoveAsync(input);
    }
}
