using AuroraStruct3D.RS485.Protocol;
using Microsoft.Extensions.Logging;

namespace AuroraStruct3D.RS485.Ktech;

/// <summary>
/// 瓴控KTECH伺服电机驱动实现（私有协议 CMD 0x9A）
/// </summary>
/// <remarks>
/// 通信参数：/dev/ttyS6，波特率 115200，8N1
/// 支持 slave_id 1 和 slave_id 2 的瓴控KTECH电机。
///
/// 行为特性：
///   - 上电后驱动器自动使能，无需软件发送使能指令（Enable/Disable 为空操作）
///   - 回零方式：硬限位（电机以低速运动直到触碰机械限位开关，驱动器内部完成清零）
///
/// 运动控制指令（CMD 0x64/0x65等）需根据瓴控官方文档完善参数格式。
/// </remarks>
public class KtechMotorDriver : IMotorDriver
{
    private readonly IRS485Port _port;
    private readonly ILogger<KtechMotorDriver> _logger;

    /// <inheritdoc/>
    public int SlaveId { get; }

    /// <inheritdoc/>
    public string Brand => "瓴控KTECH";

    /// <inheritdoc/>
    /// <remarks>瓴控KTECH上电即使能，驱动器内部自动管理，无需软件干预。</remarks>
    public bool RequiresEnable => false;

    /// <inheritdoc/>
    /// <remarks>瓴控KTECH采用硬限位回原：电机低速运动直至触碰机械限位，驱动器内部自动清零。</remarks>
    public string HomeMethod => "硬限位";

    /// <summary>
    /// 初始化瓴控KTECH电机驱动
    /// </summary>
    /// <param name="slaveId">从机地址（对应总线上的电机编号）</param>
    /// <param name="port">RS485串口通信接口</param>
    /// <param name="logger">日志记录器</param>
    public KtechMotorDriver(int slaveId, IRS485Port port, ILogger<KtechMotorDriver> logger)
    {
        SlaveId = slaveId;
        _port = port;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<MotorStatus> QueryStatusAsync(CancellationToken cancellationToken = default)
    {
        byte[] request = KtechFrame.BuildQueryStatusFrame((byte)SlaveId);
        byte[] response = await _port
            .SendAndReceiveAsync(
                request,
                KtechFrame.QueryResponseLength,
                timeoutMs: 500,
                cancellationToken
            )
            .ConfigureAwait(false);

        KtechStatusFrame frame = KtechFrame.ParseQueryStatusResponse(response, (byte)SlaveId);

        _logger.LogDebug(
            "[KTECH SlaveId={Id}] 状态查询: 速度={Speed}, 位置={Pos}, 标志=0x{Flags:X2}",
            SlaveId,
            frame.SpeedRaw,
            frame.PositionRaw,
            frame.StatusFlags
        );

        return new MotorStatus
        {
            MotorId = SlaveId,
            Brand = Brand,
            IsEnabled = frame.IsEnabled,
            IsMoving = frame.IsMoving,
            HasFault = frame.HasFault,
            IsHomed = false, // KTECH协议暂无独立的回零完成标志位，需结合位置判断
            CurrentPosition = frame.PositionRaw,
            CurrentSpeed = frame.SpeedRaw,
            RawStatusWord = frame.StatusFlags,
        };
    }

    /// <inheritdoc/>
    /// <remarks>瓴控KTECH上电即使能，此方法为空操作，仅记录调试日志。</remarks>
    public Task EnableAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[KTECH SlaveId={Id}] 无需使能（上电自动使能），跳过", SlaveId);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>瓴控KTECH不支持软件去使能，此方法为空操作，仅记录调试日志。</remarks>
    public Task DisableAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[KTECH SlaveId={Id}] 无需去使能（硬件控制），跳过", SlaveId);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task MoveAbsoluteAsync(
        long position,
        int speedRpm = 0,
        CancellationToken cancellationToken = default
    )
    {
        // position 单位：0.01°/LSB（36000 = 1圈）
        // speedRpm 转换为 0.01dps：1 RPM = 360°/60s = 6°/s = 600 * 0.01°/s = 600 LSB
        uint maxSpeedCentidps = (uint)Math.Clamp((long)speedRpm * 600, 0, uint.MaxValue);
        _logger.LogInformation(
            "[KTECH SlaveId={Id}] 绝对位置定位至 {Pos}(0.01°)，最大速度 {Speed}(0.01dps)",
            SlaveId,
            position,
            maxSpeedCentidps
        );
        byte[] request = KtechFrame.BuildPositionAbsoluteFrame(
            (byte)SlaveId,
            position,
            maxSpeedCentidps
        );
        await _port.SendAndReceiveAsync(request, 0, 500, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task MoveRelativeAsync(
        long delta,
        int speedRpm = 0,
        CancellationToken cancellationToken = default
    )
    {
        // delta 单位：0.01°/LSB，截断到 int32 范围（单一增量限制在 ±21474836.47° 内）
        int incrementCentideg = (int)Math.Clamp(delta, int.MinValue, int.MaxValue);
        uint maxSpeedCentidps = (uint)Math.Clamp((long)speedRpm * 600, 0, uint.MaxValue);
        _logger.LogInformation(
            "[KTECH SlaveId={Id}] 增量定位 {Delta}(0.01°)，最大速度 {Speed}(0.01dps)",
            SlaveId,
            incrementCentideg,
            maxSpeedCentidps
        );
        byte[] request = KtechFrame.BuildPositionRelativeFrame(
            (byte)SlaveId,
            incrementCentideg,
            maxSpeedCentidps
        );
        await _port.SendAndReceiveAsync(request, 0, 500, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[KTECH SlaveId={Id}] 发送减速停止指令（CMD 0x81）", SlaveId);
        // CmdStopHold(0x81)：减速停止，保留使能状态
        byte[] request = KtechFrame.BuildCommandFrame((byte)SlaveId, KtechFrame.CmdStopHold);
        await _port.SendAndReceiveAsync(request, 0, 200, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task EmergencyStopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("[KTECH SlaveId={Id}] 发送紧急关闭指令（CMD 0x80）", SlaveId);
        // CmdClose(0x80)：立即切断电机输出，最快停止
        byte[] request = KtechFrame.BuildCommandFrame((byte)SlaveId, KtechFrame.CmdClose);
        await _port.SendAndReceiveAsync(request, 0, 200, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 瓴控KTECH 硬限位回原：发送回零命令后，电机以低速向限位方向运动，
    /// 触碰机械限位开关后驱动器自动停止并将当前位置清零。
    /// TODO: 回零命令字及参数格式待璀控官方文档确认。
    /// </remarks>
    public async Task HomeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[KTECH SlaveId={Id}] 执行回零（硬限位方式）", SlaveId);
        // TODO: 根据璀控官方文档确认回零命令字和参数
        byte[] request = KtechFrame.BuildCommandFrame((byte)SlaveId, KtechFrame.CmdRun);
        await _port.SendAndReceiveAsync(request, 0, 200, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task ClearFaultAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[KTECH SlaveId={Id}] 发送清除故障指令", SlaveId);
        // 尝试用 CmdRun 重新启动电机清除故障
        // TODO: 根据璀控官方文档确认清除故障的正确命令字
        byte[] request = KtechFrame.BuildCommandFrame((byte)SlaveId, KtechFrame.CmdRun);
        await _port.SendAndReceiveAsync(request, 0, 200, cancellationToken).ConfigureAwait(false);
    }
}
