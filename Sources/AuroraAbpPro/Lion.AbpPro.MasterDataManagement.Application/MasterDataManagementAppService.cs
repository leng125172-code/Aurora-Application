namespace Lion.AbpPro.MasterDataManagement
{
    public abstract class MasterDataManagementAppService : ApplicationService
    {
        protected MasterDataManagementAppService()
        {
            LocalizationResource = typeof(MasterDataManagementResource);
            ObjectMapperContext = typeof(MasterDataManagementApplicationModule);
        }
    }
}
