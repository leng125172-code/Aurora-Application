using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.Projectors;

/// <summary>
/// 投影机设备仓储接口
/// </summary>
public interface IProjectorDeviceRepository : IRepository<ProjectorDevice, Guid>
{
    /// <summary>
    /// 根据 IP 地址查找投影机设备（TCP 模式）
    /// </summary>
    Task<ProjectorDevice?> FindByIpAddressAsync(
        string ipAddress,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 根据 HID 设备索引查找投影机设备（USB HID 模式，VID/PID 由硬件固定）
    /// </summary>
    Task<ProjectorDevice?> FindByHidAsync(
        int deviceIndex = 0,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 获取所有已启用的投影机设备列表，按序号排序
    /// </summary>
    Task<List<ProjectorDevice>> GetEnabledListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取全部投影机设备列表，按序号排序
    /// </summary>
    Task<List<ProjectorDevice>> GetListOrderedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 投影机操作日志仓储接口
/// </summary>
public interface IProjectorOperationLogRepository : IRepository<ProjectorOperationLog, Guid>
{
    /// <summary>
    /// 获取指定投影机的操作日志分页列表，按时间倒序
    /// </summary>
    /// <param name="projectorDeviceId">投影机设备 ID</param>
    /// <param name="skipCount">跳过条数</param>
    /// <param name="maxResultCount">最大返回数</param>
    /// <param name="operationType">按操作类型筛选（null=不筛选）</param>
    /// <param name="onlyFailures">仅返回失败记录</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<List<ProjectorOperationLog>> GetPagedListAsync(
        Guid projectorDeviceId,
        int skipCount,
        int maxResultCount,
        ProjectorOperationType? operationType = null,
        bool onlyFailures = false,
        DateTime? startTime = null,
        DateTime? endTime = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 获取指定投影机操作日志总数
    /// </summary>
    Task<long> GetCountAsync(
        Guid projectorDeviceId,
        ProjectorOperationType? operationType = null,
        bool onlyFailures = false,
        DateTime? startTime = null,
        DateTime? endTime = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 删除指定投影机指定时间之前的历史日志（用于日志清理）
    /// </summary>
    Task<int> DeleteBeforeAsync(
        Guid projectorDeviceId,
        DateTime beforeUtc,
        CancellationToken cancellationToken = default
    );
}
