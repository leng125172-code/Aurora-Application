using Lion.AbpPro.DynamicMenuManagement.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace Lion.AbpPro.DynamicMenuManagement
{
    public abstract class DynamicMenuManagementController : AbpController
    {
        protected DynamicMenuManagementController()
        {
            LocalizationResource = typeof(DynamicMenuManagementResource);
        }
    }
}
