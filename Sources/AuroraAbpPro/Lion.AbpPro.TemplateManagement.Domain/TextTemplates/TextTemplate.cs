using Lion.AbpPro.Core;

namespace Lion.AbpPro.TemplateManagement.TextTemplates;

/// <summary>
/// 模板
/// </summary>
public class TextTemplate : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    private TextTemplate() { }

    public TextTemplate(
        Guid id,
        string name,
        string code,
        string content,
        string cultureName,
        Guid? tenantId = null
    )
        : base(id)
    {
        SetName(name);
        SetCode(code);
        SetContent(content);
        SetCultureName(cultureName);

        TenantId = tenantId;
    }

    public Guid? TenantId { get; private set; }

    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// 编码
    /// </summary>
    public string Code { get; private set; }

    /// <summary>
    /// 内容大苏打
    /// </summary>
    public string Content { get; private set; }

    /// <summary>
    /// 语言
    /// </summary>
    public string CultureName { get; private set; }

    /// <summary>
    /// 设置名称
    /// </summary>
    private void SetName(string name)
    {
        Guard.NotNullOrWhiteSpace(name, nameof(name), 128, 0);
        Name = name;
    }

    /// <summary>
    /// 设置编码
    /// </summary>
    private void SetCode(string code)
    {
        Guard.NotNullOrWhiteSpace(code, nameof(code), 128, 0);
        Code = code;
    }

    /// <summary>
    /// 设置内容大苏打
    /// </summary>
    private void SetContent(string content)
    {
        Guard.NotNullOrWhiteSpace(content, nameof(content), 1024, 0);
        Content = content;
    }

    /// <summary>
    /// 设置语言
    /// </summary>
    private void SetCultureName(string cultureName)
    {
        Guard.NotNullOrWhiteSpace(cultureName, nameof(cultureName), 128, 0);
        CultureName = cultureName;
    }

    /// <summary>
    /// 更新模板
    /// </summary>
    public void Update(string name, string code, string content, string cultureName)
    {
        SetName(name);
        SetCode(code);
        SetContent(content);
        SetCultureName(cultureName);
    }
}
