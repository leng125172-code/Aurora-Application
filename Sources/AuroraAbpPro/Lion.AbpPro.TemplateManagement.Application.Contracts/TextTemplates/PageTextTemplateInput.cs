using Lion.AbpPro.Core;

namespace Lion.AbpPro.TemplateManagement.TextTemplates;

/// <summary>
/// 分页查询模板
/// </summary>
public class PageTextTemplateInput : PagingBase
{
    /// <summary>
    /// 名称
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// 编码
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    /// 内容
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// 开始创建时间
    /// </summary>
    public DateTime? StartCreationTime { get; set; }

    /// <summary>
    /// 结束创建时间
    /// </summary>
    public DateTime? EndCreationTime { get; set; }
}
