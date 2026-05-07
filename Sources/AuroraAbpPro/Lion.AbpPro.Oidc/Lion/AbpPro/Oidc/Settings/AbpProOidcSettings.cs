namespace Lion.AbpPro.Oidc.Settings;

public static class AbpProOidcSettings
{
    /// <summary>
    /// 扩展登录
    /// </summary>
    public static class OidcSetting
    {
        public const string Default = "Setting.Group";

        public const string Tag = "Setting.Oidc";

        public const string Oidc = Default + ".Oidc";

        public static class Github
        {
            public const string Group = Oidc + "." + AbpProOidcConsts.Github;

            /// <summary>
            /// 是否启用
            /// </summary>
            public const string Enabled = Group + "." + AbpProOidcConsts.Enabled;

            /// <summary>
            /// 第三方登录地址
            /// </summary>
            public const string HostUrl = Group + "." + AbpProOidcConsts.HostUrl;

            /// <summary>
            /// 第三方登录图标
            /// </summary>
            /// <returns></returns>
            public const string Icon = Group + "." + AbpProOidcConsts.Icon;

            /// <summary>
            /// ClientName
            /// </summary>
            public const string ClientName = Group + "." + AbpProOidcConsts.ClientName;

            /// <summary>
            /// ClientId
            /// </summary>
            public const string ClientId = Group + "." + AbpProOidcConsts.ClientId;

            /// <summary>
            /// ClientSecret
            /// c0aeb3b8de25ffd203af80c21a62e187b6921cf1
            /// </summary>
            public const string ClientSecret = Group + "." + AbpProOidcConsts.ClientSecret;

            /// <summary>
            /// 重定向地址
            /// </summary>
            public const string RedirectUrl = Group + "." + AbpProOidcConsts.RedirectUrl;

            /// <summary>
            /// 重定向地址
            /// </summary>
            public const string AuthUri = Group + "." + AbpProOidcConsts.AuthUri;
        }

        public static class Gitee
        {
            public const string Group = Oidc + "." + AbpProOidcConsts.Gitee;

            /// <summary>
            /// 是否启用
            /// </summary>
            public const string Enabled = Group + "." + AbpProOidcConsts.Enabled;

            /// <summary>
            /// 第三方登录地址
            /// </summary>
            public const string HostUrl = Group + "." + AbpProOidcConsts.HostUrl;

            /// <summary>
            /// 第三方登录图标
            /// </summary>
            /// <returns></returns>
            public const string Icon = Group + "." + AbpProOidcConsts.Icon;

            /// <summary>
            /// ClientName
            /// </summary>
            public const string ClientName = Group + "." + AbpProOidcConsts.ClientName;

            /// <summary>
            /// ClientId
            /// </summary>
            public const string ClientId = Group + "." + AbpProOidcConsts.ClientId;

            /// <summary>
            /// ClientSecret
            /// c0aeb3b8de25ffd203af80c21a62e187b6921cf1
            /// </summary>
            public const string ClientSecret = Group + "." + AbpProOidcConsts.ClientSecret;

            /// <summary>
            /// 重定向地址
            /// </summary>
            public const string RedirectUrl = Group + "." + AbpProOidcConsts.RedirectUrl;

            /// <summary>
            /// 重定向地址
            /// </summary>
            public const string AuthUri = Group + "." + AbpProOidcConsts.AuthUri;
        }

        public static class DingTalk
        {
            public const string Group = Oidc + "." + AbpProOidcConsts.DingTalk;

            /// <summary>
            /// 是否启用
            /// </summary>
            public const string Enabled = Group + "." + AbpProOidcConsts.Enabled;

            /// <summary>
            /// 第三方登录地址
            /// </summary>
            public const string HostUrl = Group + "." + AbpProOidcConsts.HostUrl;

            /// <summary>
            /// 第三方登录图标
            /// </summary>
            public const string Icon = Group + "." + AbpProOidcConsts.Icon;

            /// <summary>
            /// ClientName
            /// </summary>
            public const string ClientName = Group + "." + AbpProOidcConsts.ClientName;

            /// <summary>
            /// ClientId (对应钉钉的AppKey)
            /// </summary>
            public const string ClientId = Group + "." + AbpProOidcConsts.ClientId;

            /// <summary>
            /// ClientSecret (对应钉钉的AppSecret)
            /// </summary>
            public const string ClientSecret = Group + "." + AbpProOidcConsts.ClientSecret;

            /// <summary>
            /// 重定向地址
            /// </summary>
            public const string RedirectUrl = Group + "." + AbpProOidcConsts.RedirectUrl;

            /// <summary>
            /// 认证地址
            /// </summary>
            public const string AuthUri = Group + "." + AbpProOidcConsts.AuthUri;
        }

        public static class WorkWechat
        {
            public const string Group = Oidc + "." + AbpProOidcConsts.WorkWechat;

            /// <summary>
            /// 是否启用
            /// </summary>
            public const string Enabled = Group + "." + AbpProOidcConsts.Enabled;

            /// <summary>
            /// 第三方登录地址
            /// </summary>
            public const string HostUrl = Group + "." + AbpProOidcConsts.HostUrl;

            /// <summary>
            /// 第三方登录图标
            /// </summary>
            public const string Icon = Group + "." + AbpProOidcConsts.Icon;

            /// <summary>
            /// ClientName
            /// </summary>
            public const string ClientName = Group + "." + AbpProOidcConsts.ClientName;

            /// <summary>
            /// ClientId (对应企业微信的CorpId)
            /// </summary>
            public const string ClientId = Group + "." + AbpProOidcConsts.ClientId;

            /// <summary>
            /// ClientSecret (对应企业微信的应用Secret)
            /// </summary>
            public const string ClientSecret = Group + "." + AbpProOidcConsts.ClientSecret;

            /// <summary>
            /// 重定向地址
            /// </summary>
            public const string RedirectUrl = Group + "." + AbpProOidcConsts.RedirectUrl;

            /// <summary>
            /// 认证地址
            /// </summary>
            public const string AuthUri = Group + "." + AbpProOidcConsts.AuthUri;

            /// <summary>
            /// AgentId (企业微信应用ID)
            /// </summary>
            public const string AgentId = Group + ".AgentId";
        }
    }
}
