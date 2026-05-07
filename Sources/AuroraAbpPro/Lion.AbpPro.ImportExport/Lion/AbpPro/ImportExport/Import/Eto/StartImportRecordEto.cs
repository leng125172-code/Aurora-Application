namespace Lion.AbpPro.ImportExport.Import.Eto;

public class StartImportRecordEto
{
    public Guid Id { get; set; }

    /// <summary>
    /// 导入名称
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 导入贡献者
    /// </summary>
    public string Contributor { get; set; }

    /// <summary>
    /// 文件Id
    /// </summary>
    public string BlobId { get; set; }

    /// <summary>
    /// 文件名称
    /// </summary>
    public string BlobName { get; set; }

    /// <summary>
    /// 备注
    /// </summary>
    public string Remark { get; set; }

    public Guid? TenantId { get; set; }
}
