using AuroraStruct3D.Calibration;
using Volo.Abp.Application.Dtos;

namespace AuroraStruct3D.Calibration.Dtos;

/// <summary>
/// 查询标定项目列表的输入 DTO
/// </summary>
public class GetCalibProjectListInput : PagedAndSortedResultRequestDto
{
    /// <summary>名称模糊搜索（为空时不过滤）</summary>
    public string? Filter { get; set; }

    /// <summary>按设备系列过滤（为空时不过滤）</summary>
    public DeviceSeries? DeviceSeries { get; set; }

    /// <summary>按设备类型过滤（为空时不过滤）</summary>
    public CalibDeviceType? DeviceType { get; set; }

    /// <summary>按标定状态过滤（为空时不过滤）</summary>
    public CalibStatus? CalibStatus { get; set; }

    /// <summary>创建时间起始（UTC，为空时不过滤）</summary>
    public DateTime? StartTime { get; set; }

    /// <summary>创建时间截止（UTC，为空时不过滤）</summary>
    public DateTime? EndTime { get; set; }
}
