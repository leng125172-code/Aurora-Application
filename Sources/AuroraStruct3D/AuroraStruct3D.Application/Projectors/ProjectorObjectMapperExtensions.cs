using AuroraStruct3D.Projectors;
using AuroraStruct3D.Projectors.Dtos;

namespace AuroraStruct3D;

/// <summary>
/// 投影仪模块对象映射辅助类
/// </summary>
public static class ProjectorObjectMapperExtensions
{
    /// <summary>
    /// 将投影仪设备实体转换为 DTO
    /// </summary>
    public static ProjectorDeviceDto ToDto(this ProjectorDevice entity)
    {
        return new ProjectorDeviceDto
        {
            Id = entity.Id,
            CreationTime = entity.CreationTime,
            CreatorId = entity.CreatorId,
            LastModificationTime = entity.LastModificationTime,
            LastModifierId = entity.LastModifierId,
            IsDeleted = entity.IsDeleted,
            DeletionTime = entity.DeletionTime,
            DeleterId = entity.DeleterId,
            Name = entity.Name,
            DeviceIndex = entity.DeviceIndex,
            Description = entity.Description,
            IsEnabled = entity.IsEnabled,
            ConnectionType = entity.ConnectionType,
            IpAddress = entity.IpAddress,
            TcpPort = entity.TcpPort,
            HidDeviceIndex = entity.HidDeviceIndex,
            ConnectTimeoutMs = entity.ConnectTimeoutMs,
            DeviceHardwareId = entity.DeviceHardwareId,
            ConnectionStatus = entity.ConnectionStatus,
            LedStatus = entity.LedStatus,
            LastLightValue = entity.LastLightValue,
            LastDisplayMode = entity.LastDisplayMode,
            LastColor = entity.LastColor,
            CheckerboardPixelSize = entity.CheckerboardPixelSize,
            FlipMode = entity.FlipMode,
            TriggerMode = entity.TriggerMode,
            BootImage = entity.BootImage,
            LedRgbR = entity.LedRgbR,
            LedRgbG = entity.LedRgbG,
            LedRgbB = entity.LedRgbB,
            LastCommunicationAt = entity.LastCommunicationAt,
            LastConnectedAt = entity.LastConnectedAt,
            LastDisconnectedAt = entity.LastDisconnectedAt,
        };
    }

    /// <summary>
    /// 将投影仪操作日志实体转换为 DTO
    /// </summary>
    public static ProjectorOperationLogDto ToDto(this ProjectorOperationLog entity)
    {
        return new ProjectorOperationLogDto
        {
            Id = entity.Id,
            ProjectorDeviceId = entity.ProjectorDeviceId,
            OperationType = entity.OperationType,
            IsSuccess = entity.IsSuccess,
            RawCommand = entity.RawCommand,
            ParameterSummary = entity.ParameterSummary,
            ErrorMessage = entity.ErrorMessage,
            RoundTripMs = entity.RoundTripMs,
            OccurredAt = entity.OccurredAt,
        };
    }
}
