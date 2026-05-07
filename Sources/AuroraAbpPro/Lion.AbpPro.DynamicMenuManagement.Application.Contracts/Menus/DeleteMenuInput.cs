using System;
using System.ComponentModel.DataAnnotations;

namespace Lion.AbpPro.DynamicMenuManagement.Menus;

/// <summary>
/// 删除菜单
/// </summary>
public class DeleteMenuInput
{
    /// <summary>
    /// 菜单Id
    /// </summary>
    public Guid Id { get; set; }
}
