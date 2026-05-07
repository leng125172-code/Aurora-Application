namespace Lion.AbpPro.MasterDataManagement
{
    [DependsOn(typeof(MasterDataManagementApplicationContractsModule), typeof(AbpHttpClientModule))]
    public class MasterDataManagementHttpApiClientModule : AbpModule
    {
        public const string RemoteServiceName = "MasterDataManagement";

        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            context.Services.AddHttpClientProxies(
                typeof(MasterDataManagementApplicationContractsModule).Assembly,
                RemoteServiceName
            );
        }
    }
}
