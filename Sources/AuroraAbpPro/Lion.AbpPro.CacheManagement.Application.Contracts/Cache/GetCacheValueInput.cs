using System.ComponentModel.DataAnnotations;

namespace Lion.AbpPro.CacheManagement.Cache;

public class GetCacheValueInput
{
    [Required]
    public string? Key { get; set; }
}
