using System.ComponentModel.DataAnnotations;

namespace AuroraStruct3D.OperatorFile.Dtos;

/// <summary>
/// 上传算子文件输入 DTO。
/// 用于指定上传文件的目标算子。
/// </summary>
public class UploadOperatorFileInput
{
    /// <summary>算子唯一标识，对应算子类上的 [Guid] 特性。</summary>
    [Required]
    public Guid OperatorId { get; set; }
}