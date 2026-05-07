using Lion.AbpPro.TemplateManagement.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace Lion.AbpPro.TemplateManagement
{
    public abstract class TemplateManagementController : AbpController
    {
        protected TemplateManagementController()
        {
            LocalizationResource = typeof(TemplateManagementResource);
        }
    }
}
