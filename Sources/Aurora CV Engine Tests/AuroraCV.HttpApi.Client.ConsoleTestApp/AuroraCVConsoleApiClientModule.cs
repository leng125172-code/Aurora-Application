namespace AuroraCV.HttpApi.Client.ConsoleTestApp
{
    [DependsOn(typeof(AuroraCVHttpApiClientModule), typeof(AbpHttpClientIdentityModelModule))]
    public class AuroraCVConsoleApiClientModule : AbpModule
    {
        public override void PreConfigureServices(ServiceConfigurationContext context)
        {
            PreConfigure<AbpHttpClientBuilderOptions>(options =>
            {
                options.ProxyClientBuildActions.Add(
                    (remoteServiceName, clientBuilder) =>
                    {
                        clientBuilder.AddTransientHttpErrorPolicy(policyBuilder =>
                            policyBuilder.WaitAndRetryAsync(
                                3,
                                i => TimeSpan.FromSeconds(Math.Pow(2, i))
                            )
                        );
                    }
                );
            });
        }
    }
}
