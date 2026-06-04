namespace AuroraStruct3D.AI.Dtos;

/// <summary>
/// AI 模型文件编辑输入。
/// </summary>
public class AiModelFileUpdateInput
{
    /// <summary>文件 ID。</summary>
    public Guid Id { get; set; }

    /// <summary>文件类型。</summary>
    public AiModelFileRole FileRole { get; set; }

    /// <summary>显示顺序。</summary>
    public int SortOrder { get; set; }
}
