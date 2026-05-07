using System.Net;
using Lion.AbpPro.TemplateManagement.TextTemplates;
using Volo.Abp.AspNetCore.Mvc;

namespace Lion.AbpPro.TemplateManagement;

[Route("TextTemplates")]
public class TextTemplateController : AbpController, ITextTemplateAppService
{
    private readonly ITextTemplateAppService _textTemplateAppService;

    public TextTemplateController(ITextTemplateAppService textTemplateAppService)
    {
        _textTemplateAppService = textTemplateAppService;
    }

    [HttpPost("Page")]
    [SwaggerOperation(summary: "分页查询模板", Tags = new[] { "TextTemplates" })]
    public async Task<PagedResultDto<PageTextTemplateOutput>> PageAsync(PageTextTemplateInput input)
    {
        return await _textTemplateAppService.PageAsync(input);
    }

    [HttpPost("Create")]
    [SwaggerOperation(summary: "创建模板", Tags = new[] { "TextTemplates" })]
    public async Task CreateAsync(CreateTextTemplateInput input)
    {
        await _textTemplateAppService.CreateAsync(input);
    }

    [HttpPost("Update")]
    [SwaggerOperation(summary: "编辑模板", Tags = new[] { "TextTemplates" })]
    public async Task UpdateAsync(UpdateTextTemplateInput input)
    {
        await _textTemplateAppService.UpdateAsync(input);
    }

    [HttpPost("Delete")]
    [SwaggerOperation(summary: "删除模板", Tags = new[] { "TextTemplates" })]
    public async Task DeleteAsync(DeleteTextTemplateInput input)
    {
        await _textTemplateAppService.DeleteAsync(input);
    }

    [HttpPost("Export")]
    [SwaggerOperation(summary: "导出模板列表", Tags = new[] { "TextTemplates" })]
    [ProducesResponseType(typeof(FileContentResult), (int)HttpStatusCode.OK)]
    public Task<ActionResult> ExportAsync(PageTextTemplateInput input)
    {
        return _textTemplateAppService.ExportAsync(input);
    }
}
