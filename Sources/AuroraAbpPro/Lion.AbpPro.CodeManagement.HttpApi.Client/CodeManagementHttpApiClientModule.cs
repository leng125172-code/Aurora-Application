namespace Lion.AbpPro.CodeManagement
{
    [DependsOn(typeof(CodeManagementApplicationContractsModule), typeof(AbpHttpClientModule))]
    public class CodeManagementHttpApiClientModule : AbpModule
    {
        public const string RemoteServiceName = "CodeManagement";

        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            context.Services.AddHttpClientProxies(
                typeof(CodeManagementApplicationContractsModule).Assembly,
                RemoteServiceName
            );
        }
    }
}
