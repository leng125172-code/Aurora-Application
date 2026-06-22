namespace AuroraStruct3D.OpenCV.VisionParameters;

public interface IVisionParameter
{
    string? ParameterName { get; }

    string? DisplayName { get; }

    Type ParameterType { get; }

    object? Value { get; set; }

    object? DefaultValue { get; }

    object? ValueLimit { get; }

    bool ErrorCheck { get; }

    PortControlType ControlType { get; }
}
