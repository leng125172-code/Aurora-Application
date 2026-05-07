namespace Lion.AbpPro.ImportExportManagement
{
    public abstract class ImportExportManagementAppService : ApplicationService
    {
        protected ImportExportManagementAppService()
        {
            LocalizationResource = typeof(ImportExportManagementResource);
            ObjectMapperContext = typeof(ImportExportManagementApplicationModule);
        }
    }
}
