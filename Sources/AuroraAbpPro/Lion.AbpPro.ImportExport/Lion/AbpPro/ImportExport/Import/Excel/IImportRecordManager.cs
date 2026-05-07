namespace Lion.AbpPro.ImportExport.Import.Excel;

public interface IImportRecordManager
{
    Task CreateAsync(
        Guid id,
        string contributor,
        string name,
        string blobId,
        string blobName,
        string remark
    );

    /// <summary>
    /// 获取导入记录
    /// </summary>
    Task<ImportRecord> FindAsync(Guid id);

    /// <summary>
    /// 获取导入记录
    /// </summary>
    Task UpdateStatusAsync(
        Guid id,
        ImportStatus status,
        string blobErrorId = "",
        string blobErrorName = "",
        string remark = ""
    );
}
