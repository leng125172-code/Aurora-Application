using Lion.AbpPro.Core;
using Lion.AbpPro.ImportExport.Import;

namespace Lion.AbpPro.ImportExportManagement.Import;

/// <summary>
/// 分页查询导入记录
/// </summary>
public class PageImportRecordInput : PagingBase
{
    /// <summary>
    /// 导入名称
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 文件名称
    /// </summary>
    public string BlobName { get; set; }

    /// <summary>
    /// 状态
    /// </summary>
    public ImportStatus? Status { get; set; }

    /// <summary>
    /// 开始创建时间
    /// </summary>
    public DateTime? StartCreationTime { get; set; }

    /// <summary>
    /// 结束创建时间
    /// </summary>
    public DateTime? EndCreationTime { get; set; }
}
