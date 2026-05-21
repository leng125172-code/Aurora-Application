using Volo.Abp.Application.Dtos;

namespace AuroraStruct3D.DeviceState;

/// <summary>
/// 分页查询故障记录请求输入
/// </summary>
public class GetFaultPagedInput : PagedAndSortedResultRequestDto
{
    /// <summary>按故障等级过滤（可选）</summary>
    public DeviceFaultLevel? FaultLevel { get; set; }

    /// <summary>按是否已处理过滤（可选）</summary>
    public bool? IsResolved { get; set; }

    /// <summary>开始时间（UTC，可选）</summary>
    public DateTime? StartTime { get; set; }

    /// <summary>结束时间（UTC，可选）</summary>
    public DateTime? EndTime { get; set; }
}
