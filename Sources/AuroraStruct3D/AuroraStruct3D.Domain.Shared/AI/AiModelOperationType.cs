namespace AuroraStruct3D.AI;

/// <summary>
/// AI 模型操作记录类型。
/// </summary>
public enum AiModelOperationType
{
    /// <summary>上传模型。</summary>
    Upload = 0,

    /// <summary>更新模型元数据。</summary>
    Update = 1,

    /// <summary>加载模型。</summary>
    Load = 2,

    /// <summary>卸载模型。</summary>
    Unload = 3,

    /// <summary>删除模型。</summary>
    Delete = 4,

    /// <summary>发起推理请求。</summary>
    InferenceRequest = 5,

    /// <summary>清理孤立记录。</summary>
    CleanUpOrphanedRecords = 6,

    /// <summary>启动模型转换。</summary>
    StartConversion = 7,

    /// <summary>删除转换产物文件。</summary>
    DeleteConvertedFile = 8,
}
