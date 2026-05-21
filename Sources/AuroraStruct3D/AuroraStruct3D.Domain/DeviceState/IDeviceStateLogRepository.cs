using AuroraStruct3D.DeviceState;
using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.DeviceState;

/// <summary>
/// 设备状态切换日志仓储接口
/// </summary>
public interface IDeviceStateLogRepository : IRepository<DeviceStateLog, Guid>
{
    /// <summary>
    /// 分页查询状态切换日志，按发生时间倒序
    /// </summary>
    /// <param name="skipCount">跳过条数</param>
    /// <param name="maxResultCount">最大条数</param>
    /// <param name="trigger">按触发来源过滤（可选）</param>
    /// <param name="status">按切换后设备状态过滤（可选）</param>
    /// <param name="onlyStatusChanges">仅返回状态变化记录</param>
    /// <param name="onlyModeChanges">仅返回模式变化记录</param>
    /// <param name="startTime">开始时间（UTC，可选）</param>
    /// <param name="endTime">结束时间（UTC，可选）</param>
    /// <param name="isSuccessful">按是否切换成功过滤（可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<List<DeviceStateLog>> GetPagedListAsync(
        int skipCount,
        int maxResultCount,
        StateChangeTrigger? trigger = null,
        DeviceStatus? status = null,
        bool onlyStatusChanges = false,
        bool onlyModeChanges = false,
        DateTime? startTime = null,
        DateTime? endTime = null,
        bool? isSuccessful = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>统计日志总数</summary>
    Task<long> GetCountAsync(
        StateChangeTrigger? trigger = null,
        DeviceStatus? status = null,
        bool onlyStatusChanges = false,
        bool onlyModeChanges = false,
        DateTime? startTime = null,
        DateTime? endTime = null,
        bool? isSuccessful = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>删除指定时间点之前的所有日志（用于日志清理）</summary>
    Task DeleteBeforeAsync(DateTime beforeUtc, CancellationToken cancellationToken = default);
}
