using Microsoft.AspNetCore.Mvc;

namespace Lion.AbpPro.TemplateManagement.TextTemplates;

/// <summary>
/// 模板
/// </summary>
public interface ITextTemplateAppService : IApplicationService
{
    /// <summary>
    /// 分页查询模板
    /// </summary>
    Task<PagedResultDto<PageTextTemplateOutput>> PageAsync(PageTextTemplateInput input);

    /// <summary>
    /// 创建模板
    /// </summary>
    Task CreateAsync(CreateTextTemplateInput input);

    /// <summary>
    /// 编辑模板
    /// </summary>
    Task UpdateAsync(UpdateTextTemplateInput input);

    /// <summary>
    /// 删除模板
    /// </summary>
    Task DeleteAsync(DeleteTextTemplateInput input);

    /// <summary>
    /// 模板导出
    /// </summary>
    Task<ActionResult> ExportAsync(PageTextTemplateInput input);
}
