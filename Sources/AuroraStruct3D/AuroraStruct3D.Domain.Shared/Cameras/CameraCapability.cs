namespace AuroraStruct3D.Cameras;

[Flags]
public enum CameraCapability
{
    None = 0,
    Preview = 1 << 0,
    Snapshot = 1 << 1,
    SoftwareTrigger = 1 << 2,
    ExternalTrigger = 1 << 3,
    ParameterNodes = 1 << 4,
    Temperature = 1 << 5,
    RtpStream = 1 << 6,
    ConcurrentPreview = 1 << 7,
    ConcurrentTrigger = 1 << 8,
}
