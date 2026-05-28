namespace AuroraStruct3D.ProductModels;

/// <summary>
/// 产品三维数模的转换状态枚举。
/// 记录从原始格式到 PLY 格式的异步转换进度。
/// </summary>
public enum ProductModelConversionStatus
{
    /// <summary>无需转换（原始格式为 PLY 或单个 OBJ，OpenCV 可直接解析）</summary>
    NotRequired = 0,

    /// <summary>待转换（已上传，等待 Hangfire 后台任务处理）</summary>
    Pending = 1,

    /// <summary>转换中（Hangfire Job 已取走，正在执行转换）</summary>
    Converting = 2,

    /// <summary>转换成功（PLY 文件已存入 BLOB 存储）</summary>
    Success = 3,

    /// <summary>转换失败（可手动触发重试）</summary>
    Failed = 4,
}
