namespace Lion.AbpPro.TemplateManagement.TextTemplates;

/// <summary>
/// 分页查询模板
/// </summary>
public class PageTextTemplateOutput
{
    /// <summary>
    /// 模板Id
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 编码
    /// </summary>
    public string Code { get; set; }

    /// <summary>
    /// 内容
    /// </summary>
    public string Content { get; set; }

    /// <summary>
    /// 语言
    /// </summary>
    public string CultureName { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }
}
