namespace AuroraStruct3D.AI;

/// <summary>
/// Python 转换执行结果。
/// </summary>
public sealed class AiModelPythonConversionResult
{
    /// <summary>是否执行成功。</summary>
    public bool Success { get; init; }

    /// <summary>结果消息。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>标准输出。</summary>
    public string? StandardOutput { get; init; }

    /// <summary>标准错误。</summary>
    public string? StandardError { get; init; }

    /// <summary>转换产物列表。</summary>
    public IReadOnlyList<AiModelPythonConversionOutputFile> OutputFiles { get; init; } = [];
}

/// <summary>
/// Python 转换产物文件。
/// </summary>
public sealed class AiModelPythonConversionOutputFile
{
    /// <summary>来源原始文件 ID。</summary>
    public Guid SourceFileId { get; init; }

    /// <summary>本地产物文件路径。</summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>产物文件名。</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>显示名称。</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>文件格式。</summary>
    public string FileFormat { get; init; } = string.Empty;

    /// <summary>文件角色。</summary>
    public AiModelFileRole FileRole { get; init; }

    /// <summary>显示顺序。</summary>
    public int SortOrder { get; init; }
}
