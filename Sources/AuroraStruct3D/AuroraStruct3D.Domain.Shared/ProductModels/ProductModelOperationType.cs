namespace AuroraStruct3D.ProductModels;

/// <summary>
/// 三维数模操作记录类型。
/// </summary>
public enum ProductModelOperationType
{
    /// <summary>上传三维数模</summary>
    Upload = 0,

    /// <summary>重命名三维数模</summary>
    Rename = 1,

    /// <summary>删除三维数模</summary>
    Delete = 2,

    /// <summary>重试格式转换</summary>
    RetryConversion = 3,

    /// <summary>下载三维数模</summary>
    Download = 4,

    /// <summary>清理孤立记录</summary>
    CleanUpOrphanedRecords = 5,
}
