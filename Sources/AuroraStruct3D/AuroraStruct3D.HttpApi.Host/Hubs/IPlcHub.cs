using AuroraStruct3D.Plcs;

namespace AuroraStruct3D.Hubs;

public interface IPlcHub
{
    Task ValueChanged(PlcTagValueDto value);
    Task ConnectionStateChanged(Guid deviceId, PlcConnectionStatus status, string? error);
}
