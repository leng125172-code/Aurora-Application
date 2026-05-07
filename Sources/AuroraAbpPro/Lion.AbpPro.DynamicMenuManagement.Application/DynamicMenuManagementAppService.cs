namespace Lion.AbpPro.DynamicMenuManagement
{
    public abstract class DynamicMenuManagementAppService : ApplicationService
    {
        protected DynamicMenuManagementAppService()
        {
            LocalizationResource = typeof(DynamicMenuManagementResource);
            ObjectMapperContext = typeof(DynamicMenuManagementApplicationModule);
        }
    }
}
