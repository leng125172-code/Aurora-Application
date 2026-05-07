using Lion.AbpPro.Core;
using Volo.Abp.Content;

namespace Lion.AbpPro.ImportExportManagement.Import;

/// <summary>
/// 导入记录
/// </summary>
public interface IImportRecordAppService : IApplicationService
{
    /// <summary>
    /// 分页查询导入记录
    /// </summary>
    Task<PagedResultDto<PageImportRecordOutput>> PageAsync(PageImportRecordInput input);

    /// <summary>
    /// 获取导入excel贡献者
    /// </summary>
    Task<List<GetImportExcelContributorOutput>> GetImportExcelContributorAsync();

    /// <summary>
    /// 获取导入类型
    /// </summary>
    Task<List<FromSelector<string, string>>> GetFromSelectorAsync();

    /// <summary>
    /// 导入excel
    /// </summary>
    Task ImportExcelAsync(ImportExcelInput input);

    /// <summary>
    /// 获取下载模板
    /// </summary>
    Task<RemoteStreamContent> DownloadTemplateAsync(DownloadTemplateInput input);
}
