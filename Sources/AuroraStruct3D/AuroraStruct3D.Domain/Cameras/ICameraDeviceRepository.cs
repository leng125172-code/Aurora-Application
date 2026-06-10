using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.Cameras;

/// <summary>
/// 相机设备仓储接口
/// </summary>
public interface ICameraDeviceRepository : IRepository<CameraDevice, Guid>
{
    /// <summary>
    /// 根据设备物理索引查找相机
    /// </summary>
    /// <param name="deviceIndex">设备索引（SDK中的物理位置）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<CameraDevice?> FindByDeviceIndexAsync(
        int deviceIndex,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 根据设备序列号查找相机
    /// </summary>
    /// <param name="deviceSerialNumber">设备序列号（DeviceControl/DeviceSerialNumber）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<CameraDevice?> FindByDeviceSerialNumberAsync(
        string deviceSerialNumber,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 获取所有启用的相机列表
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task<List<CameraDevice>> GetEnabledListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取相机列表（包含参数集信息，分页）
    /// </summary>
    /// <param name="skipCount">跳过条数</param>
    /// <param name="maxResultCount">最大条数</param>
    /// <param name="filter">名称过滤关键字</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<List<CameraDevice>> GetListAsync(
        int skipCount,
        int maxResultCount,
        string? filter = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 获取相机总数
    /// </summary>
    /// <param name="filter">名称过滤关键字</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<long> GetCountAsync(string? filter = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取相机详情（含所有参数集）
    /// </summary>
    /// <param name="id">相机ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<CameraDevice?> GetWithParameterSetsAsync(
        Guid id,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 仅查询相机的 SDK 设备索引（AsNoTracking 投影查询，不加载完整实体）。
    /// 用于长时间 SDK 操作前的索引获取，避免 EF Core 在操作期间跟踪实体导致并发异常。
    /// </summary>
    /// <param name="id">相机数据库 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>SDK 设备索引</returns>
    Task<int> GetDeviceIndexByIdAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>
/// 相机操作日志仓储接口
/// </summary>
public interface ICameraOperationLogRepository : IRepository<CameraOperationLog, Guid>
{
    /// <summary>
    /// 分页查询指定相机的操作日志，按发生时间倒序
    /// </summary>
    /// <param name="cameraDeviceId">相机设备 ID</param>
    /// <param name="skipCount">跳过条数</param>
    /// <param name="maxResultCount">最大条数</param>
    /// <param name="operationType">按操作类型过滤（可选）</param>
    /// <param name="onlyFailures">仅返回失败记录</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<List<CameraOperationLog>> GetPagedListAsync(
        Guid cameraDeviceId,
        int skipCount,
        int maxResultCount,
        CameraOperationType? operationType = null,
        bool onlyFailures = false,
        DateTime? startTime = null,
        DateTime? endTime = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 统计指定相机的操作日志总数
    /// </summary>
    Task<long> GetCountAsync(
        Guid cameraDeviceId,
        CameraOperationType? operationType = null,
        bool onlyFailures = false,
        DateTime? startTime = null,
        DateTime? endTime = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 删除指定相机在某时间点之前的所有日志（用于日志清理）
    /// </summary>
    Task DeleteBeforeAsync(
        Guid cameraDeviceId,
        DateTime beforeUtc,
        CancellationToken cancellationToken = default
    );
}
