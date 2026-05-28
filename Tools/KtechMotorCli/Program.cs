using System.Buffers.Binary;
using System.Globalization;
using System.IO.Ports;
using AuroraStruct3D.RS485;
using AuroraStruct3D.RS485.Ktech;
using AuroraStruct3D.RS485.Protocol;
using KtechMotorCli.Logging;
using KtechMotorCli.Sequences;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace KtechMotorCli;

/// <summary>
/// 瓴控 KTECH 电机调试控制台（替代 Demo「LK-motor-tool」）。
///
/// 用法：
///   KtechMotorCli [--port COM3] [--baud 115200] [--id 1] [--verbose]
///
/// 启动后进入 REPL，输入 help 查看命令。默认参数对应用户当前调试场景：
///   COM3 / 115200 / SlaveId=1。
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        CliOptions options = CliOptions.Parse(args);

        // RS485Port 帧日志走 Debug 级别（被 DemoConsoleFormatter 重排为 TX:/RX:），
        // 其余分类走 Information；--verbose 时全部降到 Debug 方便排查。
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .SetMinimumLevel(options.Verbose ? LogLevel.Debug : LogLevel.Information)
                .AddFilter("AuroraStruct3D.RS485.Protocol.RS485Port", LogLevel.Debug)
                .AddConsole(o => o.FormatterName = DemoConsoleFormatter.Name)
                .AddConsoleFormatter<DemoConsoleFormatter, ConsoleFormatterOptions>();
        });

        ILogger<RS485Port> portLogger = loggerFactory.CreateLogger<RS485Port>();
        ILogger<KtechMotorDriver> driverLogger = loggerFactory.CreateLogger<KtechMotorDriver>();
        ILogger cliLogger = loggerFactory.CreateLogger("KtechMotorCli");

        // 自动探测串口：用户未通过 --port 指定时，扫描系统所有 COM 口
        string? resolvedPort = options.Port;
        if (string.IsNullOrWhiteSpace(resolvedPort))
        {
            resolvedPort = ResolvePortInteractive(cliLogger);
            if (resolvedPort is null)
            {
                return 1;
            }
        }

        cliLogger.LogInformation(
            "瓴控 KTECH 调试控制台 → Port={Port} Baud={Baud} SlaveId={Id} Verbose={V}",
            resolvedPort,
            options.Baud,
            options.SlaveId,
            options.Verbose
        );

        using RS485Port port = new(resolvedPort, options.Baud, portLogger);
        try
        {
            port.Open();
        }
        catch (Exception ex)
        {
            cliLogger.LogError(ex, "打开串口 {Port} 失败", resolvedPort);
            return 1;
        }

        KtechMotorDriver driver = new(options.SlaveId, port, driverLogger);

        // 按瓴控 Demo 标准执行 5 步握手：0x1F → 0x12 → 0x16 → 0x14 → 0x10
        try
        {
            await ConnectSequence.RunAsync(driver, cliLogger);
        }
        catch (Exception ex)
        {
            cliLogger.LogWarning(ex, "初始握手异常（可用 reconnect 重试）");
        }

        PrintHelp();

        CancellationTokenSource appCts = new();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            appCts.Cancel();
        };

        while (!appCts.IsCancellationRequested)
        {
            Console.Write($"ktech[{options.SlaveId}@{resolvedPort}]> ");
            string? line = Console.ReadLine();
            if (line is null)
            {
                break;
            }
            string trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }
            if (
                trimmed.Equals("quit", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("q", StringComparison.OrdinalIgnoreCase)
            )
            {
                break;
            }

            try
            {
                await DispatchAsync(driver, trimmed, cliLogger, appCts.Token);
            }
            catch (OperationCanceledException)
            {
                cliLogger.LogWarning("命令被取消");
            }
            catch (Exception ex)
            {
                cliLogger.LogError(ex, "命令执行失败");
            }
        }

        try
        {
            await driver.DisconnectAsync();
        }
        catch
        {
            // 退出阶段忽略
        }

        port.Close();
        cliLogger.LogInformation("再见～");
        return 0;
    }

    /// <summary>
    /// 自动探测系统中可用的 COM 口。规则：
    ///   - 0 个：报错返回 null
    ///   - 1 个：直接选用
    ///   - 多个：列出让用户输入序号选择
    /// </summary>
    private static string? ResolvePortInteractive(ILogger logger)
    {
        string[] ports = SerialPort.GetPortNames();
        // 部分平台返回 “COM3\0” 之类脏数据，做一次清洗 + 去重 + 自然排序
        ports = ports
            .Select(p => p.Trim().TrimEnd('\0'))
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p =>
                int.TryParse(new string(p.Where(char.IsDigit).ToArray()), out int n)
                    ? n
                    : int.MaxValue
            )
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (ports.Length == 0)
        {
            logger.LogError("未发现任何串口，请检查 USB-485 转换器是否已插入并安装驱动");
            return null;
        }

        if (ports.Length == 1)
        {
            logger.LogInformation("自动选用唯一可用串口：{Port}", ports[0]);
            return ports[0];
        }

        Console.WriteLine();
        Console.WriteLine("检测到多个串口，请输入序号选择（直接回车默认 0）：");
        for (int i = 0; i < ports.Length; i++)
        {
            Console.WriteLine($"  [{i}] {ports[i]}");
        }
        Console.Write("> ");
        string? input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
        {
            return ports[0];
        }
        if (
            int.TryParse(
                input.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int idx
            )
            && idx >= 0
            && idx < ports.Length
        )
        {
            return ports[idx];
        }
        // 也允许直接输入 COM 名称
        string typed = input.Trim();
        if (ports.Any(p => p.Equals(typed, StringComparison.OrdinalIgnoreCase)))
        {
            return typed;
        }
        logger.LogError("输入无效：{Input}", input);
        return null;
    }

    /// <summary>命令分发：解析首个 token 作为命令，其余作为参数</summary>
    private static async Task DispatchAsync(
        KtechMotorDriver driver,
        string line,
        ILogger logger,
        CancellationToken ct
    )
    {
        string[] tokens = line.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );
        string cmd = tokens[0].ToLowerInvariant();
        string[] argv = tokens[1..];

        switch (cmd)
        {
            case "help":
            case "?":
                PrintHelp();
                break;

            // ─── 连接管理 ───
            case "connect":
            case "reconnect":
                await ConnectSequence.RunAsync(driver, logger, ct);
                break;

            case "disconnect":
                await driver.DisconnectAsync(ct);
                logger.LogInformation("Disconnect (0x11) 已下发");
                break;

            case "ports":
                PrintAvailablePorts(logger);
                break;

            // ─── 信息读取 ───
            case "info":
                KtechProductInfo info = await driver.ReadProductInfoAsync(ct);
                logger.LogInformation(
                    "Driver={Drv} Motor={Mot} HW={Hw} FW={Fw} MotorFW={MotorFw} Chip={Chip}",
                    info.DriverName,
                    info.MotorName,
                    info.HardwareVersion,
                    info.FirmwareVersion,
                    info.MotorVersion,
                    info.ChipId
                );
                break;

            case "type":
                KtechDeviceType t = await driver.ReadDeviceTypeAsync(ct);
                logger.LogInformation("DeviceType={Type} ({Code:X4})", t, (int)t);
                break;

            // ─── 状态 ───
            case "status":
                MotorStatus s = await driver.QueryStatusAsync(ct);
                logger.LogInformation(
                    "Enabled={E} Moving={M} Fault={F} Pos={P} Speed={Sp} Flags=0x{Fl:X2}",
                    s.IsEnabled,
                    s.IsMoving,
                    s.HasFault,
                    s.CurrentPosition,
                    s.CurrentSpeed,
                    s.RawStatusWord
                );
                break;

            case "state1":
            {
                KtechState1 r = await driver.ReadState1Async(ct);
                logger.LogInformation(
                    "MotorTemp={T}℃ Voltage={V:F2}V Current={I:F2}A Error={E}",
                    r.MotorTemperature,
                    r.BusVoltage,
                    r.BusCurrent,
                    r.ErrorFlags
                );
                break;
            }

            case "state2":
            {
                KtechState2 r = await driver.ReadState2Async(ct);
                logger.LogInformation(
                    "MotorTemp={T}℃ Tq/Pw={Tq} Speed={Sp} Encoder={Enc}",
                    r.MotorTemperature,
                    r.TorqueOrPower,
                    r.Speed,
                    r.EncoderValue
                );
                break;
            }

            case "state3":
            {
                KtechState3 r = await driver.ReadState3Async(ct);
                logger.LogInformation(
                    "MotorTemp={T}℃ Ia={A}mA Ib={B}mA Ic={C}mA",
                    r.MotorTemperature,
                    r.Ia,
                    r.Ib,
                    r.Ic
                );
                break;
            }

            case "clear-error":
            {
                KtechState1 r = await driver.ClearMotorErrorAsync(ct);
                logger.LogInformation("已清错，残余 Error={E}", r.ErrorFlags);
                break;
            }

            // ─── 配置 / 标定读取 ───
            case "read-setting":
                KtechSetting setting = await driver.ReadSettingAsync(cancellationToken: ct);
                PrintSetting(setting, logger);
                break;

            case "read-calib":
                IKtechCalib calib = await driver.ReadCalibAsync(cancellationToken: ct);
                PrintCalib(calib, logger);
                break;

            // ─── 电机使能 / 制动 ───
            case "on":
                await driver.MotorOnAsync(ct);
                logger.LogInformation("MotorOn (0x88) 已下发");
                break;
            case "off":
                await driver.MotorOffAsync(ct);
                logger.LogInformation("MotorOff (0x80) 已下发");
                break;
            case "stop":
                await driver.MotorStopAsync(ct);
                logger.LogInformation("MotorStop (0x81) 已下发");
                break;
            case "restore":
                await driver.MotorRestoreAsync(ct);
                logger.LogInformation("MotorRestore (0x89) 已下发");
                break;
            case "brake":
                bool release = argv.Length > 0 && argv[0] == "on";
                await driver.BrakeAsync(release, ct);
                logger.LogInformation("Brake {Op} (0x8C) 已下发", release ? "release" : "engage");
                break;

            // ─── 标定 / 零点 ───
            case "align":
            {
                byte[] data = await driver.CalibrateAsync(ct);
                logger.LogInformation(
                    "Calibrate (0x18) 完成，返回={Hex}",
                    Convert.ToHexString(data)
                );
                break;
            }
            case "zero":
            {
                byte[] data = await driver.SetEncoderOffsetAsync(ct);
                logger.LogInformation(
                    "SetOffset (0x19) 完成，返回={Hex}",
                    Convert.ToHexString(data)
                );
                break;
            }
            case "set-zero-ram":
                await driver.SetMotorZeroRamAsync(ct);
                logger.LogInformation("SetMotorZeroRam (0x95) 已下发");
                break;
            case "clear-loops":
                await driver.ClearMotorLoopsAsync(ct);
                logger.LogInformation("ClearMotorLoops (0x93) 已下发");
                break;
            case "reset-setting":
                await driver.ResetSettingAsync(ct);
                logger.LogInformation("ResetSetting (0x48) 已下发");
                break;
            case "reset-calib":
                await driver.ResetCalibAsync(ct);
                logger.LogInformation("ResetCalib (0x4A) 已下发");
                break;
            case "reboot":
                await driver.RebootAsync(ct);
                logger.LogInformation("Reboot (0x07) 已下发");
                break;

            // ─── 角度读取 ───
            case "read-angle":
            {
                long a = await driver.ReadMultiAngleAsync(ct);
                logger.LogInformation("MultiAngle = {Raw} (0.01°) = {Deg:F2}°", a, a / 100.0);
                break;
            }
            case "read-single":
            {
                uint a = await driver.ReadSingleAngleAsync(ct);
                logger.LogInformation("SingleAngle = {Raw} (0.01°) = {Deg:F2}°", a, a / 100.0);
                break;
            }

            // ─── 运动控制 ───
            case "open":
            {
                short v = short.Parse(argv[0], CultureInfo.InvariantCulture);
                KtechMotionResponse r = await driver.OpenControlAsync(v, ct);
                LogMotion("OpenControl(0xA0)", r, logger);
                break;
            }
            case "tq":
            {
                short v = short.Parse(argv[0], CultureInfo.InvariantCulture);
                KtechMotionResponse r = await driver.TorqueOrPowerControlAsync(v, ct);
                LogMotion("TorqueOrPower(0xA1)", r, logger);
                break;
            }
            case "speed":
            {
                int v = int.Parse(argv[0], CultureInfo.InvariantCulture);
                KtechMotionResponse r = await driver.SpeedControlAsync(v, ct);
                LogMotion("SpeedControl(0xA2)", r, logger);
                break;
            }
            case "multi":
            {
                long ang = long.Parse(argv[0], CultureInfo.InvariantCulture);
                KtechMotionResponse r =
                    argv.Length >= 2
                        ? await driver.MultiAngleWithSpeedAsync(
                            ang,
                            uint.Parse(argv[1], CultureInfo.InvariantCulture),
                            ct
                        )
                        : await driver.MultiAngleAsync(ang, ct);
                LogMotion("MultiAngle(0xA3/0xA4)", r, logger);
                break;
            }
            case "single":
            {
                byte dir = byte.Parse(argv[0], CultureInfo.InvariantCulture);
                uint ang = uint.Parse(argv[1], CultureInfo.InvariantCulture);
                KtechMotionResponse r =
                    argv.Length >= 3
                        ? await driver.SingleAngleWithSpeedAsync(
                            dir,
                            ang,
                            uint.Parse(argv[2], CultureInfo.InvariantCulture),
                            ct
                        )
                        : await driver.SingleAngleAsync(dir, ang, ct);
                LogMotion("SingleAngle(0xA5/0xA6)", r, logger);
                break;
            }
            case "inc":
            {
                int ang = int.Parse(argv[0], CultureInfo.InvariantCulture);
                KtechMotionResponse r =
                    argv.Length >= 2
                        ? await driver.IncrementAngleWithSpeedAsync(
                            ang,
                            uint.Parse(argv[1], CultureInfo.InvariantCulture),
                            ct
                        )
                        : await driver.IncrementAngleAsync(ang, ct);
                LogMotion("IncrementAngle(0xA7/0xA8)", r, logger);
                break;
            }

            // ─── 通用原始帧（透传） ───
            // raw <cmdHex> [payloadHex] [expectedLen]
            //   例: raw 1F                    → 读设备类型，等待 2B payload
            //   例: raw A2 64000000 7         → 速度=100 0.01dps，等待 7B payload
            //   例: raw 80 "" 0               → 不等响应
            case "raw":
            {
                if (argv.Length < 1)
                {
                    logger.LogWarning("用法: raw <cmdHex> [payloadHex] [expectedLen=-1]");
                    break;
                }
                byte cmdByte = byte.Parse(
                    argv[0],
                    NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture
                );
                byte[] payload =
                    argv.Length >= 2 && !string.IsNullOrEmpty(argv[1]) && argv[1] != "\"\""
                        ? Convert.FromHexString(argv[1])
                        : [];
                int expectedLen =
                    argv.Length >= 3 ? int.Parse(argv[2], CultureInfo.InvariantCulture) : -1;
                byte[] resp = await driver
                    .SendCommandAsync(
                        (KtechCommands)cmdByte,
                        payload,
                        expectedLen,
                        cancellationToken: ct
                    )
                    .ConfigureAwait(false);
                logger.LogInformation(
                    "RAW 0x{Cmd:X2} → payload[{Len}]={Hex}",
                    cmdByte,
                    resp.Length,
                    Convert.ToHexString(resp)
                );
                break;
            }

            default:
                logger.LogWarning("未知命令：{Cmd}，输入 help 查看命令列表", cmd);
                break;
        }
    }

    private static void LogMotion(string name, KtechMotionResponse r, ILogger logger)
    {
        logger.LogInformation(
            "{Name}: Temp={T}℃ Tq/Pw={Tq} Speed={Sp} Encoder={Enc}",
            name,
            r.MotorTemperature,
            r.TorqueOrPower,
            r.Speed,
            r.EncoderValue
        );
    }

    private static void PrintSetting(KtechSetting s, ILogger logger)
    {
        logger.LogInformation(
            "Setting: DriverId={Id} Bus={Bus} Spin={Spin} MaxTorque={Mt} MaxSpeed={Ms}(0.01dps) "
                + "MaxAngle={Ma}(0.01°) AnglePID=({Akp},{Aki},{Akd}) SpeedPID=({Skp},{Ski},{Skd}) "
                + "CurrentPID=({Ckp},{Cki},{Ckd}) Saved=0x{Sf:X8}",
            s.DriverId,
            s.BusType,
            s.SpinDirection,
            s.MaxTorque,
            s.MaxSpeed,
            s.MaxAngle,
            s.AnglePidKp,
            s.AnglePidKi,
            s.AnglePidKd,
            s.SpeedPidKp,
            s.SpeedPidKi,
            s.SpeedPidKd,
            s.CurrentPidKp,
            s.CurrentPidKi,
            s.CurrentPidKd,
            s.SavedFlag
        );
    }

    private static void PrintCalib(IKtechCalib c, ILogger logger)
    {
        logger.LogInformation(
            "Calib[{Type}]: Poles={P} EncType={Et} EncPos={Ep} Phase={Ph} "
                + "AlignBias={Ab} AlignRatio={Ar} AlignVoltage={Av} AlignFlag={Af} "
                + "EncoderOffset={Eo} OffsetFlag={Of} Saved=0x{Sf:X8}",
            c.DeviceType,
            c.MotorPoles,
            c.EncoderType,
            c.EncoderPos,
            c.MotorPhaseSequence,
            c.MotorEncoderAlignBias,
            c.MotorEncoderAlignRatio,
            c.MotorEncoderAlignVoltage,
            c.MotorEncoderAlignFlag,
            c.EncoderOffset,
            c.EncoderOffsetFlag,
            c.SavedFlag
        );
        if (c is KtechCalibMg mg)
        {
            logger.LogInformation(
                "  [MG] ReductionRatio={Rr} EncoderRelate={Er} EncoderRelateFlag={Erf} "
                    + "Encoder2Offset={E2o} Encoder2OffsetFlag={E2of}",
                mg.ReductionRatio,
                mg.EncoderRelateValue,
                mg.EncoderRelateValueFlag,
                mg.Encoder2Offset,
                mg.Encoder2OffsetFlag
            );
        }
    }

    /// <summary>列出当前系统全部可用 COM 端口</summary>
    private static void PrintAvailablePorts(ILogger logger)
    {
        string[] ports = SerialPort
            .GetPortNames()
            .Select(p => p.Trim().TrimEnd('\0'))
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (ports.Length == 0)
        {
            logger.LogWarning("未发现任何串口");
            return;
        }
        logger.LogInformation("可用串口：{Ports}", string.Join(", ", ports));
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """

            ── 瓴控 KTECH 控制台命令 ──────────────────────────────────────────────
              help / ?                          显示帮助
              quit / exit / q                   退出

              【连接】
              connect / reconnect               执行 Demo 5 步握手（0x1F→0x12→0x16→0x14→0x10）
              disconnect                        断开 (0x11)
              ports                             列出可用串口

              【信息】
              info                              读取产品信息 (CMD 0x12)
              type                              读取设备类型 (CMD 0x1F)
              status                            综合状态查询
              state1 / state2 / state3          读运行态 (0x9A/0x9C/0x9D)
              clear-error                       清错 (0x9B)
              read-setting                      读 Setting (0x14)
              read-calib                        读 Calib (0x16)
              read-angle                        读多圈角度 (0x92)
              read-single                       读单圈角度 (0x94)

              【电机】
              on | off | stop | restore         电机使能/关闭/停止/恢复
              brake on | brake off              抱闸释放/制动 (0x8C)
              align                             电机编码器对齐 (0x18)
              zero                              设置编码器零点 (0x19)
              set-zero-ram                      RAM 零点 (0x95)
              clear-loops                       清电机圈数 (0x93)
              reset-setting | reset-calib       恢复出厂参数/标定
              reboot                            重启设备 (0x07)

              【运动】
              open  <voltage>                   开环 (0xA0)
              tq    <val>                       力矩(mA)/功率(W,MS型) (0xA1)
              speed <centidps>                  速度 (0xA2) 0.01dps/LSB
              multi <centideg> [maxSpeed]       多圈角度 (0xA3/0xA4)
              single <dir 0|1> <centideg> [sp]  单圈角度 (0xA5/0xA6)
              inc   <centideg> [maxSpeed]       增量角度 (0xA7/0xA8)

              【高级】
              raw <cmdHex> [payloadHex] [expectedLen]
                                                直接构帧透传，便于协议联调
                                                expectedLen<0=变长, 0=不等响应, >0=精确字节

            提示: 角度单位 0.01°；速度单位 0.01dps；电压单位 0.01V（具体 LSB 见命令注释）
            ──────────────────────────────────────────────────────────────────────

            """
        );
    }
}

/// <summary>命令行参数解析（极简，避免引入额外依赖）</summary>
internal sealed class CliOptions
{
    /// <summary>串口号；为 null 表示自动检测</summary>
    public string? Port { get; init; }
    public int Baud { get; init; } = 115200;
    public int SlaveId { get; init; } = 1;
    public bool Verbose { get; init; }

    public static CliOptions Parse(string[] args)
    {
        string? port = null;
        int baud = 115200;
        int id = 1;
        bool verbose = false;

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "--port" or "-p" when i + 1 < args.Length:
                    port = args[++i];
                    break;
                case "--baud" or "-b" when i + 1 < args.Length:
                    baud = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--id" or "-i" when i + 1 < args.Length:
                    id = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--verbose" or "-v":
                    verbose = true;
                    break;
                case "--help" or "-h":
                    Console.WriteLine(
                        "用法: KtechMotorCli [--port COM3] [--baud 115200] [--id 1] [--verbose]"
                    );
                    Environment.Exit(0);
                    break;
            }
        }

        return new CliOptions
        {
            Port = port,
            Baud = baud,
            SlaveId = id,
            Verbose = verbose,
        };
    }
}
