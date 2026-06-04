using System.ComponentModel.DataAnnotations;

namespace AuroraStruct3D.AI.Dtos;

/// <summary>
/// AI 模型上传文件输入。
/// 用于描述单个文件对应的上传会话和文件角色。
/// </summary>
public class AiModelUploadFileInput
{
    /// <summary>上传会话 ID。</summary>
    public Guid SessionId { get; set; }

    /// <summary>原始文件名。</summary>
    [Required]
    [MaxLength(AiModelConsts.MaxOriginalFileNameLength)]
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>文件 MD5。</summary>
    [Required]
    [MaxLength(AiModelConsts.MaxMd5Length)]
    public string Md5 { get; set; } = string.Empty;

    /// <summary>文件类型。</summary>
    public AiModelFileRole FileRole { get; set; } = AiModelFileRole.SingleWholeModel;

    /// <summary>显示顺序。</summary>
    public int SortOrder { get; set; }
}
