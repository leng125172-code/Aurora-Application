using Lion.AbpPro.ImportExport.Import;

namespace Lion.AbpPro.ImportExportManagement.Import;

public interface IImportRecordRepository : IBasicRepository<ImportRecord, Guid>
{
    Task<List<ImportRecord>> GetListAsync(
        string name,
        string blobName,
        ImportStatus? status,
        DateTime? startDateTime = null,
        DateTime? endDateTime = null,
        int maxResultCount = 10,
        int skipCount = 0
    );

    Task<long> GetCountAsync(
        string name,
        string blobName,
        ImportStatus? status,
        DateTime? startDateTime = null,
        DateTime? endDateTime = null
    );
}
