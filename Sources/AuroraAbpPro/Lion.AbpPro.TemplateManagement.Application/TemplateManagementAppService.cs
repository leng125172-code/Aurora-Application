using Lion.AbpPro.TemplateManagement.Localization;

namespace Lion.AbpPro.TemplateManagement
{
    public abstract class TemplateManagementAppService : ApplicationService
    {
        protected TemplateManagementAppService()
        {
            LocalizationResource = typeof(TemplateManagementResource);
            ObjectMapperContext = typeof(TemplateManagementApplicationModule);
        }
    }
}
