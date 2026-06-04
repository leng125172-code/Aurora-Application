namespace AuroraStruct3D.AI;

/// <summary>
/// AI 模型模块常量定义。
/// </summary>
public static class AiModelConsts
{
    /// <summary>数据库表名前缀。</summary>
    public const string DbTablePrefix = "AbpPro";

    /// <summary>模型名称最大长度。</summary>
    public const int MaxNameLength = 256;

    /// <summary>模型描述最大长度。</summary>
    public const int MaxDescriptionLength = 2048;

    /// <summary>原始文件名最大长度。</summary>
    public const int MaxOriginalFileNameLength = 512;

    /// <summary>模型标识名称最大长度。</summary>
    public const int MaxIdentifierNameLength = 128;

    /// <summary>模型标识规范化名称最大长度。</summary>
    public const int MaxIdentifierNormalizedNameLength = 128;

    /// <summary>版本号最大长度。</summary>
    public const int MaxVersionLength = 64;

    /// <summary>文件格式最大长度。</summary>
    public const int MaxFileFormatLength = 64;

    /// <summary>转换类型最大长度。</summary>
    public const int MaxConversionTypeLength = 64;

    /// <summary>文件 MD5 最大长度。</summary>
    public const int MaxMd5Length = 32;

    /// <summary>BLOB 键名最大长度。</summary>
    public const int MaxBlobNameLength = 128;

    /// <summary>模型位置标识最大长度。</summary>
    public const int MaxLocationKeyLength = 128;

    /// <summary>模型文件数量上限。</summary>
    public const int MaxFileCountPerModel = 2;

    /// <summary>生成条件最大长度。</summary>
    public const int MaxGenerationConditionLength = 2048;

    /// <summary>加载错误信息最大长度。</summary>
    public const int MaxErrorMessageLength = 2048;

    /// <summary>操作日志中的模型名称最大长度。</summary>
    public const int MaxOperationLogModelNameLength = MaxNameLength;

    /// <summary>操作日志中的文件名最大长度。</summary>
    public const int MaxOperationLogFileNameLength = MaxOriginalFileNameLength;

    /// <summary>操作日志参数摘要最大长度。</summary>
    public const int MaxOperationLogParameterLength = 512;

    /// <summary>操作日志错误消息最大长度。</summary>
    public const int MaxOperationLogErrorMessageLength = 2048;

    /// <summary>BLOB 容器名称。</summary>
    public const string BlobContainerName = "ai-models";
}
