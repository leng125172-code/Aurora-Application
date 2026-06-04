using System.ComponentModel.DataAnnotations;

namespace AuroraStruct3D.AI.Dtos;

/// <summary>
/// AI 模型文件 MD5 查重输入 DTO。
/// </summary>
public class CheckAiModelFileMd5Input
{
    /// <summary>文件 MD5 值。</summary>
    [Required]
    [MaxLength(32)]
    public string Md5 { get; set; } = string.Empty;
}
