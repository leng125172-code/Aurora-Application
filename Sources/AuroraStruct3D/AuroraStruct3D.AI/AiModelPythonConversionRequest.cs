namespace AuroraStruct3D.AI;

/// <summary>
/// Python 转换执行请求。
/// </summary>
public sealed class AiModelPythonConversionRequest
{
    /// <summary>模型 ID。</summary>
    public Guid ModelId { get; init; }

    /// <summary>模型名称。</summary>
    public string ModelName { get; init; } = string.Empty;

    /// <summary>转换目标类型。</summary>
    public AiModelResolvedConversionType TargetType { get; init; }

    /// <summary>工作目录。</summary>
    public string WorkingDirectory { get; init; } = string.Empty;

    /// <summary>输入文件列表。</summary>
    public IReadOnlyList<AiModelPythonConversionInputFile> InputFiles { get; init; } = [];
}

/// <summary>
/// Python 转换输入文件。
/// </summary>
public sealed class AiModelPythonConversionInputFile
{
    /// <summary>来源文件 ID。</summary>
    public Guid SourceFileId { get; init; }

    /// <summary>本地临时文件路径。</summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>原始文件名。</summary>
    public string OriginalFileName { get; init; } = string.Empty;

    /// <summary>文件角色。</summary>
    public AiModelFileRole FileRole { get; init; }

    /// <summary>显示顺序。</summary>
    public int SortOrder { get; init; }
}
