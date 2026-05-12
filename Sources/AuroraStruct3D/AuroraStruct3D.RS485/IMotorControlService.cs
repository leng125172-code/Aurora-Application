namespace AuroraStruct3D.RS485;

/// <summary>
/// 电机控制服务接口，统一管理总线上的所有电机
/// </summary>
public interface IMotorControlService
{
    /// <summary>
    /// 查询指定电机的当前状态
    /// </summary>
    /// <param name="motorId">电机编号（对应 RS485 从机地址）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>电机状态快照</returns>
    Task<MotorStatus> QueryStatusAsync(int motorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询所有电机的状态
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>所有电机状态列表</returns>
    Task<IReadOnlyList<MotorStatus>> QueryAllStatusAsync(
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 使能指定电机
    /// </summary>
    /// <param name="motorId">电机编号</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task EnableAsync(int motorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 去使能指定电机
    /// </summary>
    /// <param name="motorId">电机编号</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task DisableAsync(int motorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 绝对位置运动
    /// </summary>
    /// <param name="motorId">电机编号</param>
    /// <param name="position">目标位置（脉冲数）</param>
    /// <param name="speedRpm">运动速度（RPM），0表示使用默认速度</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task MoveAbsoluteAsync(
        int motorId,
        long position,
        int speedRpm = 0,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 相对位置运动
    /// </summary>
    /// <param name="motorId">电机编号</param>
    /// <param name="delta">位移量（脉冲数）</param>
    /// <param name="speedRpm">运动速度（RPM），0表示使用默认速度</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task MoveRelativeAsync(
        int motorId,
        long delta,
        int speedRpm = 0,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 停止指定电机运动
    /// </summary>
    /// <param name="motorId">电机编号</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task StopAsync(int motorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止所有电机（安全急停）
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task StopAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 紧急停止指定电机（立即断电）
    /// </summary>
    /// <param name="motorId">电机编号</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task EmergencyStopAsync(int motorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行回零
    /// </summary>
    /// <param name="motorId">电机编号</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task HomeAsync(int motorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 清除电机故障
    /// </summary>
    /// <param name="motorId">电机编号</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task ClearFaultAsync(int motorId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查指定电机是否已配置
    /// </summary>
    /// <param name="motorId">电机编号</param>
    bool IsMotorConfigured(int motorId);

    /// <summary>
    /// 获取所有已配置的电机编号列表
    /// </summary>
    IReadOnlyList<int> ConfiguredMotorIds { get; }
}
