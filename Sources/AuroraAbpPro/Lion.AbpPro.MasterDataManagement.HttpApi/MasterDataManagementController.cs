using Lion.AbpPro.MasterDataManagement.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace Lion.AbpPro.MasterDataManagement
{
    public abstract class MasterDataManagementController : AbpController
    {
        protected MasterDataManagementController()
        {
            LocalizationResource = typeof(MasterDataManagementResource);
        }
    }
}
