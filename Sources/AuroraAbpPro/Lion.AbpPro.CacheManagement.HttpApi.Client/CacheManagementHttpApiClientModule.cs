namespace Lion.AbpPro.CacheManagement
{
    [DependsOn(typeof(CacheManagementApplicationContractsModule), typeof(AbpHttpClientModule))]
    public class CacheManagementHttpApiClientModule : AbpModule
    {
        public const string RemoteServiceName = "CacheManagement";

        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            context.Services.AddHttpClientProxies(
                typeof(CacheManagementApplicationContractsModule).Assembly,
                RemoteServiceName
            );
        }
    }
}
