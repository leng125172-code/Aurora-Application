namespace AuroraStruct3D.AI.Dtos;

/// <summary>
/// AI 模型文件 MD5 查重结果 DTO。
/// </summary>
public class AiModelFileMd5CheckResultDto
{
    /// <summary>传入的 MD5 值。</summary>
    public string Md5 { get; set; } = string.Empty;

    /// <summary>数据库中是否已存在相同文件。</summary>
    public bool Exists { get; set; }

    /// <summary>重复模型 ID。</summary>
    public Guid? ExistingModelId { get; set; }

    /// <summary>重复模型名称。</summary>
    public string? ExistingModelName { get; set; }

    /// <summary>重复模型原始文件名。</summary>
    public string? ExistingOriginalFileName { get; set; }
}
