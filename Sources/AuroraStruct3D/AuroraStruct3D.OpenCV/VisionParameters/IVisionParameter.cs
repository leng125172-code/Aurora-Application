namespace AuroraStruct3D.OpenCV.VisionParameters;

public interface IVisionParameter
{
    string? ParameterName { get; }

    string? DisplayName { get; }

    Type ParameterType { get; }

    object? Value { get; set; }

    object? DefaultValue { get; }

    object? ValueLimit { get; }

    /// <summary>字符串承载结构化 JSON 时的 JSON Schema；普通标量可为空。</summary>
    string? JsonSchema => null;

    bool ErrorCheck { get; }

    PortControlType ControlType { get; }
}
