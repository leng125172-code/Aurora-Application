using Volo.Abp.Modularity;

namespace AuroraCV
{
    [DependsOn(typeof(AuroraCVApplicationModule), typeof(AuroraCVDomainTestModule))]
    public class AuroraCVApplicationTestModule : AbpModule { }
}
