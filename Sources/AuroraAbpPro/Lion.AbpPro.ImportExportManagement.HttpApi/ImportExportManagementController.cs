using Lion.AbpPro.ImportExportManagement.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace Lion.AbpPro.ImportExportManagement
{
    public abstract class ImportExportManagementController : AbpController
    {
        protected ImportExportManagementController()
        {
            LocalizationResource = typeof(ImportExportManagementResource);
        }
    }
}
