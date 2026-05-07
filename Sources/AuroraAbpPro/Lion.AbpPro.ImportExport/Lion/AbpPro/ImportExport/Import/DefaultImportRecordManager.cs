namespace Lion.AbpPro.ImportExport.Import;

public class DefaultImportRecordManager : IImportRecordManager, ITransientDependency
{
    public async Task CreateAsync(
        Guid id,
        string contributor,
        string name,
        string blobId,
        string blobName,
        string remark
    )
    {
        await Task.CompletedTask;
    }

    public async Task<ImportRecord> FindAsync(Guid id)
    {
        await Task.CompletedTask;
        return null;
    }

    public async Task UpdateStatusAsync(
        Guid id,
        ImportStatus status,
        string blobErrorId = "",
        string blobErrorName = "",
        string remark = ""
    )
    {
        await Task.CompletedTask;
    }
}
