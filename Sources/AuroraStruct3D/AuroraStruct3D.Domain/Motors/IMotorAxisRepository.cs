using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.Motors;

/// <summary>
/// 电机轴仓储接口
/// </summary>
public interface IMotorAxisRepository : IRepository<MotorAxis, Guid>
{
    /// <summary>按从机地址和串口路径查找轴</summary>
    Task<MotorAxis?> FindBySlaveIdAsync(
        string portName,
        int slaveId,
        CancellationToken cancellationToken = default
    );

    /// <summary>获取指定串口上的所有轴（按 AxisIndex 排序）</summary>
    Task<List<MotorAxis>> GetListByPortAsync(
        string portName,
        CancellationToken cancellationToken = default
    );

    /// <summary>获取所有启用的轴</summary>
    Task<List<MotorAxis>> GetEnabledListAsync(CancellationToken cancellationToken = default);
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
