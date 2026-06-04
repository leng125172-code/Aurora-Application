using System.ComponentModel.DataAnnotations;

namespace AuroraStruct3D.Calibration.Dtos;

/// <summary>
/// 云台组输出 DTO
/// </summary>
public class CalibGimbalGroupDto
{
    /// <summary>主键</summary>
    public Guid Id { get; set; }

    /// <summary>云台组名称</summary>
    public string Name { get; set; } = null!;

    /// <summary>云台组描述</summary>
    public string? Description { get; set; }

    /// <summary>最大速度</summary>
    public decimal MaxSpeed { get; set; }

    /// <summary>加速度</summary>
    public decimal Acceleration { get; set; }

    /// <summary>加速时间（ms）</summary>
    public decimal AccelerationTime { get; set; }

    /// <summary>减速时间（ms）</summary>
    public decimal DecelerationTime { get; set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; }

    /// <summary>创建时间</summary>
    public DateTime CreationTime { get; set; }

    /// <summary>最后修改时间</summary>
    public DateTime? LastModificationTime { get; set; }
}

/// <summary>
/// 查询云台组列表的输入 DTO
/// </summary>
public class GetCalibGimbalGroupListInput : Volo.Abp.Application.Dtos.PagedAndSortedResultRequestDto
{
    /// <summary>名称模糊搜索（为空时不过滤）</summary>
    public string? Filter { get; set; }

    /// <summary>是否启用过滤（为空时不过滤）</summary>
    public bool? IsEnabled { get; set; }

    /// <summary>创建时间起始（UTC，为空时不过滤）</summary>
    public DateTime? StartTime { get; set; }

    /// <summary>创建时间截止（UTC，为空时不过滤）</summary>
    public DateTime? EndTime { get; set; }
}

/// <summary>
/// 新增/修改云台组的输入 DTO
/// </summary>
public class CreateUpdateCalibGimbalGroupInput
{
    /// <summary>云台组名称（必填，最长 256 字符）</summary>
    [Required]
    [MaxLength(CalibConsts.MaxNameLength)]
    public string Name { get; set; } = null!;

    /// <summary>云台组描述（可选）</summary>
    [MaxLength(CalibConsts.MaxDescriptionLength)]
    public string? Description { get; set; }

    /// <summary>最大速度</summary>
    public decimal MaxSpeed { get; set; }

    /// <summary>加速度</summary>
    public decimal Acceleration { get; set; }

    /// <summary>加速时间（ms）</summary>
    public decimal AccelerationTime { get; set; }

    /// <summary>减速时间（ms）</summary>
    public decimal DecelerationTime { get; set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;
}
