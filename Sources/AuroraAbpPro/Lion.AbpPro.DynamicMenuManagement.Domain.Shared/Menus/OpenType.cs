using System.ComponentModel;

namespace Lion.AbpPro.DynamicMenuManagement.Menus;

/// <summary>
/// 打开类型
/// </summary>
public enum OpenType
{
    [Description("无")]
    Default = 10,

    [Description("组件")]
    Component = 20,

    [Description("内链")]
    InternalLink = 30,

    [Description("外链")]
    ExternalLink = 40,
}
