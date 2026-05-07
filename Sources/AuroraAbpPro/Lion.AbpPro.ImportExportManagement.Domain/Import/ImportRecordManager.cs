using System.Globalization;
using Lion.AbpPro.ImportExport;
using Lion.AbpPro.ImportExport.Import;
using Lion.AbpPro.ImportExport.Import.Eto;
using Lion.AbpPro.ImportExport.Import.Excel;
using Mapster;
using Microsoft.Extensions.Options;
using Volo.Abp.Users;

namespace Lion.AbpPro.ImportExportManagement.Import;

[Dependency(ReplaceServices = true)]
public class ImportRecordManager : ImportExportManagementDomainService, IImportRecordManager
{
    private readonly IImportRecordRepository _importRecordRepository;
    private readonly ImportExcelContributorOptions _importExcelContributorOptions;
    private readonly IDistributedEventBus _distributedEventBus;

    public ImportRecordManager(
        IImportRecordRepository importRecordRepository,
        IOptions<ImportExcelContributorOptions> importExcelContributorOptions,
        IDistributedEventBus distributedEventBus
    )
    {
        _importRecordRepository = importRecordRepository;
        _distributedEventBus = distributedEventBus;
        _importExcelContributorOptions = importExcelContributorOptions.Value;
    }

    public async Task<List<ImportRecordDto>> GetListAsync(
        string name,
        string blobName,
        ImportStatus? status,
        DateTime? startDateTime = null,
        DateTime? endDateTime = null,
        int maxResultCount = 10,
        int skipCount = 0
    )
    {
        var list = await _importRecordRepository.GetListAsync(
            name,
            blobName,
            status,
            startDateTime,
            endDateTime,
            maxResultCount,
            skipCount
        );
        return list.Adapt<List<ImportRecordDto>>();
    }

    public async Task<long> GetCountAsync(
        string name,
        string blobName,
        ImportStatus? status,
        DateTime? startDateTime = null,
        DateTime? endDateTime = null
    )
    {
        return await _importRecordRepository.GetCountAsync(
            name,
            blobName,
            status,
            startDateTime,
            endDateTime
        );
    }

    /// <summary>
    /// 创建导入记录
    /// </summary>
    public async Task CreateAsync(
        Guid id,
        string contributor,
        string name,
        string blobId,
        string blobName,
        string remark
    )
    {
        var cultureName = CultureInfo.CurrentUICulture.TextInfo.CultureName;
        var entity = new ImportRecord(
            id,
            contributor,
            name,
            blobId,
            blobName,
            cultureName,
            remark,
            ImportStatus.Running,
            CurrentTenant.Id
        );
        await _importRecordRepository.InsertAsync(entity);
        await _distributedEventBus.PublishAsync(
            new StartImportRecordEto()
            {
                Id = entity.Id,
                Name = entity.Name,
                Contributor = entity.Contributor,
                BlobId = entity.BlobId,
                BlobName = entity.BlobName,
                Remark = entity.Remark,
                TenantId = CurrentTenant.Id,
            }
        );
    }

    /// <summary>
    /// 删除导入记录
    /// </summary>
    public async Task DeleteAsync(Guid id)
    {
        var entity = await _importRecordRepository.FindAsync(id);
        if (entity == null)
            throw new UserFriendlyException($"导入记录不存在");
        await _importRecordRepository.DeleteAsync(entity);
    }

    /// <summary>
    /// 获取导入记录
    /// </summary>
    public async Task<ImportRecord> GetAsync(Guid id)
    {
        var entity = await _importRecordRepository.FindAsync(id);
        if (entity == null)
            throw new UserFriendlyException($"导入记录不存在");
        return entity;
    }

    /// <summary>
    /// 获取导入记录
    /// </summary>
    public async Task<ImportRecord> FindAsync(Guid id)
    {
        return await _importRecordRepository.FindAsync(id);
    }

    /// <summary>
    /// 获取导入记录
    /// </summary>
    public async Task UpdateStatusAsync(
        Guid id,
        ImportStatus status,
        string blobErrorId = "",
        string blobErrorName = "",
        string remark = ""
    )
    {
        var entity = await _importRecordRepository.FindAsync(id);
        if (entity == null)
            throw new UserFriendlyException($"导入记录不存在");
        entity.SetStatus(status);
        entity.SetRemark(remark);
        if (blobErrorId.IsNotNullOrWhiteSpace() && blobErrorName.IsNotNullOrWhiteSpace())
        {
            entity.SetBlobErrorIdAndBlobErrorName(blobErrorId, blobErrorName);
        }

        await _importRecordRepository.UpdateAsync(entity);
    }
}
