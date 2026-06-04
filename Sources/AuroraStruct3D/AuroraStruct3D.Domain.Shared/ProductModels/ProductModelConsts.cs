namespace AuroraStruct3D.ProductModels;

/// <summary>
/// 产品三维数模模块常量定义
/// </summary>
public static class ProductModelConsts
{
    /// <summary>数据库表名前缀</summary>
    public const string DbTablePrefix = "AbpPro";

    /// <summary>数模名称最大长度</summary>
    public const int MaxNameLength = 256;

    /// <summary>原始文件名最大长度</summary>
    public const int MaxOriginalFileNameLength = 512;

    /// <summary>BLOB 存储键名最大长度（通常为 GUID 字符串）</summary>
    public const int MaxBlobNameLength = 64;

    /// <summary>转换错误信息最大长度</summary>
    public const int MaxErrorMessageLength = 2048;

    /// <summary>操作日志中的数模名称最大长度</summary>
    public const int MaxOperationLogModelNameLength = MaxNameLength;

    /// <summary>操作日志中的原始文件名最大长度</summary>
    public const int MaxOperationLogFileNameLength = MaxOriginalFileNameLength;

    /// <summary>操作日志参数摘要最大长度</summary>
    public const int MaxOperationLogParameterLength = 512;

    /// <summary>操作日志错误消息最大长度</summary>
    public const int MaxOperationLogErrorMessageLength = 2048;

    /// <summary>BLOB 容器名称</summary>
    public const string BlobContainerName = "product-models";

    /// <summary>
    /// 判断指定格式是否需要后台异步转换（即不是 PLY 或单个 OBJ）
    /// </summary>
    public static bool NeedsConversion(ProductModelFormat format)
    {
        return format != ProductModelFormat.PLY && format != ProductModelFormat.OBJ;
    }
}
