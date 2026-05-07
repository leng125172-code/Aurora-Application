using System.ComponentModel.DataAnnotations;

namespace Lion.AbpPro.TemplateManagement.TextTemplates;

/// <summary>
/// 更新模板
/// </summary>
public class UpdateTextTemplateInput
{
    /// <summary>
    /// 模板Id
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 名称
    /// </summary>
    [Required(ErrorMessage = "名称不能为空")]
    public string Name { get; set; }

    /// <summary>
    /// 编码
    /// </summary>
    [Required(ErrorMessage = "编码不能为空")]
    public string Code { get; set; }

    /// <summary>
    /// 内容
    /// </summary>
    [Required(ErrorMessage = "内容不能为空")]
    public string Content { get; set; }

    /// <summary>
    /// 语言
    /// </summary>
    [Required(ErrorMessage = "语言不能为空")]
    public string CultureName { get; set; }
}
