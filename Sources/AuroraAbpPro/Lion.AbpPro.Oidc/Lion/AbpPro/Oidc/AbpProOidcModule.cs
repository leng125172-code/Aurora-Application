using Lion.AbpPro.Core;
using Lion.AbpPro.Localization;
using Lion.AbpPro.Oidc.DingTalk;
using Lion.AbpPro.Oidc.Gitee;
using Lion.AbpPro.Oidc.Github;
using Lion.AbpPro.Oidc.WorkWechat;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Caching;
using Volo.Abp.Localization;
using Volo.Abp.Localization.ExceptionHandling;
using Volo.Abp.Modularity;
using Volo.Abp.VirtualFileSystem;

namespace Lion.AbpPro.Oidc;

[DependsOn(typeof(AbpProCoreModule), typeof(AbpCachingModule))]
public class AbpProOidcModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddHttpClient();
        context.Services.AddKeyedTransient<IExternalLoginProvider, GithubExternalLoginProvider>(
            AbpProOidcConsts.Github
        );
        context.Services.AddKeyedTransient<IExternalLoginProvider, GiteeExternalLoginProvider>(
            AbpProOidcConsts.Gitee
        );
        context.Services.AddKeyedTransient<IExternalLoginProvider, DingTalkExternalLoginProvider>(
            AbpProOidcConsts.DingTalk
        );
        context.Services.AddKeyedTransient<IExternalLoginProvider, WorkWechatExternalLoginProvider>(
            AbpProOidcConsts.WorkWechat
        );
        Configure<AbpExceptionLocalizationOptions>(options =>
        {
            options.MapCodeNamespace(
                AbpProLocalizationConsts.NameSpace,
                typeof(AbpProLocalizationResource)
            );
        });
    }
}
