using Lion.AbpPro.ImportExport.Import;
using Lion.AbpPro.ImportExportManagement.Import;

namespace Lion.AbpPro.ImportExportManagement.EntityFrameworkCore.Import;

/// <summary>
/// 导入记录 仓储Ef core 实现
/// </summary>
public class EfCoreImportRecordRepository
    : EfCoreRepository<ImportExportManagementDbContext, ImportRecord, Guid>,
        IImportRecordRepository
{
    public EfCoreImportRecordRepository(
        IDbContextProvider<ImportExportManagementDbContext> dbContextProvider
    )
        : base(dbContextProvider) { }

    public async Task<List<ImportRecord>> GetListAsync(
        string name,
        string blobName,
        ImportStatus? status,
        DateTime? startDateTime = null,
        DateTime? endDateTime = null,
        int maxResultCount = 10,
        int skipCount = 0
    )
    {
        return await (await GetDbSetAsync())
            .WhereIf(name.IsNotNullOrWhiteSpace(), e => e.Name.Contains(name))
            .WhereIf(blobName.IsNotNullOrWhiteSpace(), e => e.BlobName.Contains(blobName))
            .WhereIf(status.HasValue, e => e.Status == status)
            .WhereIf(startDateTime.HasValue, e => e.CreationTime >= startDateTime.Value)
            .WhereIf(endDateTime.HasValue, e => e.CreationTime <= endDateTime.Value)
            .OrderByDescending(e => e.CreationTime)
            .PageBy(skipCount, maxResultCount)
            .ToListAsync();
    }

    public async Task<long> GetCountAsync(
        string name,
        string blobName,
        ImportStatus? status,
        DateTime? startDateTime = null,
        DateTime? endDateTime = null
    )
    {
        return await (await GetDbSetAsync())
            .WhereIf(name.IsNotNullOrWhiteSpace(), e => e.Name.Contains(name))
            .WhereIf(blobName.IsNotNullOrWhiteSpace(), e => e.BlobName.Contains(blobName))
            .WhereIf(status.HasValue, e => e.Status == status)
            .WhereIf(startDateTime.HasValue, e => e.CreationTime >= startDateTime.Value)
            .WhereIf(endDateTime.HasValue, e => e.CreationTime <= endDateTime.Value)
            .WhereIf(startDateTime.HasValue, e => e.CreationTime >= startDateTime.Value)
            .WhereIf(endDateTime.HasValue, e => e.CreationTime <= endDateTime.Value)
            .CountAsync();
    }
}
