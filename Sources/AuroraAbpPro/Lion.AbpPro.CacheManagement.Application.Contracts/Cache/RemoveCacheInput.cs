using System.ComponentModel.DataAnnotations;

namespace Lion.AbpPro.CacheManagement.Cache;

public class RemoveCacheInput
{
    [Required]
    public string Key { get; set; }
}
