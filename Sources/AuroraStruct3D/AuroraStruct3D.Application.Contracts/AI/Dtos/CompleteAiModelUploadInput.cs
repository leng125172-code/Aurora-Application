namespace AuroraStruct3D.AI.Dtos;

/// <summary>
/// 完成 AI 模型分片上传输入 DTO。
/// </summary>
public class CompleteAiModelUploadInput : UploadAiModelInput
{
    /// <summary>上传文件列表。</summary>
    public new List<AiModelUploadFileInput> Files { get; set; } = [];
}
