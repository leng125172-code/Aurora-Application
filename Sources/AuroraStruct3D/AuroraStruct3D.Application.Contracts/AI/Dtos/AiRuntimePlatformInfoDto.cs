namespace AuroraStruct3D.AI.Dtos;

/// <summary>
/// AI 运行平台能力信息 DTO。
/// </summary>
public class AiRuntimePlatformInfoDto
{
    /// <summary>是否支持 AI 模型运行。</summary>
    public bool IsSupported { get; set; }

    /// <summary>运行系统名称。</summary>
    public string OperatingSystem { get; set; } = string.Empty;

    /// <summary>系统架构名称。</summary>
    public string Architecture { get; set; } = string.Empty;

    /// <summary>平台显示名称。</summary>
    public string PlatformName { get; set; } = string.Empty;

    /// <summary>不支持原因。</summary>
    public string? UnsupportedReason { get; set; }

    /// <summary>兼容性原始描述。</summary>
    public string? CompatibilityDescription { get; set; }

    /// <summary>允许上传的扩展名列表。</summary>
    public List<string> SupportedExtensions { get; set; } = [];

    /// <summary>是否支持 ONNX 上传后转换。</summary>
    public bool SupportsOnnxConversion { get; set; }
}
