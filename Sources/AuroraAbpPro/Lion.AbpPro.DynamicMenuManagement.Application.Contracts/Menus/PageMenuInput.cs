using System;
using System.ComponentModel.DataAnnotations;
using Lion.AbpPro.Core;
using Lion.AbpPro.DynamicMenuManagement.Menus;

namespace Lion.AbpPro.DynamicMenuManagement.Menus;

/// <summary>
/// 分页查询菜单
/// </summary>
public class PageMenuInput : PagingBase
{
    /// <summary>
    /// 开始创建时间
    /// </summary>
    public DateTime? StartCreationTime { get; set; }

    /// <summary>
    /// 结束创建时间
    /// </summary>
    public DateTime? EndCreationTime { get; set; }
}
