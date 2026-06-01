using AuroraStruct3D.Cameras;
using AuroraStruct3D.Cameras.Dtos;

namespace AuroraStruct3D;

/// <summary>
/// 相机模块对象映射辅助类，提供手动映射方法
/// </summary>
public static class CameraObjectMapperExtensions
{
    /// <summary>
    /// 将相机设备实体转换为DTO
    /// </summary>
    public static CameraDeviceDto ToDto(this CameraDevice entity)
    {
        return new CameraDeviceDto
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
            Model = entity.Model,
            DeviceIndex = entity.DeviceIndex,
            Status = entity.Status,
            Description = entity.Description,
            IsEnabled = entity.IsEnabled,
            ActiveParameterSetId = entity.ActiveParameterSetId,
            ImageRotationAngle = entity.ImageRotationAngle,
            ParameterSetCount = entity.ParameterSets?.Count ?? 0,
        };
    }

    /// <summary>
    /// 将相机参数集实体转换为DTO
    /// </summary>
    public static CameraParameterSetDto ToDto(this CameraParameterSet entity)
    {
        return new CameraParameterSetDto
        {
            Id = entity.Id,
            CreationTime = entity.CreationTime,
            CreatorId = entity.CreatorId,
            LastModificationTime = entity.LastModificationTime,
            LastModifierId = entity.LastModifierId,
            IsDeleted = entity.IsDeleted,
            DeletionTime = entity.DeletionTime,
            DeleterId = entity.DeleterId,
            CameraDeviceId = entity.CameraDeviceId,
            Name = entity.Name,
            Description = entity.Description,
            IsDefault = entity.IsDefault,
            SortOrder = entity.SortOrder,
            Parameters =
                entity.Parameters?.Select(p => p.ToDto()).ToList()
                ?? new List<CameraParameterDto>(),
        };
    }

    /// <summary>
    /// 将相机参数项实体转换为DTO
    /// </summary>
    public static CameraParameterDto ToDto(this CameraParameter entity)
    {
        return new CameraParameterDto
        {
            Id = entity.Id,
            ParameterSetId = entity.ParameterSetId,
            ParamKey = entity.ParamKey,
            ParamType = entity.ParamType,
            Value = entity.Value,
            Description = entity.Description,
        };
    }

    /// <summary>
    /// 将相机操作日志实体转换为DTO
    /// </summary>
    public static CameraOperationLogDto ToDto(this CameraOperationLog entity)
    {
        return new CameraOperationLogDto
        {
            Id = entity.Id,
            CameraDeviceId = entity.CameraDeviceId,
            DeviceIndex = entity.DeviceIndex,
            OperationType = entity.OperationType,
            OccurredAt = entity.OccurredAt,
            IsSuccess = entity.IsSuccess,
            ParameterSummary = entity.ParameterSummary,
            ErrorMessage = entity.ErrorMessage,
            RoundTripMs = entity.RoundTripMs,
        };
    }
}
