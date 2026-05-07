namespace Lion.AbpPro.DynamicMenuManagement
{
    [DependsOn(
        typeof(DynamicMenuManagementApplicationContractsModule),
        typeof(AbpHttpClientModule)
    )]
    public class DynamicMenuManagementHttpApiClientModule : AbpModule
    {
        public const string RemoteServiceName = "DynamicMenuManagement";

        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            context.Services.AddHttpClientProxies(
                typeof(DynamicMenuManagementApplicationContractsModule).Assembly,
                RemoteServiceName
            );
        }
    }
}
