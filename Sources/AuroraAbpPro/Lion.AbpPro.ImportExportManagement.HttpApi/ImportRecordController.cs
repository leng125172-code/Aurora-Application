using Lion.AbpPro.Core;
using Lion.AbpPro.ImportExportManagement.Import;
using Volo.Abp.Content;

namespace Lion.AbpPro.ImportExportManagement;

[Route("ImportRecords")]
public class ImportRecordController : ImportExportManagementController, IImportRecordAppService
{
    private readonly IImportRecordAppService _importRecordAppService;

    public ImportRecordController(IImportRecordAppService importRecordAppService)
    {
        _importRecordAppService = importRecordAppService;
    }

    [HttpPost("Page")]
    [SwaggerOperation(summary: "分页查询导入记录", Tags = new[] { "ImportRecords" })]
    public async Task<PagedResultDto<PageImportRecordOutput>> PageAsync(PageImportRecordInput input)
    {
        return await _importRecordAppService.PageAsync(input);
    }

    [HttpPost("ImportExcel/Contributor")]
    [SwaggerOperation(summary: "获取导入excel贡献者", Tags = new[] { "ImportRecords" })]
    public async Task<List<GetImportExcelContributorOutput>> GetImportExcelContributorAsync()
    {
        return await _importRecordAppService.GetImportExcelContributorAsync();
    }

    [HttpPost("Get")]
    [SwaggerOperation(summary: "获取导入类型", Tags = new[] { "ImportRecords" })]
    public Task<List<FromSelector<string, string>>> GetFromSelectorAsync()
    {
        return _importRecordAppService.GetFromSelectorAsync();
    }

    [HttpPut("Import/Excel")]
    [SwaggerOperation(summary: "导入Excel", Tags = new[] { "ImportRecords" })]
    public Task ImportExcelAsync([FromForm] ImportExcelInput input)
    {
        return _importRecordAppService.ImportExcelAsync(input);
    }

    [HttpPut("Download/Excel/Template")]
    [SwaggerOperation(summary: "下载导入模板", Tags = new[] { "ImportRecords" })]
    public async Task<RemoteStreamContent> DownloadTemplateAsync(DownloadTemplateInput input)
    {
        return await _importRecordAppService.DownloadTemplateAsync(input);
    }
}
