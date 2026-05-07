using System.ComponentModel;

namespace Lion.AbpPro.DynamicMenuManagement.Menus;

/// <summary>
/// 菜单类型
/// </summary>
public enum MenuType
{
    [Description("目录")]
    Folder = 10,

    [Description("菜单")]
    Menu = 20,
}
