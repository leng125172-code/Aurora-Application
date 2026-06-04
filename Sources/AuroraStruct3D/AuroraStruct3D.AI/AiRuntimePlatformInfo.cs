namespace AuroraStruct3D.AI;

/// <summary>
/// AI 运行平台能力信息。
/// </summary>
public sealed class AiRuntimePlatformInfo
{
    /// <summary>是否支持运行。</summary>
    public bool IsSupported { get; init; }

    /// <summary>操作系统名称。</summary>
    public string OperatingSystem { get; init; } = string.Empty;

    /// <summary>架构名称。</summary>
    public string Architecture { get; init; } = string.Empty;

    /// <summary>平台显示名称。</summary>
    public string PlatformName { get; init; } = string.Empty;

    /// <summary>不支持原因。</summary>
    public string? UnsupportedReason { get; init; }

    /// <summary>兼容性原始描述。</summary>
    public string? CompatibilityDescription { get; init; }

    /// <summary>允许扩展名。</summary>
    public IReadOnlyList<string> SupportedExtensions { get; init; } = [];

    /// <summary>是否支持 ONNX 转换。</summary>
    public bool SupportsOnnxConversion { get; init; }
}
