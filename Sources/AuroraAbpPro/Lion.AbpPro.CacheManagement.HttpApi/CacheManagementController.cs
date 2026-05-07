using Lion.AbpPro.CacheManagement.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace Lion.AbpPro.CacheManagement
{
    public abstract class CacheManagementController : AbpController
    {
        protected CacheManagementController()
        {
            LocalizationResource = typeof(CacheManagementResource);
        }
    }
}
