using System;
using Lion.AbpPro.DynamicMenuManagement.Menus;

namespace Lion.AbpPro.DynamicMenuManagement.Menus;

/// <summary>
/// 分页查询菜单
/// </summary>
public class PageMenuOutput
{
    /// <summary>
    /// 菜单Id
    /// </summary>
    public Guid Id { get; set; }

    public Guid? ParentId { get; set; }

    /// <summary>
    /// 唯一编码
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 标题
    /// </summary>
    public string LocalizationTitle { get; set; }

    /// <summary>
    /// 标题
    /// </summary>
    public string DisplayTitle { get; set; }

    /// <summary>
    /// 标题
    /// </summary>
    public string Title { get; set; }

    /// <summary>
    /// 图标
    /// </summary>
    public string Icon { get; set; }

    /// <summary>
    /// 是否缓存
    /// </summary>
    public bool KeepAlive { get; set; }

    /// <summary>
    /// 是否显示
    /// </summary>
    public bool HideInMenu { get; set; }

    /// <summary>
    /// 排序
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// 路由/接口地址
    /// </summary>
    public string Path { get; set; }

    /// <summary>
    /// 菜单类型
    /// </summary>
    public MenuType MenuType { get; set; }

    public string MenuTypeDescription => MenuType.ToDescription();

    /// <summary>
    /// 打开类型
    /// </summary>
    public OpenType OpenType { get; set; }

    public string OpenTypeDescription => OpenType.ToDescription();

    /// <summary>
    /// 内外链地址
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// 组件地址
    /// </summary>
    public string? Component { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 权限
    /// </summary>
    public string Policy { get; set; }
}
