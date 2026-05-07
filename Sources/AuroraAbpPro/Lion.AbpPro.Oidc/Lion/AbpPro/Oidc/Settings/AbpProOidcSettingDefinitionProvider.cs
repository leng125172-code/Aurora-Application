using Lion.AbpPro.Core;
using Lion.AbpPro.Localization;
using Volo.Abp.Localization;
using Volo.Abp.Settings;

namespace Lion.AbpPro.Oidc.Settings;

public class AbpProOidcSettingDefinitionProvider : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        ConfigGithub(context);
        ConfigGitee(context);
        ConfigDingTalk(context);
        ConfigWorkWechat(context);
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<AbpProLocalizationResource>(name);
    }

    private void ConfigGithub(ISettingDefinitionContext context)
    {
        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.Github.Enabled,
                "false",
                L("Lion.AbpPro:Oidc.Enabled"),
                L("Lion.AbpPro:Oidc.Enabled")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.Github.Group
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeCheckBox
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
        );

        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.Github.ClientName,
                "应用名称,保持和github的一致",
                L("Lion.AbpPro:Oidc.ClientName"),
                L("Lion.AbpPro:Oidc.ClientName")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.Github.Group
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeText
                )
        );

        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.Github.ClientId,
                "github颁发的clientId",
                L("Lion.AbpPro:Oidc.ClientId"),
                L("Lion.AbpPro:Oidc.ClientId")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.Github.Group
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeText
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
        );

        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.Github.ClientSecret,
                "github颁发的clientSecret",
                L("Lion.AbpPro:Oidc.ClientSecret"),
                L("Lion.AbpPro:Oidc.ClientSecret")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.Github.Group
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeText
                )
        );

        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.Github.RedirectUrl,
                "http://localhost:4200/auth/oidc-login",
                L("Lion.AbpPro:Oidc.RedirectUrl"),
                L("Lion.AbpPro:Oidc.RedirectUrl")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.Github.Group
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeText
                )
        );

        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.Github.HostUrl,
                "https://github.com",
                L("Lion.AbpPro:Oidc.HostUrl"),
                L("Lion.AbpPro:Oidc.HostUrl")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.Github.Group
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeText
                )
        );

        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.Github.Icon,
                "mdi:github",
                L("Lion.AbpPro:Oidc.Icon"),
                L("Lion.AbpPro:Oidc.Icon")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.Github.Group
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeText
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
        );

        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.Github.AuthUri,
                "https://github.com/login/oauth/authorize?client_id=你的clientId&redirect_uri=你的回调地址&response_type=code&state=Github",
                L("Lion.AbpPro:Oidc.AuthUri"),
                L("Lion.AbpPro:Oidc.AuthUri")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.Github.Group
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeText
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
        );
    }

    private void ConfigGitee(ISettingDefinitionContext context)
    {
        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.Gitee.Enabled,
                "false",
                L("Lion.AbpPro:Oidc.Enabled"),
                L("Lion.AbpPro:Oidc.Enabled")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.Gitee.Group
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeCheckBox
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
        );

        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.Gitee.ClientName,
                "应用名称,保持和gitee的一致",
                L("Lion.AbpPro:Oidc.ClientName"),
                L("Lion.AbpPro:Oidc.ClientName")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.Gitee.Group
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeText
                )
        );

        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.Gitee.ClientId,
                "gitee颁发的clientId",
                L("Lion.AbpPro:Oidc.ClientId"),
                L("Lion.AbpPro:Oidc.ClientId")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.Gitee.Group
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeText
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
        );

        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.Gitee.ClientSecret,
                "gitee颁发的clientSecret",
                L("Lion.AbpPro:Oidc.ClientSecret"),
                L("Lion.AbpPro:Oidc.ClientSecret")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.Gitee.Group
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeText
                )
        );

        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.Gitee.RedirectUrl,
                "http://localhost:4200/auth/oidc-login",
                L("Lion.AbpPro:Oidc.RedirectUrl"),
                L("Lion.AbpPro:Oidc.RedirectUrl")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.Gitee.Group
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeText
                )
        );

        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.Gitee.HostUrl,
                "https://gitee.com",
                L("Lion.AbpPro:Oidc.HostUrl"),
                L("Lion.AbpPro:Oidc.HostUrl")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.Gitee.Group
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeText
                )
        );

        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.Gitee.Icon,
                "mdi:git",
                L("Lion.AbpPro:Oidc.Icon"),
                L("Lion.AbpPro:Oidc.Icon")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.Gitee.Group
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeText
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
        );

        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.Gitee.AuthUri,
                "https://gitee.com/oauth/authorize?client_id=你的clientId&redirect_uri=你的回调地址&response_type=code&state=Gitee",
                L("Lion.AbpPro:Oidc.AuthUri"),
                L("Lion.AbpPro:Oidc.AuthUri")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.Gitee.Group
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeText
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
        );
    }

    private void ConfigDingTalk(ISettingDefinitionContext context)
    {
        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.DingTalk.Enabled,
                "false",
                L("Lion.AbpPro:Oidc.Enabled"),
                L("Lion.AbpPro:Oidc.Enabled")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.DingTalk.Group
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeCheckBox
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
        );

        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.DingTalk.ClientName,
                "应用名称,保持和钉钉应用的一致",
                L("Lion.AbpPro:Oidc.ClientName"),
                L("Lion.AbpPro:Oidc.ClientName")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.DingTalk.Group
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeText
                )
        );

        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.DingTalk.ClientId,
                "钉钉应用的AppKey",
                L("Lion.AbpPro:Oidc.ClientId"),
                L("Lion.AbpPro:Oidc.ClientId")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.DingTalk.Group
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeText
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
        );

        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.DingTalk.ClientSecret,
                "钉钉应用的AppSecret",
                L("Lion.AbpPro:Oidc.ClientSecret"),
                L("Lion.AbpPro:Oidc.ClientSecret")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.DingTalk.Group
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeText
                )
        );

        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.DingTalk.RedirectUrl,
                "http://localhost:4200/auth/oidc-login",
                L("Lion.AbpPro:Oidc.RedirectUrl"),
                L("Lion.AbpPro:Oidc.RedirectUrl")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.DingTalk.Group
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeText
                )
        );

        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.DingTalk.HostUrl,
                "https://api.dingtalk.com/v1.0",
                L("Lion.AbpPro:Oidc.HostUrl"),
                L("Lion.AbpPro:Oidc.HostUrl")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.DingTalk.Group
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeText
                )
        );

        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.DingTalk.Icon,
                "ant-design:dingtalk-circle-filled",
                L("Lion.AbpPro:Oidc.Icon"),
                L("Lion.AbpPro:Oidc.Icon")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.DingTalk.Group
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeText
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
        );

        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.DingTalk.AuthUri,
                "https://login.dingtalk.com/oauth2/auth?client_id=ding8m78mrovsjydeiq5&response_type=code&scope=openid corpid&state=DingTalk&redirect_uri=http%3A%2F%2Flocalhost%3A4200%2Fauth%2Foidc-login",
                L("Lion.AbpPro:Oidc.AuthUri"),
                L("Lion.AbpPro:Oidc.AuthUri")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.DingTalk.Group
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeText
                )
        );
    }

    private void ConfigWorkWechat(ISettingDefinitionContext context)
    {
        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.WorkWechat.Enabled,
                "false",
                L("Lion.AbpPro:Oidc.Enabled"),
                L("Lion.AbpPro:Oidc.Enabled")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.WorkWechat.Group
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeCheckBox
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
        );

        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.WorkWechat.ClientName,
                "企业微信应用名称",
                L("Lion.AbpPro:Oidc.ClientName"),
                L("Lion.AbpPro:Oidc.ClientName")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.WorkWechat.Group
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeText
                )
        );

        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.WorkWechat.ClientId,
                "企业微信的CorpId",
                L("Lion.AbpPro:Oidc.ClientId"),
                L("Lion.AbpPro:Oidc.ClientId")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.WorkWechat.Group
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeText
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
        );

        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.WorkWechat.ClientSecret,
                "企业微信应用的Secret",
                L("Lion.AbpPro:Oidc.ClientSecret"),
                L("Lion.AbpPro:Oidc.ClientSecret")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.WorkWechat.Group
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeText
                )
        );

        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.WorkWechat.AgentId,
                "企业微信应用的AgentId",
                L("Lion.AbpPro:Oidc.AgentId"),
                L("Lion.AbpPro:Oidc.AgentId")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.WorkWechat.Group
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeText
                )
        );

        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.WorkWechat.RedirectUrl,
                "http://localhost:4200/auth/oidc-login",
                L("Lion.AbpPro:Oidc.RedirectUrl"),
                L("Lion.AbpPro:Oidc.RedirectUrl")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.WorkWechat.Group
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeText
                )
        );

        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.WorkWechat.HostUrl,
                "https://qyapi.weixin.qq.com",
                L("Lion.AbpPro:Oidc.HostUrl"),
                L("Lion.AbpPro:Oidc.HostUrl")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.WorkWechat.Group
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeText
                )
        );

        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.WorkWechat.Icon,
                "ant-design:wechat-work-outlined",
                L("Lion.AbpPro:Oidc.Icon"),
                L("Lion.AbpPro:Oidc.Icon")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.WorkWechat.Group
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeText
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
        );

        context.Add(
            new SettingDefinition(
                AbpProOidcSettings.OidcSetting.WorkWechat.AuthUri,
                "https://login.work.weixin.qq.com/wwlogin/sso/login?login_type=CorpApp&appid=ww76403e3d18e5c269&agentid=1000002&redirect_uri=http%3A%2F%2Flocalhost%3A4200%2Fauth&state=WorkWechat",
                L("Lion.AbpPro:Oidc.AuthUri"),
                L("Lion.AbpPro:Oidc.AuthUri")
            )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Default,
                    AbpProOidcSettings.OidcSetting.WorkWechat.Group
                )
                .WithProperty(
                    AbpProOidcSettings.OidcSetting.Tag,
                    AbpProOidcSettings.OidcSetting.Tag
                )
                .WithProperty(
                    AbpProSettingConsts.ControlType.Default,
                    AbpProSettingConsts.ControlType.TypeText
                )
        );
    }
}
