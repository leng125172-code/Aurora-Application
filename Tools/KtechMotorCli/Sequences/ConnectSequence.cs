using System.Diagnostics;
using AuroraStruct3D.RS485.Ktech;
using Microsoft.Extensions.Logging;

namespace KtechMotorCli.Sequences;

/// <summary>
/// 仿瓴控官方 Demo（LK-motor-tool-V2.361）的连接初始化序列。
/// <para>Demo 的 buttonConnect_Click → timer2_Elapsed 状态机按下述顺序依次下发：
/// 0x1F → 0x12 → 0x16 → 0x14 → 0x10。本类在控制台启动或用户执行 <c>connect</c> 命令时
/// 严格复现该流程，并按阶段输出 Step 标签与耗时，便于联调时直观比对 Demo 行为。</para>
/// </summary>
internal static class ConnectSequence
{
    /// <summary>
    /// 各步之间的稳态间隔（ms）。Demo 的 timer2 在阶段切换时使用 130~300ms 节拍，
    /// USB-485 转换器需要时间切换 TX/RX 方向、KTECH 主控也需短暂处理，
    /// 过短的间隔会导致 0x12 等命令拿不到响应。
    /// </summary>
    private const int InterStepDelayMs = 150;

    /// <summary>
    /// 执行完整的 5 步握手。任一步失败仅记日志、继续下一阶段，最终返回是否全部成功。
    /// </summary>
    /// <param name="driver">已构造好的 KTECH 驱动实例（不要求当前已连接）</param>
    /// <param name="logger">CLI 自身 logger，用于打印阶段进度</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>5 步是否全部成功</returns>
    public static async Task<bool> RunAsync(
        KtechMotorDriver driver,
        ILogger logger,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(driver);
        ArgumentNullException.ThrowIfNull(logger);

        logger.LogInformation("══════ 开始连接 KTECH 电机（5 步握手）══════");

        bool allOk = true;
        KtechDeviceType deviceType = KtechDeviceType.Unknown;
        KtechProductInfo? productInfo = null;
        IKtechCalib? calib = null;
        KtechSetting? setting = null;

        // Step 1/5：读取设备类型（CMD 0x1F），Demo 用其决定后续 Calib 解析长度
        allOk &= await RunStepAsync(
            logger,
            "1/5",
            "ReadDeviceType (0x1F)",
            async () =>
            {
                deviceType = await driver.ReadDeviceTypeAsync(cancellationToken);
                logger.LogInformation(
                    "        DeviceType = {Type} (0x{Code:X4})",
                    deviceType,
                    (int)deviceType
                );
            }
        );

        // Step 2/5：读取产品信息（CMD 0x12）—— 驱动名 / 电机名 / 芯片 ID / 版本号
        allOk &= await RunStepAsync(
            logger,
            "2/5",
            "ReadProductInfo (0x12)",
            async () =>
            {
                productInfo = await driver.ReadProductInfoAsync(cancellationToken);
                logger.LogInformation(
                    "        Driver={Drv}  Motor={Mot}  HW={Hw}  FW={Fw}  MotorFW={Mfw}  Chip={Chip}",
                    productInfo.DriverName,
                    productInfo.MotorName,
                    productInfo.HardwareVersion,
                    productInfo.FirmwareVersion,
                    productInfo.MotorVersion,
                    productInfo.ChipId
                );
            }
        );

        // Step 3/5：读取标定（CMD 0x16）—— Demo 据此刷新 Calib Tab
        allOk &= await RunStepAsync(
            logger,
            "3/5",
            "ReadCalib (0x16)",
            async () =>
            {
                calib = await driver.ReadCalibAsync(cancellationToken: cancellationToken);
                logger.LogInformation(
                    "        Calib[{Type}]  Poles={P}  EncType={Et}  EncPos={Ep}  AlignFlag={Af}  OffsetFlag={Of}",
                    calib.DeviceType,
                    calib.MotorPoles,
                    calib.EncoderType,
                    calib.EncoderPos,
                    calib.MotorEncoderAlignFlag,
                    calib.EncoderOffsetFlag
                );
            }
        );

        // Step 4/5：读取参数设置（CMD 0x14）—— Demo 据此刷新 Setting Tab
        allOk &= await RunStepAsync(
            logger,
            "4/5",
            "ReadSetting (0x14)",
            async () =>
            {
                setting = await driver.ReadSettingAsync(cancellationToken: cancellationToken);
                logger.LogInformation(
                    "        Setting  DriverId={Id}  Bus={Bus}  Spin={Spin}  "
                        + "MaxTorque={Mt}  MaxSpeed={Ms}(0.01dps)  AnglePID=({Akp},{Aki},{Akd})",
                    setting.DriverId,
                    setting.BusType,
                    setting.SpinDirection,
                    setting.MaxTorque,
                    setting.MaxSpeed,
                    setting.AnglePidKp,
                    setting.AnglePidKi,
                    setting.AnglePidKd
                );
            }
        );

        // Step 5/5：建立连接（CMD 0x10），Demo 在此后将 deviceConnected 置 true
        allOk &= await RunStepAsync(
            logger,
            "5/5",
            "Connect (0x10)",
            async () =>
            {
                await driver.ConnectAsync(cancellationToken);
                logger.LogInformation("        Connected ✓");
            }
        );

        if (allOk)
        {
            logger.LogInformation("══════ 握手完成：全部 5 步成功 ══════");

            // 所有步骤成功时，打印完整的控件等效摘要（对标 Demo formXxxRefresh）
            if (productInfo is not null && calib is not null && setting is not null)
            {
                ConnectSummary.Print(logger, deviceType, productInfo, calib, setting);
            }
        }
        else
        {
            logger.LogWarning("══════ 握手结束：存在失败步骤，可执行 reconnect 重试 ══════");
        }

        return allOk;
    }

    /// <summary>包一层 try/catch + 耗时统计；首步外都先等待 <see cref="InterStepDelayMs"/> ms</summary>
    private static async Task<bool> RunStepAsync(
        ILogger logger,
        string stepTag,
        string title,
        Func<Task> action
    )
    {
        // Step 1/5 不延时，其余阶段先稳一会儿
        if (!stepTag.StartsWith("1/", StringComparison.Ordinal))
        {
            await Task.Delay(InterStepDelayMs).ConfigureAwait(false);
        }

        Stopwatch sw = Stopwatch.StartNew();
        logger.LogInformation("Step {Step}  {Title} ...", stepTag, title);
        try
        {
            await action();
            sw.Stop();
            logger.LogInformation(
                "Step {Step}  ✓ 完成（{Ms} ms）",
                stepTag,
                sw.ElapsedMilliseconds
            );
            return true;
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(
                ex,
                "Step {Step}  ✗ 失败（{Ms} ms）：{Msg}",
                stepTag,
                sw.ElapsedMilliseconds,
                ex.Message
            );
            return false;
        }
    }
}
