namespace Lion.AbpPro.ImportExport.Import;

/// <summary>
/// 导入记录
/// </summary>
public class ImportRecord : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    private ImportRecord() { }

    public ImportRecord(
        Guid id,
        string contributor,
        string name,
        string blobId,
        string blobName,
        string cultureName,
        string remark,
        ImportStatus status,
        Guid? tenantId = null
    )
        : base(id)
    {
        SetContributor(contributor);
        SetName(name);
        SetBlobId(blobId);
        SetBlobName(blobName);
        SetCultureName(cultureName);
        SetRemark(remark);
        SetStatus(status);
        SetBlobErrorIdAndBlobErrorName(string.Empty, string.Empty);
        TenantId = tenantId;
    }

    public Guid? TenantId { get; private set; }

    /// <summary>
    /// 导入贡献者
    /// </summary>
    public string Contributor { get; private set; }

    /// <summary>
    /// 导入名称
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// 文件Id
    /// </summary>
    public string BlobId { get; private set; }

    /// <summary>
    /// 文件名称
    /// </summary>
    public string BlobName { get; private set; }

    /// <summary>
    /// 导入失败文件Id
    /// </summary>
    public string BlobErrorId { get; private set; }

    /// <summary>
    /// 导入文件名称
    /// </summary>
    public string BlobErrorName { get; private set; }

    /// <summary>
    /// 多语言
    /// </summary>
    public string CultureName { get; private set; }

    /// <summary>
    /// 备注
    /// </summary>
    public string Remark { get; private set; }

    /// <summary>
    /// 状态
    /// </summary>
    public ImportStatus Status { get; private set; }

    /// <summary>
    /// 设置导入贡献者
    /// </summary>
    private void SetContributor(string contributor)
    {
        Guard.NotNullOrWhiteSpace(contributor, nameof(contributor), 128, 0);
        Contributor = contributor;
    }

    /// <summary>
    /// 设置导入名称
    /// </summary>
    private void SetName(string name)
    {
        Guard.NotNullOrWhiteSpace(name, nameof(name), 128, 0);
        Name = name;
    }

    /// <summary>
    /// 设置文件名称
    /// </summary>
    public void SetBlobErrorIdAndBlobErrorName(string blobErrorId, string blobErrorName)
    {
        BlobErrorId = blobErrorId.IsNotNullOrWhiteSpace() ? blobErrorId : string.Empty;
        BlobErrorName = blobErrorName.IsNotNullOrWhiteSpace() ? blobErrorName : string.Empty;
        if (BlobErrorId.IsNotNullOrWhiteSpace() && BlobErrorName.IsNotNullOrWhiteSpace())
        {
            AddLocalEvent(
                new ImportExcelFailedEto()
                {
                    BlobId = BlobId,
                    BlobErrorId = BlobErrorId,
                    BlobErrorName = BlobErrorName,
                }
            );
        }
    }

    /// <summary>
    /// 设置文件名称
    /// </summary>
    private void SetBlobId(string blobId)
    {
        BlobId = blobId.IsNotNullOrWhiteSpace() ? blobId : string.Empty;
    }

    /// <summary>
    /// 设置文件名称
    /// </summary>
    private void SetBlobName(string blobName)
    {
        Guard.NotNullOrWhiteSpace(blobName, nameof(blobName), 256, 0);
        BlobName = blobName;
    }

    /// <summary>
    /// 设置多语言
    /// </summary>
    private void SetCultureName(string cultureName)
    {
        Guard.NotNullOrWhiteSpace(cultureName, nameof(cultureName), 36, 0);
        CultureName = cultureName;
    }

    /// <summary>
    /// 设置备注
    /// </summary>
    public void SetRemark(string remark)
    {
        Remark = remark.IsNotNullOrWhiteSpace()
            ? (remark.Length > 2048 ? remark.Substring(0, 2000) : remark)
            : string.Empty;
    }

    /// <summary>
    /// 设置状态
    /// </summary>
    public void SetStatus(ImportStatus status)
    {
        Status = status;
    }

    /// <summary>
    /// 更新导入记录
    /// </summary>
    public void Update(
        string contributor,
        string name,
        string blobName,
        string cultureName,
        string remark,
        ImportStatus status
    )
    {
        SetContributor(contributor);
        SetName(name);
        SetBlobName(blobName);
        SetCultureName(cultureName);
        SetRemark(remark);
        SetStatus(status);
    }
}
