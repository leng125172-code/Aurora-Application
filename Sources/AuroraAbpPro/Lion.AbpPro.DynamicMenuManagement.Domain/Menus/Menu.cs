using Lion.AbpPro.Core;
using Volo.Abp.Domain.Entities.Auditing;

namespace Lion.AbpPro.DynamicMenuManagement.Menus;

/// <summary>
/// 菜单
/// </summary>
public class Menu : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    private Menu() { }

    public Menu(
        Guid id,
        Guid? parentId,
        string name,
        string title,
        string displayTitle,
        string icon,
        bool keepAlive,
        bool hideInMenu,
        int order,
        string path,
        MenuType menuType,
        OpenType openType,
        string url,
        string component,
        bool enabled,
        string policy,
        Guid? tenantId = null
    )
        : base(id)
    {
        SetName(name);
        SetTitle(title);
        SetDisplayTitle(displayTitle);
        SetIcon(icon);
        SetKeepAlive(keepAlive);
        SetHideInMenu(hideInMenu);
        SetOrder(order);
        SetPath(path);
        SetMenuType(menuType);
        SetOpenType(openType);
        SetUrl(url);
        SetComponent(component);
        SetPolicy(policy);
        Enabled = enabled;
        TenantId = tenantId;
        ParentId = parentId;
    }

    public Guid? TenantId { get; private set; }

    public Guid? ParentId { get; set; }

    /// <summary>
    /// 唯一编码
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// 标题
    /// </summary>
    public string Title { get; private set; }

    /// <summary>
    /// 标题
    /// </summary>
    public string DisplayTitle { get; private set; }

    /// <summary>
    /// 图标
    /// </summary>
    public string Icon { get; private set; }

    /// <summary>
    /// 是否缓存
    /// </summary>
    public bool KeepAlive { get; private set; }

    /// <summary>
    /// 是否显示
    /// </summary>
    public bool HideInMenu { get; private set; }

    /// <summary>
    /// 排序
    /// </summary>
    public int Order { get; private set; }

    /// <summary>
    /// 路由/接口地址
    /// </summary>
    public string Path { get; private set; }

    /// <summary>
    /// 菜单类型
    /// </summary>
    public MenuType MenuType { get; private set; }

    /// <summary>
    /// 打开类型
    /// </summary>
    public OpenType OpenType { get; private set; }

    /// <summary>
    /// 内外链地址
    /// </summary>
    public string Url { get; private set; }

    /// <summary>
    /// 组件地址
    /// </summary>
    public string Component { get; private set; }

    /// <summary>
    /// 权限
    /// </summary>
    public string Policy { get; private set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; private set; }

    /// <summary>
    /// 设置唯一编码
    /// </summary>
    private void SetName(string name)
    {
        Guard.NotNullOrWhiteSpace(name, nameof(name), 128, 0);
        Name = name;
    }

    /// <summary>
    /// 设置标题
    /// </summary>
    private void SetTitle(string title)
    {
        Guard.NotNullOrWhiteSpace(title, nameof(title), 128, 0);
        Title = title;
    }

    private void SetDisplayTitle(string displayTitle)
    {
        DisplayTitle = displayTitle;
    }

    /// <summary>
    /// 设置图标
    /// </summary>
    private void SetIcon(string icon)
    {
        Guard.Length(icon, nameof(icon), 128, 0);
        Icon = icon;
    }

    /// <summary>
    /// 设置是否缓存
    /// </summary>
    private void SetKeepAlive(bool keepAlive)
    {
        KeepAlive = keepAlive;
    }

    /// <summary>
    /// 设置是否显示
    /// </summary>
    private void SetHideInMenu(bool hideInMenu)
    {
        HideInMenu = hideInMenu;
    }

    /// <summary>
    /// 设置排序
    /// </summary>
    private void SetOrder(int order)
    {
        Order = order;
    }

    /// <summary>
    /// 设置路由/接口地址
    /// </summary>
    private void SetPath(string path)
    {
        Guard.NotNullOrWhiteSpace(path, nameof(path), 512, 0);
        Path = path;
    }

    /// <summary>
    /// 设置菜单类型
    /// </summary>
    private void SetMenuType(MenuType menuType)
    {
        MenuType = menuType;
    }

    /// <summary>
    /// 设置打开类型
    /// </summary>
    private void SetOpenType(OpenType openType)
    {
        OpenType = openType;
    }

    /// <summary>
    /// 设置内外链地址
    /// </summary>
    private void SetUrl(string url)
    {
        Guard.Length(url, nameof(url), 512, 0);
        Url = url;
    }

    /// <summary>
    /// 设置组件地址
    /// </summary>
    private void SetComponent(string component)
    {
        Component = component;
    }

    private void SetPolicy(string policy)
    {
        Policy = policy.IsNullOrWhiteSpace() ? string.Empty : policy;
    }

    /// <summary>
    /// 更新菜单
    /// </summary>
    public void Update(
        string name,
        string title,
        string displayTitle,
        string icon,
        bool keepAlive,
        bool hideInMenu,
        int order,
        string path,
        MenuType menuType,
        OpenType openType,
        string url,
        string component,
        bool enabled,
        string policy,
        Guid? parentId
    )
    {
        SetName(name);
        SetTitle(title);
        SetDisplayTitle(displayTitle);
        SetIcon(icon);
        SetKeepAlive(keepAlive);
        SetHideInMenu(hideInMenu);
        SetOrder(order);
        SetPath(path);
        SetMenuType(menuType);
        SetOpenType(openType);
        SetUrl(url);
        SetComponent(component);
        Enabled = enabled;
        SetPolicy(policy);
        ParentId = parentId;
    }
}
