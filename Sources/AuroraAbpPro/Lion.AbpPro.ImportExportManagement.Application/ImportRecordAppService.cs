using Lion.AbpPro.Core;
using Lion.AbpPro.FileManagement.Files;
using Lion.AbpPro.ImportExport.Import;
using Lion.AbpPro.ImportExport.Import.Excel;
using Lion.AbpPro.ImportExportManagement.Import;
using Mapster;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Content;

namespace Lion.AbpPro.ImportExportManagement;

/// <summary>
/// 导入记录
/// </summary>
[Authorize(policy: ImportExportManagementPermissions.ImportExportManagement.Default)]
public class ImportRecordAppService : ApplicationService, IImportRecordAppService
{
    private readonly ImportRecordManager _importRecordManager;
    private readonly ImportExcelContributorOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly FileManager _fileManager;

    public ImportRecordAppService(
        ImportRecordManager importRecordManager,
        IOptions<ImportExcelContributorOptions> options,
        IServiceProvider serviceProvider,
        FileManager fileManager
    )
    {
        _importRecordManager = importRecordManager;
        _serviceProvider = serviceProvider;
        _fileManager = fileManager;
        _options = options.Value;
    }

    /// <summary>
    /// 分页查询导入记录
    /// </summary>
    public async Task<PagedResultDto<PageImportRecordOutput>> PageAsync(PageImportRecordInput input)
    {
        var result = new PagedResultDto<PageImportRecordOutput>();
        var totalCount = await _importRecordManager.GetCountAsync(
            input.Name,
            input.BlobName,
            input.Status,
            input.StartCreationTime,
            input.EndCreationTime
        );
        result.TotalCount = totalCount;
        if (totalCount <= 0)
            return result;
        var list = await _importRecordManager.GetListAsync(
            input.Name,
            input.BlobName,
            input.Status,
            input.StartCreationTime,
            input.EndCreationTime,
            input.PageSize,
            input.SkipCount
        );
        result.Items = list.Adapt<List<PageImportRecordOutput>>();
        return result;
    }

    [Authorize(policy: ImportExportManagementPermissions.ImportExportManagement.Create)]
    public async Task ImportExcelAsync(ImportExcelInput input)
    {
        _options.Contributors.TryGetValue(input.ContributorName, out var contributor);
        if (contributor == null)
        {
            throw new UserFriendlyException("导入贡献者不存在");
        }

        var excelContributor = (IImportExcelContributor)
            _serviceProvider.GetRequiredService(contributor);
        // 获取文件的二进制数据
        using var memoryStream = new MemoryStream();
        await input.File.CopyToAsync(memoryStream);
        var fileBytes = memoryStream.ToArray();
        var id = GuidGenerator.Create();
        await _fileManager.CreateAsync(
            id,
            input.File.FileName,
            input.File.Length,
            input.File.ContentType,
            fileBytes
        );
        await _importRecordManager.CreateAsync(
            GuidGenerator.Create(),
            input.ContributorName,
            excelContributor.Name,
            id.ToString(),
            input.File.FileName,
            input.Remark
        );
    }

    /// <summary>
    /// 获取导入excel贡献着
    /// </summary>
    public async Task<List<GetImportExcelContributorOutput>> GetImportExcelContributorAsync()
    {
        var result = new List<GetImportExcelContributorOutput>();
        foreach (var item in _options.Contributors)
        {
            var contributor = (IImportExcelContributor)
                _serviceProvider.GetRequiredService(item.Value);
            result.Add(
                new GetImportExcelContributorOutput
                {
                    Name = contributor.Name,
                    Contributor = item.Key,
                }
            );
        }

        await Task.CompletedTask;
        return result;
    }

    public Task<List<FromSelector<string, string>>> GetFromSelectorAsync()
    {
        var result = new List<FromSelector<string, string>>();
        foreach (var item in _options.Contributors)
        {
            result.Add(new FromSelector<string, string>(item.Key, item.Key));
        }

        return Task.FromResult(result);
    }

    public async Task<RemoteStreamContent> DownloadTemplateAsync(DownloadTemplateInput input)
    {
        _options.Contributors.TryGetValue(input.Contributor, out var contributor);
        if (contributor == null)
        {
            throw new UserFriendlyException("导入贡献者不存在");
        }

        var import = (IImportExcelContributor)_serviceProvider.GetRequiredService(contributor);
        var template = await import.GetTemplateAsync();
        var memoryStream = new MemoryStream(template.TemplateBytes);
        return new RemoteStreamContent(
            memoryStream,
            $"{import.Name}.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        );
    }
}
