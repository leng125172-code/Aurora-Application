namespace Lion.AbpPro.TemplateManagement.TextTemplates;

/// <summary>
/// 模板
/// </summary>
[Authorize(TemplateManagementPermissions.TemplateManagement.Default)]
public class TextTemplateAppService : ApplicationService, ITextTemplateAppService
{
    private readonly TextTemplateManager _textTemplateManager;
    private readonly IExcelExporter _excelExporter;

    public TextTemplateAppService(
        TextTemplateManager textTemplateManager,
        IExcelExporter excelExporter
    )
    {
        _textTemplateManager = textTemplateManager;
        _excelExporter = excelExporter;
    }

    /// <summary>
    /// 分页查询模板
    /// </summary>
    public async Task<PagedResultDto<PageTextTemplateOutput>> PageAsync(PageTextTemplateInput input)
    {
        var result = new PagedResultDto<PageTextTemplateOutput>();
        var totalCount = await _textTemplateManager.GetCountAsync(
            input.Code,
            input.Name,
            input.Content,
            input.StartCreationTime,
            input.EndCreationTime
        );
        result.TotalCount = totalCount;
        if (totalCount <= 0)
            return result;
        var list = await _textTemplateManager.GetListAsync(
            input.Code,
            input.Name,
            input.Content,
            input.StartCreationTime,
            input.EndCreationTime,
            input.PageSize,
            input.SkipCount
        );
        result.Items = list.Adapt<List<PageTextTemplateOutput>>();
        return result;
    }

    /// <summary>
    /// 创建模板
    /// </summary>
    [Authorize(TemplateManagementPermissions.TemplateManagement.Create)]
    public Task CreateAsync(CreateTextTemplateInput input)
    {
        return _textTemplateManager.CreateAsync(
            GuidGenerator.Create(),
            input.Name,
            input.Code,
            input.Content,
            input.CultureName
        );
    }

    /// <summary>
    /// 编辑模板
    /// </summary>
    [Authorize(TemplateManagementPermissions.TemplateManagement.Update)]
    public Task UpdateAsync(UpdateTextTemplateInput input)
    {
        return _textTemplateManager.UpdateAsync(
            input.Id,
            input.Name,
            input.Code,
            input.Content,
            input.CultureName
        );
    }

    /// <summary>
    /// 删除模板
    /// </summary>
    [Authorize(TemplateManagementPermissions.TemplateManagement.Delete)]
    public Task DeleteAsync(DeleteTextTemplateInput input)
    {
        return _textTemplateManager.DeleteAsync(input.Id);
    }

    /// <summary>
    /// 模板导出
    /// </summary>
    [Authorize(TemplateManagementPermissions.TemplateManagement.Export)]
    public async Task<ActionResult> ExportAsync(PageTextTemplateInput input)
    {
        var list = await _textTemplateManager.GetListAsync(
            startDateTime: input.StartCreationTime,
            endDateTime: input.EndCreationTime,
            skipCount: 0,
            maxResultCount: Int32.MaxValue
        );
        var result = list.Adapt<List<PageTextTemplateOutput>>();
        var bytes = await _excelExporter.ExportAsByteArray<PageTextTemplateOutput>(result);
        return new XlsxFileResult(
            bytes: bytes,
            fileDownloadName: $"模板导出列表{Clock.Now:yyyyMMdd}"
        );
    }
}
