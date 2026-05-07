using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Aurora.AbpPro.Tools.Models;

/// <summary>
/// 侧边栏菜单项模型：用于绑定页面路由
/// 继承 ObservableObject，便于 DisplayName 在语言切换后通知 UI 刷新
/// </summary>
public partial class MenuItemModel : ObservableObject
{
    /// <summary>
    /// 菜单唯一键，对应多语言资源键
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// 显示名称（运行时由 ILocalizationService 翻译，可观察）
    /// </summary>
    [ObservableProperty]
    private string _displayName = string.Empty;

    /// <summary>
    /// 图标 Geometry（HandyControl 内置图标资源 Key）
    /// </summary>
    public string IconKey { get; set; } = string.Empty;

    /// <summary>
    /// 关联的页面类型（运行时通过 DI 解析实例）
    /// </summary>
    public Type PageType { get; set; } = default!;
}
