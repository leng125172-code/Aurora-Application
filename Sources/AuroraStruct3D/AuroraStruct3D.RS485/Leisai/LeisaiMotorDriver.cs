using AuroraStruct3D.RS485.Modbus;
using AuroraStruct3D.RS485.Protocol;
using Microsoft.Extensions.Logging;

namespace AuroraStruct3D.RS485.Leisai;

/// <summary>
/// 雷赛 iCL-RS 伺服驱动器的 Modbus RTU 驱动实现
/// </summary>
/// <remarks>
/// 通信参数：/dev/ttyS6，波特率 115200，8N1，Modbus RTU
/// 从机地址：slave_id 3
///
/// 行为特性：
///   - 上电后需先发送使能指令（CtrlEnable），电机才能接受运动命令
///   - 回零方式：光电开关 DI 回原——电机向原点方向低速运动，
///     触发外部光电传感器 DI 信号后停止并清零当前位置计数
///
/// 雷赛iCL-RS寄存器地址表（常用，其余参考《iCL系列用户手册》）：
///   0x1003 - 状态字（Status Word）：0x0000=就绪，bit1=使能，bit2=运动中，bit3=故障
///   0x1004 - 控制字（Control Word）
///   0x1007 - 当前位置（低16位）
///   0x1008 - 当前位置（高16位，与0x1007组合为32位）
///   0x100B - 目标位置（低16位）
///   0x100C - 目标位置（高16位）
///   0x100D - 目标速度（RPM）
///   0x100E - 运动模式：1=绝对，2=相对，3=回零
/// </remarks>
public class LeisaiMotorDriver : IMotorDriver
{
    private const string LogTag = "[Servo drive]";

    private readonly IRS485Port _port;
    private readonly ILogger<LeisaiMotorDriver> _logger;

    // ===================== 寄存器地址常量 =====================

    /// <summary>状态字寄存器地址</summary>
    private const ushort RegStatusWord = 0x1003;

    /// <summary>控制字寄存器地址</summary>
    private const ushort RegControlWord = 0x1004;

    /// <summary>当前位置低16位寄存器</summary>
    private const ushort RegPositionLow = 0x1007;

    /// <summary>当前速度寄存器（RPM）</summary>
    private const ushort RegCurrentSpeed = 0x100A;

    /// <summary>目标位置低16位寄存器</summary>
    private const ushort RegTargetPositionLow = 0x100B;

    /// <summary>目标位置高16位寄存器</summary>
    private const ushort RegTargetPositionHigh = 0x100C;

    /// <summary>目标速度寄存器（RPM）</summary>
    private const ushort RegTargetSpeed = 0x100D;

    /// <summary>运动模式寄存器（1=绝对，2=相对，3=回零）</summary>
    private const ushort RegMotionMode = 0x100E;

    // ===================== 控制字常量 =====================

    /// <summary>控制字：使能电机</summary>
    private const ushort CtrlEnable = 0x000F;

    /// <summary>控制字：去使能（关闭驱动器）</summary>
    private const ushort CtrlDisable = 0x0000;

    /// <summary>控制字：停止运动</summary>
    private const ushort CtrlStop = 0x000B;

    /// <summary>控制字：启动运动</summary>
    private const ushort CtrlStart = 0x001F;

    /// <summary>控制字：清除故障</summary>
    private const ushort CtrlClearFault = 0x0086;

    // ===================== 状态字位掩码 =====================

    /// <summary>状态字 bit1：已使能</summary>
    private const ushort StatusEnabled = 0x0002;

    /// <summary>状态字 bit2：运动中</summary>
    private const ushort StatusMoving = 0x0004;

    /// <summary>状态字 bit3：故障报警</summary>
    private const ushort StatusFault = 0x0008;

    /// <summary>状态字 bit4：回零完成</summary>
    private const ushort StatusHomed = 0x0010;

    /// <inheritdoc/>
    public int SlaveId { get; }

    /// <inheritdoc/>
    public string Brand => "雷赛iCL-RS";

    /// <inheritdoc/>
    /// <remarks>雷赛iCL-RS需要软件显式使能，上电后驱动器默认处于去使能状态。</remarks>
    public bool RequiresEnable => true;

    /// <inheritdoc/>
    /// <remarks>雷赛iCL-RS采用光电开关DI回原：电机低速运动触发外部光电传感器DI信号后停止清零。</remarks>
    public string HomeMethod => "光电开关DI";

    /// <summary>
    /// 初始化雷赛iCL-RS Modbus RTU驱动
    /// </summary>
    /// <param name="slaveId">从机地址</param>
    /// <param name="port">RS485串口通信接口</param>
    /// <param name="logger">日志记录器</param>
    public LeisaiMotorDriver(int slaveId, IRS485Port port, ILogger<LeisaiMotorDriver> logger)
    {
        SlaveId = slaveId;
        _port = port;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<MotorStatus> QueryStatusAsync(CancellationToken cancellationToken = default)
    {
        // 一次性读取从 0x1003 开始的 8 个寄存器（状态字、控制字、保留、位置低、位置高、速度等）
        byte[] request = ModbusRtuHelper.BuildReadHoldingRegisters(
            (byte)SlaveId,
            RegStatusWord,
            quantity: 8
        );

        int responseLen = ModbusRtuHelper.GetReadResponseLength(8);
        byte[] response = await _port
            .SendAndReceiveAsync(request, responseLen, timeoutMs: 500, cancellationToken)
            .ConfigureAwait(false);

        ushort[] registers = ModbusRtuHelper.ParseReadHoldingRegisters(response, (byte)SlaveId);

        // 按寄存器偏移解析（registers[0] = 0x1003, registers[1] = 0x1004, ...）
        ushort statusWord = registers[0];
        ushort posLow = registers.Length > 4 ? registers[4] : (ushort)0;
        ushort posHigh = registers.Length > 5 ? registers[5] : (ushort)0;
        ushort speed = registers.Length > 7 ? registers[7] : (ushort)0;

        long position = (long)((posHigh << 16) | posLow);

        _logger.LogDebug(
            "{Tag} [Leisai iCL-RS SlaveId={Id}] StatusWord=0x{Status:X4}, Position={Pos}, Speed={Speed}",
            LogTag,
            SlaveId,
            statusWord,
            position,
            speed
        );

        return new MotorStatus
        {
            MotorId = SlaveId,
            Brand = Brand,
            IsEnabled = (statusWord & StatusEnabled) != 0,
            IsMoving = (statusWord & StatusMoving) != 0,
            HasFault = (statusWord & StatusFault) != 0,
            IsHomed = (statusWord & StatusHomed) != 0,
            CurrentPosition = position,
            CurrentSpeed = speed,
            RawStatusWord = statusWord,
        };
    }

    /// <inheritdoc/>
    public async Task EnableAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("{Tag} [Leisai iCL-RS SlaveId={Id}] Enable servo", LogTag, SlaveId);
        await WriteRegisterAsync(RegControlWord, CtrlEnable, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DisableAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("{Tag} [Leisai iCL-RS SlaveId={Id}] Disable servo", LogTag, SlaveId);
        await WriteRegisterAsync(RegControlWord, CtrlDisable, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task MoveAbsoluteAsync(
        long position,
        int speedRpm = 0,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogInformation(
            "{Tag} [Leisai iCL-RS SlaveId={Id}] Move absolute to {Pos} at {Speed} RPM",
            LogTag,
            SlaveId,
            position,
            speedRpm
        );

        // 写入目标位置（高低16位）、目标速度、运动模式，最后写控制字触发运动
        await WritePositionAndSpeedAsync(position, speedRpm, cancellationToken)
            .ConfigureAwait(false);
        await WriteRegisterAsync(RegMotionMode, 0x0001, cancellationToken).ConfigureAwait(false); // 绝对运动
        await WriteRegisterAsync(RegControlWord, CtrlStart, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task MoveRelativeAsync(
        long delta,
        int speedRpm = 0,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogInformation(
            "{Tag} [Leisai iCL-RS SlaveId={Id}] Move relative by {Delta} pulses at {Speed} RPM",
            LogTag,
            SlaveId,
            delta,
            speedRpm
        );

        await WritePositionAndSpeedAsync(delta, speedRpm, cancellationToken).ConfigureAwait(false);
        await WriteRegisterAsync(RegMotionMode, 0x0002, cancellationToken).ConfigureAwait(false); // 相对运动
        await WriteRegisterAsync(RegControlWord, CtrlStart, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "{Tag} [Leisai iCL-RS SlaveId={Id}] Decelerating stop",
            LogTag,
            SlaveId
        );
        await WriteRegisterAsync(RegControlWord, CtrlStop, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task EmergencyStopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("{Tag} [Leisai iCL-RS SlaveId={Id}] Emergency stop", LogTag, SlaveId);
        // 去使能（CtrlDisable=0x0000）立即断开驱动器输出
        // 使用紧急队列，跳过所有普通命令优先执行
        byte[] request = ModbusRtuHelper.BuildWriteSingleRegister(
            (byte)SlaveId,
            RegControlWord,
            CtrlDisable
        );
        int responseLen = ModbusRtuHelper.GetWriteResponseLength();
        await _port
            .SendAndReceiveUrgentAsync(request, responseLen, 200, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 雷赛iCL-RS 光电开关DI回原：写入回零模式（0x0003）并触发运动，
    /// 电机低速运动至光电传感器DI信号触发位置后自动停止，驱动器内部将该点清零。
    /// 需确保外部光电开关已正确接入驱动器DI引脚，且在驱动器参数中配置为回原传感器输入。
    /// </remarks>
    public async Task HomeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "{Tag} [Leisai iCL-RS SlaveId={Id}] Start homing (DI photoelectric sensor mode)",
            LogTag,
            SlaveId
        );
        await WriteRegisterAsync(RegMotionMode, 0x0003, cancellationToken).ConfigureAwait(false); // 回零模式
        await WriteRegisterAsync(RegControlWord, CtrlStart, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task ClearFaultAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("{Tag} [Leisai iCL-RS SlaveId={Id}] Clear fault", LogTag, SlaveId);
        await WriteRegisterAsync(RegControlWord, CtrlClearFault, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 写目标位置（32位拆分为高低16位写入）和目标速度
    /// </summary>
    private async Task WritePositionAndSpeedAsync(
        long position,
        int speedRpm,
        CancellationToken cancellationToken
    )
    {
        ushort posLow = (ushort)(position & 0xFFFF);
        ushort posHigh = (ushort)((position >> 16) & 0xFFFF);
        ushort speed = (ushort)Math.Clamp(speedRpm, 0, ushort.MaxValue);

        // 批量写入：[TargetPositionLow, TargetPositionHigh, TargetSpeed]，减少总线往返次数
        ushort[] values = [posLow, posHigh, speed];
        byte[] request = ModbusRtuHelper.BuildWriteMultipleRegisters(
            (byte)SlaveId,
            RegTargetPositionLow,
            values
        );

        int responseLen = ModbusRtuHelper.GetWriteResponseLength();
        await _port
            .SendAndReceiveAsync(request, responseLen, 500, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 写单个 Modbus 保持寄存器（FC06，公共方法，操作台/采样器复用）
    /// </summary>
    public async Task WriteRegisterAsync(
        ushort address,
        ushort value,
        CancellationToken cancellationToken = default
    )
    {
        byte[] request = ModbusRtuHelper.BuildWriteSingleRegister((byte)SlaveId, address, value);
        int responseLen = ModbusRtuHelper.GetWriteResponseLength();
        await _port
            .SendAndReceiveAsync(request, responseLen, 500, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 读取单个保持寄存器（FC03，quantity=1）
    /// </summary>
    public async Task<ushort> ReadRegisterAsync(
        ushort address,
        CancellationToken cancellationToken = default
    )
    {
        ushort[] regs = await ReadRegistersAsync(address, 1, cancellationToken)
            .ConfigureAwait(false);
        return regs.Length > 0 ? regs[0] : (ushort)0;
    }

    /// <summary>
    /// 批量读取保持寄存器（FC03）
    /// </summary>
    public async Task<ushort[]> ReadRegistersAsync(
        ushort startAddress,
        ushort quantity,
        CancellationToken cancellationToken = default
    )
    {
        byte[] request = ModbusRtuHelper.BuildReadHoldingRegisters(
            (byte)SlaveId,
            startAddress,
            quantity
        );
        int responseLen = ModbusRtuHelper.GetReadResponseLength(quantity);
        byte[] response = await _port
            .SendAndReceiveAsync(request, responseLen, 500, cancellationToken)
            .ConfigureAwait(false);
        return ModbusRtuHelper.ParseReadHoldingRegisters(response, (byte)SlaveId);
    }

    /// <summary>
    /// 批量写入保持寄存器（FC16）
    /// </summary>
    public async Task WriteRegistersAsync(
        ushort startAddress,
        ushort[] values,
        CancellationToken cancellationToken = default
    )
    {
        byte[] request = ModbusRtuHelper.BuildWriteMultipleRegisters(
            (byte)SlaveId,
            startAddress,
            values
        );
        int responseLen = ModbusRtuHelper.GetWriteResponseLength();
        await _port
            .SendAndReceiveAsync(request, responseLen, 500, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 采集一次完整状态快照，用于实时推送。
    /// 任意子读取失败时返回的快照 IsSuccess=false，FailureReason 填异常消息。
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>状态快照元组（不含 AxisId，由上层 Sampler 注入）</returns>
    public async Task<LeisaiStateSnapshot> SampleStateAsync(
        CancellationToken cancellationToken = default
    )
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var snapshot = new LeisaiStateSnapshot { SlaveId = SlaveId, IsSuccess = false };

        try
        {
            // 1) 状态字 0x1003 + 触发字 0x6002
            snapshot.StatusWord = await ReadRegisterAsync(0x1003, cancellationToken)
                .ConfigureAwait(false);
            snapshot.TriggerWord = await ReadRegisterAsync(0x6002, cancellationToken)
                .ConfigureAwait(false);

            // 2) 位置：0x602A 高 16 + 0x602B 低 16（命令位置），0x602C 高 + 0x602D 低（实际位置）
            ushort[] posRegs = await ReadRegistersAsync(0x602A, 4, cancellationToken)
                .ConfigureAwait(false);
            snapshot.CommandPosition = (int)((posRegs[0] << 16) | posRegs[1]);
            snapshot.ActualPosition = (int)((posRegs[2] << 16) | posRegs[3]);

            // 3) 速度判定：四步法
            (snapshot.EffectiveSpeed, snapshot.SpeedSource, snapshot.TriggerMode) =
                await ResolveSpeedAsync(
                        snapshot.StatusWord,
                        snapshot.TriggerWord,
                        cancellationToken
                    )
                    .ConfigureAwait(false);

            // 4) 母线电压 0x0177（单位 0.1V）
            ushort voltageRaw = await ReadRegisterAsync(0x0177, cancellationToken)
                .ConfigureAwait(false);
            snapshot.BusVoltageVolt = voltageRaw / 10.0;

            // 5) IO 状态 0x0179 输入 / 0x017B 输出
            snapshot.InputIoBitmap = await ReadRegisterAsync(0x0179, cancellationToken)
                .ConfigureAwait(false);
            snapshot.OutputIoBitmap = await ReadRegisterAsync(0x017B, cancellationToken)
                .ConfigureAwait(false);

            // 6) 故障 0x2203 + PR 警告 0x601D
            snapshot.CurrentFaultCode = await ReadRegisterAsync(0x2203, cancellationToken)
                .ConfigureAwait(false);
            snapshot.PrWarningCode = await ReadRegisterAsync(0x601D, cancellationToken)
                .ConfigureAwait(false);

            snapshot.IsSuccess = true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            snapshot.IsSuccess = false;
            snapshot.FailureReason = ex.Message;
            _logger.LogWarning(
                ex,
                "{Tag} [Leisai iCL-RS SlaveId={Id}] SampleStateAsync 失败：{Msg}",
                LogTag,
                SlaveId,
                ex.Message
            );
        }
        finally
        {
            sw.Stop();
            snapshot.ElapsedMs = (int)sw.ElapsedMilliseconds;
        }

        return snapshot;
    }

    /// <summary>
    /// 四步法判定当前生效速度与来源：
    ///   1) 0x1003 bit2==0 → 停止；
    ///   2) 0x6002==0x0120 → 回零中（Phase 1 暂返回 0）；
    ///   3) 0x6002 高字节==0x01 → PR{P} 模式，读 0x6203 + 8*P；
    ///   4) 0x6002==0 且 bit2==1 → JOG，读 0x6027。
    /// </summary>
    private async Task<(int Speed, string Source, string Mode)> ResolveSpeedAsync(
        ushort statusWord,
        ushort triggerWord,
        CancellationToken cancellationToken
    )
    {
        bool moving = (statusWord & 0x0004) != 0;
        if (!moving)
        {
            return (0, "Stop", "Idle");
        }

        if (triggerWord == 0x0120)
        {
            return (0, "Homing", "Homing");
        }

        // 0x010P：PR 路径 0..15（高字节 0x01，低字节 0x00..0x0F）
        if ((triggerWord & 0xFF00) == 0x0100)
        {
            int pathIndex = triggerWord & 0x000F;
            ushort prSpeedAddr = (ushort)(0x6203 + 8 * pathIndex);
            ushort sp = await ReadRegisterAsync(prSpeedAddr, cancellationToken)
                .ConfigureAwait(false);
            return ((short)sp, $"PR{pathIndex}", $"PR{pathIndex}");
        }

        if (triggerWord == 0x0000 && moving)
        {
            ushort jogSpeed = await ReadRegisterAsync(0x6027, cancellationToken)
                .ConfigureAwait(false);
            return ((short)jogSpeed, "JOG", "JOG");
        }

        return (0, "Unknown", "Unknown");
    }
}

/// <summary>
/// 采样器返回的轻量快照（不含 AxisId / TimestampMs，由上层 Sampler 注入），
/// 与 Application.Contracts 中的 DTO 解耦。
/// </summary>
public class LeisaiStateSnapshot
{
    public int SlaveId { get; set; }
    public int ElapsedMs { get; set; }
    public bool IsSuccess { get; set; }
    public string? FailureReason { get; set; }
    public ushort StatusWord { get; set; }
    public ushort TriggerWord { get; set; }
    public string TriggerMode { get; set; } = "Idle";
    public int CommandPosition { get; set; }
    public int ActualPosition { get; set; }
    public int EffectiveSpeed { get; set; }
    public string SpeedSource { get; set; } = "Stop";
    public double BusVoltageVolt { get; set; }
    public ushort InputIoBitmap { get; set; }
    public ushort OutputIoBitmap { get; set; }
    public ushort CurrentFaultCode { get; set; }
    public ushort PrWarningCode { get; set; }
}
