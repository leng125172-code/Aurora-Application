using System;
using System.ComponentModel.DataAnnotations;
using Lion.AbpPro.DynamicMenuManagement.Menus;

namespace Lion.AbpPro.DynamicMenuManagement.Menus;

/// <summary>
/// 更新菜单
/// </summary>
public class UpdateMenuInput
{
    /// <summary>
    /// 菜单Id
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 唯一编码
    /// </summary>
    [Required(ErrorMessage = "唯一编码不能为空")]
    public string Name { get; set; }

    /// <summary>
    /// 标题
    /// </summary>
    [Required(ErrorMessage = "标题不能为空")]
    public string Title { get; set; }

    /// <summary>
    /// 标题
    /// </summary>
    public string DisplayTitle { get; set; }

    /// <summary>
    /// 图标
    /// </summary>
    public string Icon { get; set; }

    /// <summary>
    /// 是否缓存
    /// </summary>
    [Required(ErrorMessage = "是否缓存不能为空")]
    public bool KeepAlive { get; set; }

    /// <summary>
    /// 是否显示
    /// </summary>
    [Required(ErrorMessage = "是否显示不能为空")]
    public bool HideInMenu { get; set; }

    /// <summary>
    /// 排序
    /// </summary>
    [Required(ErrorMessage = "排序不能为空")]
    public int Order { get; set; }

    /// <summary>
    /// 路由地址
    /// </summary>
    [Required(ErrorMessage = "路由地址不能为空")]
    public string Path { get; set; }

    /// <summary>
    /// 菜单类型
    /// </summary>
    public MenuType MenuType { get; set; }

    /// <summary>
    /// 打开类型
    /// </summary>
    public OpenType OpenType { get; set; }

    /// <summary>
    /// 内外链地址
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// 组件地址
    /// </summary>
    public string? Component { get; set; }

    public bool Enabled { get; set; }

    /// <summary>
    /// 权限
    /// </summary>
    public string Policy { get; set; }

    public Guid? ParentId { get; set; }
}
