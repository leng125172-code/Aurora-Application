using Lion.AbpPro.ImportExport.Import;

namespace Lion.AbpPro.ImportExportManagement.Import;

/// <summary>
/// 导入记录
/// </summary>
public class ImportRecordDto
{
    /// <summary>
    /// 主键Id
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreationTime { get; set; }

    /// <summary>
    /// 导入贡献者
    /// </summary>
    public string Contributor { get; set; }

    /// <summary>
    /// 导入名称
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 文件id
    /// </summary>
    public Guid BlobId { get; set; }

    /// <summary>
    /// 文件名称
    /// </summary>
    public string BlobName { get; set; }

    /// <summary>
    /// 导入失败文件Id
    /// </summary>
    public string BlobErrorId { get; set; }

    /// <summary>
    /// 导入文件名称
    /// </summary>
    public string BlobErrorName { get; set; }

    /// <summary>
    /// 多语言
    /// </summary>
    public string CultureName { get; set; }

    /// <summary>
    /// 备注
    /// </summary>
    public string Remark { get; set; }

    /// <summary>
    /// 状态
    /// </summary>
    public ImportStatus Status { get; set; }

    private const string CacheKeyFormat = "i:{0}";

    public static string CalculateCacheKey(Guid id)
    {
        return string.Format(CacheKeyFormat, id);
    }
}
