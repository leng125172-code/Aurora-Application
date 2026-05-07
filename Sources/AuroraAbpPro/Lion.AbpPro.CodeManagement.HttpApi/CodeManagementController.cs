using Lion.AbpPro.CodeManagement.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace Lion.AbpPro.CodeManagement
{
    public abstract class CodeManagementController : AbpController
    {
        protected CodeManagementController()
        {
            LocalizationResource = typeof(CodeManagementResource);
        }
    }
}
