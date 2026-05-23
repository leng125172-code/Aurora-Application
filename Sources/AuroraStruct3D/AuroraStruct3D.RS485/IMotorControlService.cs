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

    /// <summary>
    /// 判断指定串口配置对应的物理串口当前是否已打开。
    /// </summary>
    /// <param name="serialPortConfigId">串口配置 ID</param>
    /// <returns>已打开返回 true，否则返回 false</returns>
    bool IsSerialPortOpen(Guid serialPortConfigId);

    /// <summary>
    /// 打开指定串口配置对应的物理串口，若波特率等参数变化会重建串口实例。
    /// </summary>
    /// <param name="portConfig">串口通讯配置</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task OpenSerialPortAsync(
        AuroraStruct3D.SerialPorts.SerialPortConfig portConfig,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 关闭指定串口配置对应的物理串口。
    /// </summary>
    /// <param name="serialPortConfigId">串口配置 ID</param>
    void CloseSerialPort(Guid serialPortConfigId);

    /// <summary>
    /// 通过指定串口发送原始字节帧并读取响应，可用于串口调试助手。
    /// </summary>
    /// <param name="portConfig">串口通讯配置</param>
    /// <param name="request">待发送字节</param>
    /// <param name="expectedResponseLength">预期响应长度，0 表示不等待，小于 0 表示读到空闲或超时</param>
    /// <param name="timeoutMs">读取超时毫秒</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<byte[]> SendRawAsync(
        AuroraStruct3D.SerialPorts.SerialPortConfig portConfig,
        byte[] request,
        int expectedResponseLength = -1,
        int timeoutMs = 500,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 从数据库配置初始化串口和电机驱动。
    /// 在应用启动后、从数据库读取 SerialPortConfig 和 MotorAxis 后调用（一次）。
    /// 多个串口各自创建独立的 RS485Port 实例，每条总线上的轴按品牌创建对应驱动。
    /// </summary>
    /// <param name="portConfigs">启用的串口配置列表（来自 AbpProSerialPortConfigs 表）</param>
    /// <param name="enabledAxes">启用的电机轴列表（来自 AbpProMotorAxes 表，需包含 SerialPortConfig 导航属性）</param>
    void Initialize(
        IReadOnlyList<AuroraStruct3D.SerialPorts.SerialPortConfig> portConfigs,
        IReadOnlyList<AuroraStruct3D.Motors.MotorAxis> enabledAxes
    );

    /// <summary>
    /// 注入从机地址到数据库电机轴 ID 的映射（用于操作日志写入）。
    /// 在应用启动后、从数据库加载 MotorAxis 配置后调用。
    /// 未调用此方法时操作日志不会写入数据库。
    /// </summary>
    /// <param name="axisIds">key = SlaveId（从机地址），value = MotorAxis 实体 ID</param>
    void SetAxisIdMapping(IReadOnlyDictionary<int, Guid> axisIds);
}
