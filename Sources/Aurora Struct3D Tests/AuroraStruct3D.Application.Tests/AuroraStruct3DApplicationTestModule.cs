using Volo.Abp.Modularity;

namespace AuroraStruct3D
{
    [DependsOn(typeof(AuroraStruct3DApplicationModule), typeof(AuroraStruct3DDomainTestModule))]
    public class AuroraStruct3DApplicationTestModule : AbpModule { }
}
