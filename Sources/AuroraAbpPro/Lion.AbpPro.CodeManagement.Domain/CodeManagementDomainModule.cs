using Volo.Abp.VirtualFileSystem;

namespace Lion.AbpPro.CodeManagement
{
    [DependsOn(
        typeof(AbpDddDomainModule),
        typeof(CodeManagementDomainSharedModule),
        typeof(AbpCachingModule)
    )]
    public class CodeManagementDomainModule : AbpModule
    {
        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            // 配置虚拟文件系统
            Configure<AbpVirtualFileSystemOptions>(options =>
            {
                options.FileSets.AddEmbedded<CodeManagementDomainModule>();
            });
        }
    }
}
