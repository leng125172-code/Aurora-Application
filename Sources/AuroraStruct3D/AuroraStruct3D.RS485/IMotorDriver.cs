namespace AuroraStruct3D.RS485;

/// <summary>
/// 电机运行状态数据
/// </summary>
public class MotorStatus
{
    /// <summary>电机编号（对应配置中的 slave_id）</summary>
    public int MotorId { get; init; }

    /// <summary>电机品牌</summary>
    public string Brand { get; init; } = string.Empty;

    /// <summary>电机是否已使能</summary>
    public bool IsEnabled { get; init; }

    /// <summary>电机是否正在运动</summary>
    public bool IsMoving { get; init; }

    /// <summary>是否已完成回零</summary>
    public bool IsHomed { get; init; }

    /// <summary>是否存在故障报警</summary>
    public bool HasFault { get; init; }

    /// <summary>当前位置（脉冲数或电机厂商定义的位置单位）</summary>
    public long CurrentPosition { get; init; }

    /// <summary>当前速度（RPM 或电机厂商定义的速度单位）</summary>
    public int CurrentSpeed { get; init; }

    /// <summary>原始状态字（保留用于调试，不同品牌含义不同）</summary>
    public ushort RawStatusWord { get; init; }
}

/// <summary>
/// 电机底层驱动接口，抽象不同品牌电机的 RS485 通信协议
/// </summary>
public interface IMotorDriver
{
    /// <summary>从机地址</summary>
    int SlaveId { get; }

    /// <summary>电机品牌标识</summary>
    string Brand { get; }

    /// <summary>
    /// 是否需要软件使能指令。
    /// true：必须先发送使能命令电机才能运动（如雷赛iCL-RS）；
    /// false：上电后驱动器自动使能，无需软件干预（如瓴控KTECH）。
    /// </summary>
    bool RequiresEnable { get; }

    /// <summary>
    /// 回零方式描述，供上层显示和日志使用。
    /// 示例："硬限位" / "光电开关DI"
    /// </summary>
    string HomeMethod { get; }

    /// <summary>
    /// 查询电机当前状态
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>电机状态快照</returns>
    Task<MotorStatus> QueryStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 使能电机（上电励磁）。
    /// 对于 <see cref="RequiresEnable"/> 为 false 的驱动，此方法为空操作。
    /// </summary>
    Task EnableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 去使能电机（断电）。
    /// 对于 <see cref="RequiresEnable"/> 为 false 的驱动，此方法为空操作。
    /// </summary>
    Task DisableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 绝对位置运动
    /// </summary>
    /// <param name="position">目标位置（脉冲数）</param>
    /// <param name="speedRpm">运动速度（RPM），0表示使用默认速度</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task MoveAbsoluteAsync(
        long position,
        int speedRpm = 0,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 相对位置运动
    /// </summary>
    /// <param name="delta">位移量（脉冲数，正值正向，负值反向）</param>
    /// <param name="speedRpm">运动速度（RPM），0表示使用默认速度</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task MoveRelativeAsync(
        long delta,
        int speedRpm = 0,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 立即停止运动（减速停止）
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 紧急停止（立即断电停止）
    /// </summary>
    Task EmergencyStopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 回零（归零点）
    /// </summary>
    Task HomeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 清除故障报警
    /// </summary>
    Task ClearFaultAsync(CancellationToken cancellationToken = default);
}
