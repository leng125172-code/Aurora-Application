using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.DeviceState;

/// <summary>
/// 设备故障记录仓储接口
/// </summary>
public interface IDeviceFaultRepository : IRepository<DeviceFault, Guid>
{
    Task<DeviceFault?> FindUnresolvedByFingerprintAsync(string fingerprint, CancellationToken cancellationToken = default);
    /// <summary>
    /// 分页查询故障记录，按发生时间倒序
    /// </summary>
    /// <param name="skipCount">跳过条数</param>
    /// <param name="maxResultCount">最大条数</param>
    /// <param name="faultLevel">按故障级别过滤（可选）</param>
    /// <param name="isResolved">按是否已处理过滤（可选）</param>
    /// <param name="startTime">开始时间（UTC，可选）</param>
    /// <param name="endTime">结束时间（UTC，可选）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<List<DeviceFault>> GetPagedListAsync(
        int skipCount,
        int maxResultCount,
        DeviceFaultSource? source = null,
        DeviceFaultLevel? faultLevel = null,
        bool? isResolved = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>统计故障记录总数</summary>
    Task<long> GetCountAsync(
        DeviceFaultSource? source = null,
        DeviceFaultLevel? faultLevel = null,
        bool? isResolved = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 查询当前未处理的故障记录（IsResolved=false），按级别降序
    /// </summary>
    Task<List<DeviceFault>> GetUnresolvedListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除指定时间点之前的已处理故障记录（用于日志清理）
    /// </summary>
    Task DeleteResolvedBeforeAsync(
        DateTime beforeUtc,
        CancellationToken cancellationToken = default
    );
}
