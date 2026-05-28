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
    private const string LogTag = "[Servo drive]";

    private readonly IRS485Port _port;
    private readonly ILogger<KtechMotorDriver> _logger;

    /// <summary>设备类型缓存（首次调用 EnsureDeviceTypeAsync/ReadDeviceTypeAsync 后填充，避免每次读 Calib 都重复探测）</summary>
    private KtechDeviceType? _cachedDeviceType;

    /// <inheritdoc/>
    public int SlaveId { get; }

    /// <inheritdoc/>
    public string Brand => "瓴控KTECH";

    /// <summary>缓存的设备类型（未探测时为 <see cref="KtechDeviceType.Unknown"/>）</summary>
    public KtechDeviceType DeviceType => _cachedDeviceType ?? KtechDeviceType.Unknown;

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
            "{Tag} [KTECH SlaveId={Id}] Status query: Speed={Speed}, Position={Pos}, Flags=0x{Flags:X2}",
            LogTag,
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
        _logger.LogDebug(
            "{Tag} [KTECH SlaveId={Id}] Enable skipped (auto-enabled after power on)",
            LogTag,
            SlaveId
        );
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    /// <remarks>瓴控KTECH不支持软件去使能，此方法为空操作，仅记录调试日志。</remarks>
    public Task DisableAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "{Tag} [KTECH SlaveId={Id}] Disable skipped (hardware controlled)",
            LogTag,
            SlaveId
        );
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
            "{Tag} [KTECH SlaveId={Id}] Move absolute to {Pos} (0.01deg), max speed {Speed} (0.01dps)",
            LogTag,
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
            "{Tag} [KTECH SlaveId={Id}] Move relative by {Delta} (0.01deg), max speed {Speed} (0.01dps)",
            LogTag,
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
        _logger.LogInformation(
            "{Tag} [KTECH SlaveId={Id}] Send decelerating stop command (CMD 0x81)",
            LogTag,
            SlaveId
        );
        // CmdStopHold(0x81)：减速停止，保留使能状态
        byte[] request = KtechFrame.BuildCommandFrame((byte)SlaveId, KtechFrame.CmdStopHold);
        await _port.SendAndReceiveAsync(request, 0, 200, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task EmergencyStopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "{Tag} [KTECH SlaveId={Id}] Send emergency shutdown command (CMD 0x80)",
            LogTag,
            SlaveId
        );
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
        _logger.LogInformation(
            "{Tag} [KTECH SlaveId={Id}] Start homing (hard limit mode)",
            LogTag,
            SlaveId
        );
        // TODO: 根据璀控官方文档确认回零命令字和参数
        byte[] request = KtechFrame.BuildCommandFrame((byte)SlaveId, KtechFrame.CmdRun);
        await _port.SendAndReceiveAsync(request, 0, 200, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task ClearFaultAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "{Tag} [KTECH SlaveId={Id}] Send clear-fault command",
            LogTag,
            SlaveId
        );
        // 尝试用 CmdRun 重新启动电机清除故障
        // TODO: 根据璀控官方文档确认清除故障的正确命令字
        byte[] request = KtechFrame.BuildCommandFrame((byte)SlaveId, KtechFrame.CmdRun);
        await _port.SendAndReceiveAsync(request, 0, 200, cancellationToken).ConfigureAwait(false);
    }

    // ==================== Demo 全功能扩展（基于 KtechProtocol 通用协议层） ====================

    /// <summary>
    /// 通用命令发送：构造帧、发送、等待并解析响应。
    /// </summary>
    /// <param name="command">命令码</param>
    /// <param name="payload">入参 payload（可为空）</param>
    /// <param name="expectedResponsePayloadLength">期望响应 payload 长度（0=无应答; 正数=精确接收）</param>
    /// <param name="timeoutMs">超时（毫秒）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>响应 payload（可能为空数组）</returns>
    public Task<byte[]> SendCommandAsync(
        KtechCommands command,
        ReadOnlySpan<byte> payload,
        int expectedResponsePayloadLength,
        int timeoutMs = 500,
        CancellationToken cancellationToken = default
    )
    {
        // 同步前置：构造帧（Span 在 async 方法中不可用，故先在同步上下文完成）
        byte[] frame = KtechProtocol.BuildFrame((byte)SlaveId, command, payload);
        return SendCommandCoreAsync(
            command,
            frame,
            expectedResponsePayloadLength,
            timeoutMs,
            cancellationToken
        );
    }

    /// <summary>SendCommandAsync 的 async 内核：仅承担 IO 与解析。</summary>
    /// <remarks>
    /// <paramref name="expectedResponsePayloadLength"/> 语义：
    /// <list type="bullet">
    ///   <item><c>0</c>：不等待响应（仅写）。</item>
    ///   <item><c>&gt; 0</c>：精确读取 <c>5 + N + 1</c> 字节（高性能路径）。</item>
    ///   <item><c>&lt; 0</c>：变长响应，读取到串口短暂空闲为止。用于 MG/MGE/MS/MF/MH 型号
    ///         payload 长度不同的命令（如 CMD 0x14 设置、CMD 0x16 标定）。</item>
    /// </list>
    /// </remarks>
    private async Task<byte[]> SendCommandCoreAsync(
        KtechCommands command,
        byte[] frame,
        int expectedResponsePayloadLength,
        int timeoutMs,
        CancellationToken cancellationToken
    )
    {
        if (expectedResponsePayloadLength == 0)
        {
            await _port
                .SendAndReceiveAsync(frame, 0, timeoutMs, cancellationToken)
                .ConfigureAwait(false);
            return [];
        }

        // expectedFrameLen: >0=精确长度, -1=变长（读到空闲）
        int expectedFrameLen =
            expectedResponsePayloadLength > 0
                ? KtechProtocol.ExpectedResponseLength(expectedResponsePayloadLength)
                : -1;

        byte[] response = await _port
            .SendAndReceiveAsync(frame, expectedFrameLen, timeoutMs, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            return KtechProtocol.ParseResponse(response, command, (byte)SlaveId);
        }
        catch (InvalidDataException ex)
        {
            // 帧头校验失败（常见原因：上一条采样命令超时后，电机延迟响应在 DiscardInBuffer()
            // 完成之后才到达缓冲区，污染了本次读取）。
            // 第二次 SendAndReceiveAsync 内部会再次 DiscardInBuffer()，
            // 届时脏数据已全部到达，可被彻底清除，无需额外等待。
            _logger.LogWarning(
                "[KTECH SlaveId={Id}] CMD 0x{Cmd:X2} 帧头污染（{Msg}），自动重试",
                SlaveId,
                (byte)command,
                ex.Message
            );
            byte[] retryResponse = await _port
                .SendAndReceiveAsync(frame, expectedFrameLen, timeoutMs, cancellationToken)
                .ConfigureAwait(false);
            return KtechProtocol.ParseResponse(retryResponse, command, (byte)SlaveId);
        }
    }

    /// <summary>读取设备类型（CMD 0x1F），结果同时写入本驱动缓存</summary>
    public async Task<KtechDeviceType> ReadDeviceTypeAsync(
        CancellationToken cancellationToken = default
    )
    {
        byte[] payload = await SendCommandAsync(
                KtechCommands.ReadDeviceType,
                ReadOnlySpan<byte>.Empty,
                expectedResponsePayloadLength: 2,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        KtechDeviceType type = KtechStructCodec.DecodeDeviceType(payload);
        _cachedDeviceType = type;
        return type;
    }

    /// <summary>
    /// 确保设备类型已探测；若已缓存直接返回，否则首次发送 CMD 0x1F。
    /// 用于在 ReadCalib/WriteCalib 前自动判定 MS/MF/MH vs MG/MG_E 布局。
    /// </summary>
    public async Task<KtechDeviceType> EnsureDeviceTypeAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (_cachedDeviceType is { } cached && cached != KtechDeviceType.Unknown)
        {
            return cached;
        }
        return await ReadDeviceTypeAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>读取产品信息（CMD 0x12）</summary>
    public async Task<KtechProductInfo> ReadProductInfoAsync(
        CancellationToken cancellationToken = default
    )
    {
        byte[] payload = await SendCommandAsync(
                KtechCommands.ReadInfo,
                ReadOnlySpan<byte>.Empty,
                expectedResponsePayloadLength: 58,
                timeoutMs: 800,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        return KtechStructCodec.DecodeProductInfo(payload);
    }

    /// <summary>建立连接握手（CMD 0x10）</summary>
    public Task ConnectAsync(CancellationToken cancellationToken = default) =>
        SendCommandAsync(
            KtechCommands.Connect,
            ReadOnlySpan<byte>.Empty,
            0,
            cancellationToken: cancellationToken
        );

    /// <summary>断开连接（CMD 0x11）</summary>
    public Task DisconnectAsync(CancellationToken cancellationToken = default) =>
        SendCommandAsync(
            KtechCommands.Disconnect,
            ReadOnlySpan<byte>.Empty,
            0,
            cancellationToken: cancellationToken
        );

    /// <summary>重启设备（CMD 0x07）</summary>
    public Task RebootAsync(CancellationToken cancellationToken = default) =>
        SendCommandAsync(
            KtechCommands.RebootDevice,
            ReadOnlySpan<byte>.Empty,
            0,
            cancellationToken: cancellationToken
        );

    /// <summary>读取设备设置（CMD 0x14）</summary>
    /// <param name="expectedPayloadLength">
    /// 期望 payload 字节数；小于 0 表示变长读取（推荐，自动兼容 MS/MF/MH/MG/MGE 不同长度）。
    /// </param>
    public async Task<KtechSetting> ReadSettingAsync(
        int expectedPayloadLength = -1,
        CancellationToken cancellationToken = default
    )
    {
        byte[] payload = await SendCommandAsync(
                KtechCommands.ReadSetting,
                ReadOnlySpan<byte>.Empty,
                expectedResponsePayloadLength: expectedPayloadLength,
                timeoutMs: 800,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        return KtechStructCodec.DecodeSetting(payload);
    }

    /// <summary>写入设备设置到 RAM（CMD 0x15）</summary>
    public Task WriteSettingAsync(
        KtechSetting setting,
        CancellationToken cancellationToken = default
    )
    {
        byte[] payload = KtechStructCodec.EncodeSetting(setting);
        return SendCommandAsync(
            KtechCommands.WriteSetting,
            payload,
            expectedResponsePayloadLength: 0,
            timeoutMs: 2000,
            cancellationToken: cancellationToken
        );
    }

    /// <summary>写入参数到 ROM 持久化（CMD 0x44）</summary>
    public Task PersistParameterToRomAsync(
        KtechSetting setting,
        CancellationToken cancellationToken = default
    )
    {
        byte[] payload = KtechStructCodec.EncodeSetting(setting);
        return SendCommandAsync(
            KtechCommands.WriteParameterToRom,
            payload,
            expectedResponsePayloadLength: 0,
            timeoutMs: 3000,
            cancellationToken: cancellationToken
        );
    }

    /// <summary>重置设置为出厂值（CMD 0x48）</summary>
    public Task ResetSettingAsync(CancellationToken cancellationToken = default) =>
        SendCommandAsync(
            KtechCommands.ResetSettingParameter,
            ReadOnlySpan<byte>.Empty,
            0,
            2000,
            cancellationToken
        );

    /// <summary>读取标定数据（CMD 0x16），自动按设备类型选择 28B(MS/MF/MH) 或 108B(MG/MG_E) 布局</summary>
    /// <param name="expectedPayloadLength">
    /// 期望 payload 字节数；小于 0 表示变长读取（推荐），自动兼容不同型号。
    /// </param>
    public async Task<IKtechCalib> ReadCalibAsync(
        int expectedPayloadLength = -1,
        CancellationToken cancellationToken = default
    )
    {
        // 先确保设备类型已就绪，决定如何解析 payload
        KtechDeviceType type = await EnsureDeviceTypeAsync(cancellationToken).ConfigureAwait(false);
        byte[] payload = await SendCommandAsync(
                KtechCommands.ReadCalib,
                ReadOnlySpan<byte>.Empty,
                expectedResponsePayloadLength: expectedPayloadLength,
                timeoutMs: 800,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        return KtechStructCodec.DecodeCalibAuto(type, payload);
    }

    /// <summary>写入标定数据（CMD 0x17），按 <see cref="IKtechCalib"/> 实际类型多态编码</summary>
    public Task WriteCalibAsync(IKtechCalib calib, CancellationToken cancellationToken = default)
    {
        byte[] payload = KtechStructCodec.EncodeCalibAuto(calib);
        return SendCommandAsync(
            KtechCommands.WriteCalib,
            payload,
            expectedResponsePayloadLength: 0,
            timeoutMs: 2000,
            cancellationToken: cancellationToken
        );
    }

    /// <summary>执行电机编码器对齐（CMD 0x18，返回 7 字节含相序/对齐比率）</summary>
    public async Task<byte[]> CalibrateAsync(CancellationToken cancellationToken = default)
    {
        return await SendCommandAsync(
                KtechCommands.Calibrate,
                ReadOnlySpan<byte>.Empty,
                expectedResponsePayloadLength: 7,
                timeoutMs: 10_000,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>设置编码器零点偏置（CMD 0x19，返回 7 字节含偏置值）</summary>
    public async Task<byte[]> SetEncoderOffsetAsync(CancellationToken cancellationToken = default)
    {
        return await SendCommandAsync(
                KtechCommands.SetOffset,
                ReadOnlySpan<byte>.Empty,
                expectedResponsePayloadLength: 7,
                timeoutMs: 5000,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>重置标定参数（CMD 0x4A）</summary>
    public Task ResetCalibAsync(CancellationToken cancellationToken = default) =>
        SendCommandAsync(
            KtechCommands.ResetCalibParameter,
            ReadOnlySpan<byte>.Empty,
            0,
            2000,
            cancellationToken
        );

    /// <summary>电机开启（CMD 0x88）</summary>
    public Task MotorOnAsync(CancellationToken cancellationToken = default) =>
        SendCommandAsync(
            KtechCommands.MotorOn,
            ReadOnlySpan<byte>.Empty,
            0,
            500,
            cancellationToken
        );

    /// <summary>电机关闭（CMD 0x80）</summary>
    public Task MotorOffAsync(CancellationToken cancellationToken = default) =>
        SendCommandAsync(
            KtechCommands.MotorOff,
            ReadOnlySpan<byte>.Empty,
            0,
            500,
            cancellationToken
        );

    /// <summary>电机停止（CMD 0x81）</summary>
    public Task MotorStopAsync(CancellationToken cancellationToken = default) =>
        SendCommandAsync(
            KtechCommands.MotorStop,
            ReadOnlySpan<byte>.Empty,
            0,
            500,
            cancellationToken
        );

    /// <summary>电机恢复（CMD 0x89）</summary>
    public Task MotorRestoreAsync(CancellationToken cancellationToken = default) =>
        SendCommandAsync(
            KtechCommands.MotorRestore,
            ReadOnlySpan<byte>.Empty,
            0,
            500,
            cancellationToken
        );

    /// <summary>制动控制（CMD 0x8C；release=true 释放制动，false 制动）</summary>
    public Task BrakeAsync(bool release, CancellationToken cancellationToken = default) =>
        SendCommandAsync(
            KtechCommands.OffBrakeControl,
            stackalloc byte[] { release ? (byte)1 : (byte)0 },
            expectedResponsePayloadLength: 1,
            timeoutMs: 500,
            cancellationToken: cancellationToken
        );

    /// <summary>清除电机圈数（CMD 0x93）</summary>
    public Task ClearMotorLoopsAsync(CancellationToken cancellationToken = default) =>
        SendCommandAsync(
            KtechCommands.ClearMotorLoops,
            ReadOnlySpan<byte>.Empty,
            0,
            500,
            cancellationToken
        );

    /// <summary>设置电机零点到 RAM（CMD 0x95）</summary>
    public Task SetMotorZeroRamAsync(CancellationToken cancellationToken = default) =>
        SendCommandAsync(
            KtechCommands.SetMotorZeroRam,
            ReadOnlySpan<byte>.Empty,
            0,
            500,
            cancellationToken
        );

    /// <summary>读取多圈角度（CMD 0x92，单位 0.01°）</summary>
    public async Task<long> ReadMultiAngleAsync(CancellationToken cancellationToken = default)
    {
        byte[] payload = await SendCommandAsync(
                KtechCommands.ReadMotorAngle,
                ReadOnlySpan<byte>.Empty,
                expectedResponsePayloadLength: 8,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        return KtechStructCodec.DecodeMultiAngle(payload);
    }

    /// <summary>读取单圈角度（CMD 0x94，单位 0.01°）</summary>
    public async Task<uint> ReadSingleAngleAsync(CancellationToken cancellationToken = default)
    {
        byte[] payload = await SendCommandAsync(
                KtechCommands.ReadMotorSingleAngle,
                ReadOnlySpan<byte>.Empty,
                expectedResponsePayloadLength: 4,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        return KtechStructCodec.DecodeSingleAngle(payload);
    }

    /// <summary>读取状态 1 + 错误位（CMD 0x9A）</summary>
    public async Task<KtechState1> ReadState1Async(CancellationToken cancellationToken = default)
    {
        byte[] payload = await SendCommandAsync(
                KtechCommands.ReadMotorState1Error,
                ReadOnlySpan<byte>.Empty,
                expectedResponsePayloadLength: 7,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        return KtechStructCodec.DecodeState1(payload);
    }

    /// <summary>清除电机错误标志（CMD 0x9B）</summary>
    public async Task<KtechState1> ClearMotorErrorAsync(
        CancellationToken cancellationToken = default
    )
    {
        byte[] payload = await SendCommandAsync(
                KtechCommands.ClearMotorState1Error,
                ReadOnlySpan<byte>.Empty,
                expectedResponsePayloadLength: 7,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        return KtechStructCodec.DecodeState1(payload);
    }

    /// <summary>读取状态 2（CMD 0x9C）</summary>
    public async Task<KtechState2> ReadState2Async(CancellationToken cancellationToken = default)
    {
        byte[] payload = await SendCommandAsync(
                KtechCommands.ReadMotorState2,
                ReadOnlySpan<byte>.Empty,
                expectedResponsePayloadLength: 7,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        return KtechStructCodec.DecodeState2(payload);
    }

    /// <summary>读取状态 3（CMD 0x9D）</summary>
    public async Task<KtechState3> ReadState3Async(CancellationToken cancellationToken = default)
    {
        byte[] payload = await SendCommandAsync(
                KtechCommands.ReadMotorState3,
                ReadOnlySpan<byte>.Empty,
                expectedResponsePayloadLength: 7,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        return KtechStructCodec.DecodeState3(payload);
    }

    // ---------- 8 种运动控制（MS 型注意：TorqueControl 入参语义为功率 W） ----------

    /// <summary>开环控制（CMD 0xA0，2 字节入参：电压 s16）</summary>
    public async Task<KtechMotionResponse> OpenControlAsync(
        short voltage,
        CancellationToken cancellationToken = default
    )
    {
        Span<byte> payload = stackalloc byte[2];
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(payload, voltage);
        byte[] data = await SendCommandAsync(
                KtechCommands.OpenControl,
                payload,
                expectedResponsePayloadLength: 7,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        return KtechStructCodec.DecodeMotionResponse(data);
    }

    /// <summary>力矩/功率控制（CMD 0xA1，2 字节入参；MS 型为功率 W，其他为电流 mA）</summary>
    public async Task<KtechMotionResponse> TorqueOrPowerControlAsync(
        short torqueOrPower,
        CancellationToken cancellationToken = default
    )
    {
        Span<byte> payload = stackalloc byte[2];
        System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(payload, torqueOrPower);
        byte[] data = await SendCommandAsync(
                KtechCommands.TorqueControl,
                payload,
                expectedResponsePayloadLength: 7,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        return KtechStructCodec.DecodeMotionResponse(data);
    }

    /// <summary>速度控制（CMD 0xA2，4 字节 int32 速度，0.01dps/LSB）</summary>
    public async Task<KtechMotionResponse> SpeedControlAsync(
        int speedCentidps,
        CancellationToken cancellationToken = default
    )
    {
        Span<byte> payload = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(payload, speedCentidps);
        byte[] data = await SendCommandAsync(
                KtechCommands.SpeedControl,
                payload,
                expectedResponsePayloadLength: 7,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        return KtechStructCodec.DecodeMotionResponse(data);
    }

    /// <summary>多圈绝对角度控制（CMD 0xA3，8 字节 int64 角度，0.01°/LSB）</summary>
    public async Task<KtechMotionResponse> MultiAngleAsync(
        long angleCentideg,
        CancellationToken cancellationToken = default
    )
    {
        Span<byte> payload = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(payload, angleCentideg);
        byte[] data = await SendCommandAsync(
                KtechCommands.AngleControl1,
                payload,
                expectedResponsePayloadLength: 7,
                timeoutMs: 1500,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        return KtechStructCodec.DecodeMotionResponse(data);
    }

    /// <summary>多圈绝对角度+速度控制（CMD 0xA4，8+4 字节）</summary>
    public async Task<KtechMotionResponse> MultiAngleWithSpeedAsync(
        long angleCentideg,
        uint maxSpeedCentidps,
        CancellationToken cancellationToken = default
    )
    {
        Span<byte> payload = stackalloc byte[12];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(payload[..8], angleCentideg);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
            payload[8..],
            maxSpeedCentidps
        );
        byte[] data = await SendCommandAsync(
                KtechCommands.AngleControl2,
                payload,
                expectedResponsePayloadLength: 7,
                timeoutMs: 1500,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        return KtechStructCodec.DecodeMotionResponse(data);
    }

    /// <summary>单圈角度控制（CMD 0xA5，1+3 字节：方向 + 角度 uint24 LE，0.01°/LSB）</summary>
    public async Task<KtechMotionResponse> SingleAngleAsync(
        byte direction,
        uint angleCentideg,
        CancellationToken cancellationToken = default
    )
    {
        // 协议角度字段为 uint24（3字节 LE），dataLen=4，非 uint32
        Span<byte> payload = stackalloc byte[4];
        payload[0] = direction;
        payload[1] = (byte)angleCentideg;
        payload[2] = (byte)(angleCentideg >> 8);
        payload[3] = (byte)(angleCentideg >> 16);
        byte[] data = await SendCommandAsync(
                KtechCommands.AngleControl3,
                payload,
                expectedResponsePayloadLength: 7,
                timeoutMs: 1500,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        return KtechStructCodec.DecodeMotionResponse(data);
    }

    /// <summary>单圈角度+速度控制（CMD 0xA6，1+3+4 字节：方向 + 角度 uint24 LE + 速度 uint32 LE）</summary>
    public async Task<KtechMotionResponse> SingleAngleWithSpeedAsync(
        byte direction,
        uint angleCentideg,
        uint maxSpeedCentidps,
        CancellationToken cancellationToken = default
    )
    {
        // 角度字段与 0xA5 一致为 uint24（3字节 LE），总 payload=8 字节
        Span<byte> payload = stackalloc byte[8];
        payload[0] = direction;
        payload[1] = (byte)angleCentideg;
        payload[2] = (byte)(angleCentideg >> 8);
        payload[3] = (byte)(angleCentideg >> 16);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
            payload[4..],
            maxSpeedCentidps
        );
        byte[] data = await SendCommandAsync(
                KtechCommands.AngleControl4,
                payload,
                expectedResponsePayloadLength: 7,
                timeoutMs: 1500,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        return KtechStructCodec.DecodeMotionResponse(data);
    }

    /// <summary>增量角度控制（CMD 0xA7，4 字节 int32 增量，0.01°/LSB）</summary>
    public async Task<KtechMotionResponse> IncrementAngleAsync(
        int incrementCentideg,
        CancellationToken cancellationToken = default
    )
    {
        Span<byte> payload = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(payload, incrementCentideg);
        byte[] data = await SendCommandAsync(
                KtechCommands.AngleControl5,
                payload,
                expectedResponsePayloadLength: 7,
                timeoutMs: 1500,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        return KtechStructCodec.DecodeMotionResponse(data);
    }

    /// <summary>增量角度+速度控制（CMD 0xA8，4+4 字节）</summary>
    public async Task<KtechMotionResponse> IncrementAngleWithSpeedAsync(
        int incrementCentideg,
        uint maxSpeedCentidps,
        CancellationToken cancellationToken = default
    )
    {
        Span<byte> payload = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
            payload[..4],
            incrementCentideg
        );
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(
            payload[4..],
            maxSpeedCentidps
        );
        byte[] data = await SendCommandAsync(
                KtechCommands.AngleControl6,
                payload,
                expectedResponsePayloadLength: 7,
                timeoutMs: 1500,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);
        return KtechStructCodec.DecodeMotionResponse(data);
    }

    /// <summary>开始 IAP 固件升级（CMD 0xBB）</summary>
    public Task BeginIapAsync(CancellationToken cancellationToken = default) =>
        SendCommandAsync(
            KtechCommands.BeginIap,
            ReadOnlySpan<byte>.Empty,
            0,
            1000,
            cancellationToken
        );

    /// <summary>暴露内部串口（用于 Ymodem 升级时直接操作流）</summary>
    public IRS485Port Port => _port;
}
