namespace Lion.AbpPro.DynamicMenuManagement.Menus;

public class GetMenuTreeOutput
{
    public Guid Id { get; set; }

    public Guid? ParentId { get; set; }

    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; set; }

    public string Path { get; set; }

    public string? Component { get; set; }

    public bool Enabled { get; set; }

    public GetMenuTreeMetaOutput Meta { get; set; } = new();

    public List<GetMenuTreeOutput> Children { get; set; } = new();
}

public class GetMenuTreeMetaOutput
{
    public string Title { get; set; }

    public string DisplayTitle { get; set; }

    /// <summary>
    /// 图标
    /// </summary>
    public string Icon { get; set; }

    /// <summary>
    /// 排序
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// 是否缓存
    /// </summary>
    public bool KeepAlive { get; set; }

    /// <summary>
    /// 是否显示
    /// </summary>
    public bool HideInMenu { get; set; }

    /// <summary>
    /// 外链地址
    /// </summary>
    public string Link { get; set; }

    /// <summary>
    /// 内链地址
    /// </summary>
    public string IframeSrc { get; set; }
}
