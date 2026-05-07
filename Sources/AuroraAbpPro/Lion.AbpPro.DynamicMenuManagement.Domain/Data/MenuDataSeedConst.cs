namespace Lion.AbpPro.DynamicMenuManagement.Data;

public class MenuDataSeedConst
{
    public static MenuDataSeed Dashboard = new()
    {
        Name = "dashboard",
        Path = "/dashboard",
        Component = "BasicLayout",
        Meta = new MenuMetaDataSeed()
        {
            Icon = "lucide:layout-dashboard",
            Title = "概览",
            DisplayTitle = "Lion.AbpPro.DynamicMenuManagement:Dashboard",
            Order = 1,
        },
        Children = new List<MenuDataSeed>()
        {
            new MenuDataSeed()
            {
                Name = "Analytics",
                Path = "/analytics",
                Component = "/views/dashboard/analytics/index.vue",
                Meta = new MenuMetaDataSeed()
                {
                    Icon = "lucide:area-chart",
                    Title = "分析页",
                    DisplayTitle = "Lion.AbpPro.DynamicMenuManagement:Analytics",
                    Order = 1,
                },
            },
            new MenuDataSeed()
            {
                Name = "Workspace",
                Path = "/workspace",
                Component = "/views/dashboard/workspace/index.vue",
                Meta = new MenuMetaDataSeed()
                {
                    Icon = "carbon:workspace",
                    Title = "工作台",
                    DisplayTitle = "Lion.AbpPro.DynamicMenuManagement:Workspace",
                    Order = 2,
                },
            },
        },
    };

    public static MenuDataSeed System = new()
    {
        Name = "system",
        Path = "/system",
        Component = "BasicLayout",
        Meta = new MenuMetaDataSeed()
        {
            Icon = "lucide:layout-dashboard",
            Title = "系统管理",
            DisplayTitle = "Lion.AbpPro.DynamicMenuManagement:System",
            Order = 2,
            Policy = "",
        },
        Children = new List<MenuDataSeed>()
        {
            new MenuDataSeed()
            {
                Name = "abpUser",
                Path = "user",
                Component = "/views/system/abpuser/index.vue",
                Meta = new MenuMetaDataSeed()
                {
                    Icon = "ph:user",
                    Title = "用户管理",
                    DisplayTitle = "Lion.AbpPro.DynamicMenuManagement:User",
                    Order = 1,
                    Policy = "AbpIdentity.Users",
                },
            },
            new MenuDataSeed()
            {
                Name = "abpRole",
                Path = "role",
                Component = "/views/system/abprole/index.vue",
                Meta = new MenuMetaDataSeed()
                {
                    Icon = "carbon:workspace",
                    Title = "角色管理",
                    DisplayTitle = "Lion.AbpPro.DynamicMenuManagement:Role",
                    Order = 2,
                    Policy = "AbpIdentity.Roles",
                },
            },
            new MenuDataSeed()
            {
                Name = "abpOrganizationUnit",
                Path = "organizationUnit",
                Component = "/views/system/abporganizationunit/index.vue",
                Meta = new MenuMetaDataSeed()
                {
                    Icon = "ant-design:team-outlined",
                    Title = "组织机构管理",
                    DisplayTitle = "Lion.AbpPro.DynamicMenuManagement:OrganizationUnit",
                    Order = 3,
                    Policy = "AbpIdentity.OrganizationUnitManagement",
                },
            },
            new MenuDataSeed()
            {
                Name = "abpSetting",
                Path = "setting",
                Component = "/views/system/abpsetting/index.vue",
                Meta = new MenuMetaDataSeed()
                {
                    Icon = "uil:setting",
                    Title = "设置管理",
                    DisplayTitle = "Lion.AbpPro.DynamicMenuManagement:Setting",
                    Order = 4,
                    Policy = "AbpIdentity.Setting",
                },
            },
            new MenuDataSeed()
            {
                Name = "abpfeature",
                Path = "feature",
                Component = "/views/system/abpfeature/index.vue",
                Meta = new MenuMetaDataSeed()
                {
                    Icon = "ant-design:tool-outlined",
                    Title = "功能管理",
                    DisplayTitle = "Lion.AbpPro.DynamicMenuManagement:Feature",
                    Order = 5,
                    Policy = "AbpIdentity.FeatureManagement",
                },
            },
            new MenuDataSeed()
            {
                Name = "abpAuditLog",
                Path = "auditlog",
                Component = "/views/system/abplog/audit.vue",
                Meta = new MenuMetaDataSeed()
                {
                    Icon = "ant-design:snippets-twotone",
                    Title = "审计日志",
                    DisplayTitle = "Lion.AbpPro.DynamicMenuManagement:AuditLog",
                    Order = 6,
                    Policy = "AbpIdentity.AuditLog",
                },
            },
            new MenuDataSeed()
            {
                Name = "abpLoginLog",
                Path = "loginlog",
                Component = "/views/system/abplog/login.vue",
                Meta = new MenuMetaDataSeed()
                {
                    Icon = "ant-design:snippets-twotone",
                    Title = "登录日志",
                    DisplayTitle = "Lion.AbpPro.DynamicMenuManagement:LoginLog",
                    Order = 7,
                    Policy = "AbpIdentity.IdentitySecurityLogs",
                },
            },
            new MenuDataSeed()
            {
                Name = "abpLanguage",
                Path = "language",
                Component = "/views/system/abplanguage/language.vue",
                Meta = new MenuMetaDataSeed()
                {
                    Icon = "ant-design:read-outlined",
                    Title = "语言管理",
                    DisplayTitle = "Lion.AbpPro.DynamicMenuManagement:Language",
                    Order = 8,
                    Policy = "AbpIdentity.Languages",
                },
            },
            new MenuDataSeed()
            {
                Name = "abpLanguageText",
                Path = "languagetext",
                Component = "/views/system/abplanguage/languagetext.vue",
                Meta = new MenuMetaDataSeed()
                {
                    Icon = "ant-design:font-size-outlined",
                    Title = "语言文本管理",
                    DisplayTitle = "Lion.AbpPro.DynamicMenuManagement:LanguageText",
                    Order = 9,
                    Policy = "AbpIdentity.LanguageTexts",
                },
            },
            new MenuDataSeed()
            {
                Name = "abpDataDictionary",
                Path = "dataDictionary",
                Component = "/views/system/abpdatadictionary/index.vue",
                Meta = new MenuMetaDataSeed()
                {
                    Icon = "ant-design:table-outlined",
                    Title = "数据字典管理",
                    DisplayTitle = "Lion.AbpPro.DynamicMenuManagement:DataDictionary",
                    Order = 10,
                    Policy = "AbpIdentity.DataDictionaryManagement",
                },
            },
            new MenuDataSeed()
            {
                Name = "abpNotification",
                Path = "notification",
                Component = "/views/system/abpnotification/notification.vue",
                Meta = new MenuMetaDataSeed()
                {
                    Icon = "ant-design:comment-outlined",
                    Title = "通告管理",
                    DisplayTitle = "Lion.AbpPro.DynamicMenuManagement:Notification",
                    Order = 11,
                    Policy = "AbpIdentity.NotificationSubscriptionManagement",
                },
            },
            new MenuDataSeed()
            {
                Name = "abpMessage",
                Path = "message",
                Component = "/views/system/abpnotification/message.vue",
                Meta = new MenuMetaDataSeed()
                {
                    Icon = "ant-design:customer-service-twotone",
                    Title = "消息管理",
                    DisplayTitle = "Lion.AbpPro.DynamicMenuManagement:Message",
                    Order = 12,
                    Policy = "AbpIdentity.NotificationManagement",
                },
            },
            new MenuDataSeed()
            {
                Name = "abpMenu",
                Path = "menu",
                Component = "/views/system/abpmenu/index.vue",
                Meta = new MenuMetaDataSeed()
                {
                    Icon = "ant-design:bars-outlined",
                    Title = "菜单管理",
                    DisplayTitle = "Lion.AbpPro.DynamicMenuManagement:Menu",
                    Order = 13,
                    Policy = "AbpIdentity.DynamicMenuManagement",
                },
            },
        },
    };

    public static MenuDataSeed Tenant = new()
    {
        Name = "tenant",
        Path = "/tenant",
        Component = "BasicLayout",
        Meta = new MenuMetaDataSeed()
        {
            Icon = "ant-design:switcher-filled",
            Title = "租户管理",
            DisplayTitle = "Lion.AbpPro.DynamicMenuManagement:Tenant",
            Order = 3,
            Policy = "",
        },
        Children = new List<MenuDataSeed>()
        {
            new MenuDataSeed()
            {
                Name = "abpTenant",
                Path = "tenant",
                Component = "/views/system/abptenant/index.vue",
                Meta = new MenuMetaDataSeed()
                {
                    Icon = "ph:user",
                    Title = "租户列表",
                    DisplayTitle = "Lion.AbpPro.DynamicMenuManagement:Tenant",
                    Order = 1,
                    Policy = "AbpTenantManagement.Tenants",
                },
            },
        },
    };

    public static MenuDataSeed File = new()
    {
        Name = "file",
        Path = "/file",
        Component = "BasicLayout",
        Meta = new MenuMetaDataSeed()
        {
            Icon = "ant-design:folder-open-outlined",
            Title = "文件管理",
            DisplayTitle = "Lion.AbpPro.DynamicMenuManagement:File",
            Order = 4,
            Policy = "",
        },
        Children = new List<MenuDataSeed>()
        {
            new MenuDataSeed()
            {
                Name = "abpFile",
                Path = "file",
                Component = "/views/system/abpfiles/index.vue",
                Meta = new MenuMetaDataSeed()
                {
                    Icon = "ant-design:file-text-twotone",
                    Title = "文件管理",
                    DisplayTitle = "Lion.AbpPro.DynamicMenuManagement:File",
                    Order = 1,
                    Policy = "FileManagement.File",
                },
            },
        },
    };
}

public class MenuDataSeed
{
    public string Name { get; set; }

    public string Path { get; set; }

    public string Component { get; set; }

    public MenuMetaDataSeed Meta { get; set; } = new();

    public List<MenuDataSeed> Children { get; set; } = new();
}

public class MenuMetaDataSeed
{
    public string Icon { get; set; }

    public string Title { get; set; }

    public string DisplayTitle { get; set; }

    public int Order { get; set; }

    public bool KeepAlive { get; set; }

    public bool HideInMenu { get; set; }

    public string Link { get; set; }
    public string Policy { get; set; }
}
