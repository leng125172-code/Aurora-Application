namespace AuroraStruct3D.OperatorFile.Dtos;

/// <summary>
/// 确认文件使用输入 DTO。
/// 前端保存工作流后调用此接口，标记文件为已使用，取消过期时间。
/// </summary>
public class ConfirmOperatorFileInput
{
    /// <summary>所属项目唯一标识。</summary>
    public Guid ProjectId { get; set; }

    /// <summary>算子唯一标识。</summary>
    public Guid OperatorId { get; set; }

    /// <summary>BLOB 存储键名（上传时返回的 BlobName）。</summary>
    public string BlobName { get; set; } = string.Empty;
}
