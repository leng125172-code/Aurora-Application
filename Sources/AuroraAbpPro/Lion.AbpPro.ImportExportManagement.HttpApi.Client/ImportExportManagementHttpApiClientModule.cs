namespace Lion.AbpPro.ImportExportManagement
{
    [DependsOn(
        typeof(ImportExportManagementApplicationContractsModule),
        typeof(AbpHttpClientModule)
    )]
    public class ImportExportManagementHttpApiClientModule : AbpModule
    {
        public const string RemoteServiceName = "ImportExportManagement";

        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            context.Services.AddHttpClientProxies(
                typeof(ImportExportManagementApplicationContractsModule).Assembly,
                RemoteServiceName
            );
        }
    }
}
