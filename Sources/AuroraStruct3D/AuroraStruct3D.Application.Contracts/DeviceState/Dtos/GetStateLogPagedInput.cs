using Volo.Abp.Application.Dtos;

namespace AuroraStruct3D.DeviceState;

/// <summary>
/// 分页查询状态切换日志请求输入
/// </summary>
public class GetStateLogPagedInput : PagedAndSortedResultRequestDto
{
    /// <summary>按触发来源过滤（可选）</summary>
    public StateChangeTrigger? Trigger { get; set; }

    /// <summary>按设备状态过滤（可选）</summary>
    public DeviceStatus? Status { get; set; }

    /// <summary>开始时间（UTC，可选）</summary>
    public DateTime? StartTime { get; set; }

    /// <summary>结束时间（UTC，可选）</summary>
    public DateTime? EndTime { get; set; }

    /// <summary>仅返回成功的切换记录（可选）</summary>
    public bool? IsSuccessful { get; set; }
}
