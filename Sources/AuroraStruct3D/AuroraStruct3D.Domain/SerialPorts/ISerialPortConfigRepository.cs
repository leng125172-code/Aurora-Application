using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.SerialPorts;

/// <summary>
/// 串口通讯配置仓储接口
/// </summary>
public interface ISerialPortConfigRepository : IRepository<SerialPortConfig, Guid>
{
    /// <summary>
    /// 按系统串口名称查找配置（如 COM3、/dev/ttyS6）
    /// </summary>
    /// <param name="portName">系统串口名称</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>匹配的串口配置，不存在则返回 null</returns>
    Task<SerialPortConfig?> FindByPortNameAsync(
        string portName,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 获取串口配置列表
    /// </summary>
    /// <param name="isEnabled">筛选启用状态，null 表示不筛选</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>串口配置列表（按显示名称排序）</returns>
    Task<List<SerialPortConfig>> GetListAsync(
        bool? isEnabled = null,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// 串口操作日志仓储接口
/// </summary>
public interface ISerialPortOperationLogRepository : IRepository<SerialPortOperationLog, Guid>
{
    /// <summary>
    /// 分页查询指定串口的操作日志（按 OccurredAt 倒序）
    /// </summary>
    /// <param name="serialPortConfigId">串口配置 ID</param>
    /// <param name="skipCount">跳过条数</param>
    /// <param name="maxResultCount">每页最大条数</param>
    /// <param name="operationType">按操作类型过滤（可选）</param>
    /// <param name="onlyFailures">仅返回失败记录</param>
    /// <param name="startTime">开始时间（可选，UTC）</param>
    /// <param name="endTime">结束时间（可选，UTC）</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<List<SerialPortOperationLog>> GetPagedListAsync(
        Guid serialPortConfigId,
        int skipCount,
        int maxResultCount,
        string? operationType = null,
        bool onlyFailures = false,
        DateTime? startTime = null,
        DateTime? endTime = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 统计指定串口的操作日志总数
    /// </summary>
    Task<long> GetCountAsync(
        Guid serialPortConfigId,
        string? operationType = null,
        bool onlyFailures = false,
        DateTime? startTime = null,
        DateTime? endTime = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 删除指定串口在某时间点之前的所有日志（用于日志清理）
    /// </summary>
    Task<int> DeleteBeforeAsync(
        Guid serialPortConfigId,
        DateTime beforeUtc,
        CancellationToken cancellationToken = default
    );
}
