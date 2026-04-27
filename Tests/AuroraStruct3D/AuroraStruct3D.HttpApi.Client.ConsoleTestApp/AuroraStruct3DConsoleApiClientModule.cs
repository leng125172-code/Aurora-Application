namespace AuroraStruct3D.HttpApi.Client.ConsoleTestApp
{
    [DependsOn(
        typeof(AuroraStruct3DHttpApiClientModule),
        typeof(AbpHttpClientIdentityModelModule)
        )]
    public class AuroraStruct3DConsoleApiClientModule : AbpModule
    {
        public override void PreConfigureServices(ServiceConfigurationContext context)
        {
            PreConfigure<AbpHttpClientBuilderOptions>(options =>
            {
                options.ProxyClientBuildActions.Add((remoteServiceName, clientBuilder) =>
                {
                    clientBuilder.AddTransientHttpErrorPolicy(
                        policyBuilder => policyBuilder.WaitAndRetryAsync(3, i => TimeSpan.FromSeconds(Math.Pow(2, i)))
                    );
                });
            });
        }
    }
}
