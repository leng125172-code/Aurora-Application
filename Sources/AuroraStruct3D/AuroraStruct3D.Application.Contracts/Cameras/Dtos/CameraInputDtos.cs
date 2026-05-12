using System.ComponentModel.DataAnnotations;
using AuroraStruct3D.Cameras;

namespace AuroraStruct3D.Cameras.Dtos;

/// <summary>
/// 创建相机设备请求DTO
/// </summary>
public class CreateCameraDeviceDto
{
    /// <summary>相机显示名称</summary>
    [Required]
    [MaxLength(CameraConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    /// <summary>设备物理索引（SDK中从0开始的编号）</summary>
    [Range(0, 63)]
    public int DeviceIndex { get; set; }

    /// <summary>描述</summary>
    [MaxLength(CameraConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// 更新相机设备请求DTO
/// </summary>
public class UpdateCameraDeviceDto
{
    /// <summary>相机显示名称</summary>
    [Required]
    [MaxLength(CameraConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    /// <summary>描述</summary>
    [MaxLength(CameraConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// 相机列表查询请求DTO
/// </summary>
public class GetCameraListDto : Volo.Abp.Application.Dtos.PagedAndSortedResultRequestDto
{
    /// <summary>名称关键字过滤</summary>
    public string? Filter { get; set; }
}

/// <summary>
/// 创建参数集请求DTO
/// </summary>
public class CreateCameraParameterSetDto
{
    /// <summary>所属相机设备ID</summary>
    [Required]
    public Guid CameraDeviceId { get; set; }

    /// <summary>参数集名称</summary>
    [Required]
    [MaxLength(CameraConsts.MaxParameterSetNameLength)]
    public string Name { get; set; } = null!;

    /// <summary>描述</summary>
    [MaxLength(CameraConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>是否为默认参数集</summary>
    public bool IsDefault { get; set; }

    /// <summary>排序序号</summary>
    public int SortOrder { get; set; }

    /// <summary>初始参数列表</summary>
    public List<CreateCameraParameterDto> Parameters { get; set; } = new();
}

/// <summary>
/// 更新参数集请求DTO
/// </summary>
public class UpdateCameraParameterSetDto
{
    /// <summary>参数集名称</summary>
    [Required]
    [MaxLength(CameraConsts.MaxParameterSetNameLength)]
    public string Name { get; set; } = null!;

    /// <summary>描述</summary>
    [MaxLength(CameraConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>是否为默认参数集</summary>
    public bool IsDefault { get; set; }

    /// <summary>排序序号</summary>
    public int SortOrder { get; set; }

    /// <summary>参数列表（全量替换）</summary>
    public List<CreateCameraParameterDto> Parameters { get; set; } = new();
}

/// <summary>
/// 创建/更新参数项请求DTO
/// </summary>
public class CreateCameraParameterDto
{
    /// <summary>参数键名</summary>
    [Required]
    [MaxLength(CameraConsts.MaxParameterKeyLength)]
    public string ParamKey { get; set; } = null!;

    /// <summary>参数类型</summary>
    public CameraParameterType ParamType { get; set; }

    /// <summary>参数值</summary>
    [Required]
    [MaxLength(CameraConsts.MaxParameterValueLength)]
    public string Value { get; set; } = null!;

    /// <summary>描述</summary>
    [MaxLength(CameraConsts.MaxDescriptionLength)]
    public string? Description { get; set; }
}

/// <summary>
/// 应用参数集到相机请求DTO
/// </summary>
public class ApplyCameraParameterSetDto
{
    /// <summary>参数集ID</summary>
    [Required]
    public Guid ParameterSetId { get; set; }
}
