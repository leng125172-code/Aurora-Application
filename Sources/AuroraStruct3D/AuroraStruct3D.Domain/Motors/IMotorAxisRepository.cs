using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.Motors;

/// <summary>
/// 电机轴仓储接口
/// </summary>
public interface IMotorAxisRepository : IRepository<MotorAxis, Guid>
{
    /// <summary>按从机地址和串口配置查找轴</summary>
    Task<MotorAxis?> FindBySlaveIdAsync(
        Guid serialPortConfigId,
        int slaveId,
        CancellationToken cancellationToken = default
    );

    /// <summary>获取指定串口配置上的所有轴（按 AxisIndex 排序）</summary>
    Task<List<MotorAxis>> GetListByPortAsync(
        Guid serialPortConfigId,
        CancellationToken cancellationToken = default
    );

    /// <summary>获取所有启用的轴</summary>
    Task<List<MotorAxis>> GetEnabledListAsync(CancellationToken cancellationToken = default);

    /// <summary>获取当前最大 AxisIndex（无轴时返回 null），不跟踪实体</summary>
    Task<int?> GetMaxAxisIndexAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// PR路径仓储接口（雷赛iCL-RS专用）
/// </summary>
public interface IMotorPrPathRepository : IRepository<MotorPrPath, Guid>
{
    /// <summary>获取指定轴的所有PR路径（按 PathIndex 排序）</summary>
    Task<List<MotorPrPath>> GetListByAxisAsync(
        Guid motorAxisId,
        CancellationToken cancellationToken = default
    );

    /// <summary>获取指定轴指定编号的PR路径</summary>
    Task<MotorPrPath?> FindByIndexAsync(
        Guid motorAxisId,
        int pathIndex,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// 电机操作日志仓储接口
/// </summary>
public interface IMotorOperationLogRepository : IRepository<MotorOperationLog, Guid>
{
    /// <summary>
    /// 分页查询指定电机轴的操作日志（按 OccurredAt 倒序）
    /// </summary>
    /// <param name="motorAxisId">电机轴 ID</param>
    /// <param name="skipCount">跳过条数</param>
    /// <param name="maxResultCount">每页最大条数</param>
    /// <param name="operationType">按操作类型过滤（可选）</param>
    /// <param name="onlyFailures">仅返回失败记录</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<List<MotorOperationLog>> GetPagedListAsync(
        Guid motorAxisId,
        int skipCount,
        int maxResultCount,
        MotorOperationType? operationType = null,
        bool onlyFailures = false,
        DateTime? startTime = null,
        DateTime? endTime = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 统计指定电机轴的操作日志总数
    /// </summary>
    Task<long> GetCountAsync(
        Guid motorAxisId,
        MotorOperationType? operationType = null,
        bool onlyFailures = false,
        DateTime? startTime = null,
        DateTime? endTime = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 删除指定时间点之前的历史日志（用于定期清理）
    /// </summary>
    /// <param name="motorAxisId">电机轴 ID</param>
    /// <param name="beforeUtc">UTC 截止时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>实际删除的记录数</returns>
    Task<int> DeleteBeforeAsync(
        Guid motorAxisId,
        DateTime beforeUtc,
        CancellationToken cancellationToken = default
    );
}
