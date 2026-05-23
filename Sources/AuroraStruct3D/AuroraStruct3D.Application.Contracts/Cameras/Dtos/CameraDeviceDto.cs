using AuroraStruct3D.Cameras;
using Volo.Abp.Application.Dtos;

namespace AuroraStruct3D.Cameras.Dtos;

/// <summary>
/// 相机设备DTO
/// </summary>
public class CameraDeviceDto : FullAuditedEntityDto<Guid>
{
    /// <summary>相机显示名称</summary>
    public string Name { get; set; } = null!;

    /// <summary>相机型号</summary>
    public string? Model { get; set; }

    /// <summary>相机序列号</summary>
    public string? SerialNumber { get; set; }

    /// <summary>设备物理索引</summary>
    public int DeviceIndex { get; set; }

    /// <summary>当前状态</summary>
    public CameraStatus Status { get; set; }

    /// <summary>状态名称（中文）</summary>
    public string StatusText =>
        Status switch
        {
            CameraStatus.Ready => "就绪",
            CameraStatus.Capturing => "采集中",
            CameraStatus.Error => "错误",
            CameraStatus.Closed => "已关闭",
            _ => "未知",
        };

    /// <summary>描述</summary>
    public string? Description { get; set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; }

    /// <summary>当前激活的参数集ID</summary>
    public Guid? ActiveParameterSetId { get; set; }

    /// <summary>图像顺时针旋转角度（度）</summary>
    public int ImageRotationAngle { get; set; }

    /// <summary>参数集数量</summary>
    public int ParameterSetCount { get; set; }
}

/// <summary>
/// 相机参数集DTO
/// </summary>
public class CameraParameterSetDto : FullAuditedEntityDto<Guid>
{
    /// <summary>所属相机设备ID</summary>
    public Guid CameraDeviceId { get; set; }

    /// <summary>参数集名称</summary>
    public string Name { get; set; } = null!;

    /// <summary>描述</summary>
    public string? Description { get; set; }

    /// <summary>是否为默认参数集</summary>
    public bool IsDefault { get; set; }

    /// <summary>排序序号</summary>
    public int SortOrder { get; set; }

    /// <summary>参数列表</summary>
    public List<CameraParameterDto> Parameters { get; set; } = new();
}

/// <summary>
/// 相机参数项DTO
/// </summary>
public class CameraParameterDto
{
    /// <summary>参数ID</summary>
    public Guid Id { get; set; }

    /// <summary>所属参数集ID</summary>
    public Guid ParameterSetId { get; set; }

    /// <summary>参数键名</summary>
    public string ParamKey { get; set; } = null!;

    /// <summary>参数类型</summary>
    public CameraParameterType ParamType { get; set; }

    /// <summary>参数类型名称</summary>
    public string ParamTypeText => ParamType == CameraParameterType.Property ? "属性" : "能力";

    /// <summary>参数值（字符串形式）</summary>
    public string Value { get; set; } = null!;

    /// <summary>描述</summary>
    public string? Description { get; set; }
}
