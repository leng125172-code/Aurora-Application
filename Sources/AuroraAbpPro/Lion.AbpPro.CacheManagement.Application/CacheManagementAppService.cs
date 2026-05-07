namespace Lion.AbpPro.CacheManagement
{
    public abstract class CacheManagementAppService : ApplicationService
    {
        protected CacheManagementAppService()
        {
            LocalizationResource = typeof(CacheManagementResource);
            ObjectMapperContext = typeof(CacheManagementApplicationModule);
        }
    }
}
