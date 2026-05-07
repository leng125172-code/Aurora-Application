namespace Lion.AbpPro.CodeManagement
{
    public abstract class CodeManagementAppService : ApplicationService
    {
        protected CodeManagementAppService()
        {
            LocalizationResource = typeof(CodeManagementResource);
            ObjectMapperContext = typeof(CodeManagementApplicationModule);
        }
    }
}
