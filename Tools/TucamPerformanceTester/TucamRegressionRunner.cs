using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuroraStruct3D.Cameras;
using AuroraStruct3D.Cameras.Tucam;
using AuroraStruct3D.Cameras.Tucam.Interop;
using Microsoft.Extensions.Logging;

namespace TucamPerformanceTester;

internal static class TucamRegressionRunner
{
    private const int ExpectedCameraCount = 2;

    public static async Task<int> RunAsync(string[] args)
    {
        string outputDirectory = GetOption(args, "--output")
            ?? Path.Combine(AppContext.BaseDirectory, "regression-results");
        Directory.CreateDirectory(outputDirectory);
        // Tucam SDK 会在当前目录写入以序列号命名的 XML 配置。
        // 将工作目录隔离到报告目录，避免回归污染源码仓库。
        Environment.CurrentDirectory = Path.GetFullPath(outputDirectory);

        var report = new RegressionReport
        {
            StartedAt = DateTimeOffset.Now,
            MachineName = Environment.MachineName,
            AssemblyName = typeof(TucamCameraService).Assembly.GetName().Name ?? string.Empty,
            AssemblyVersion =
                typeof(TucamCameraService).Assembly.GetName().Version?.ToString() ?? string.Empty,
        };

        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
        var service = new TucamCameraService(loggerFactory.CreateLogger<TucamCameraService>());
        var driver = (ICameraDriver)service;

        try
        {
            await RunCaseAsync(report, "程序集名称", true, () =>
            {
                Ensure(
                    report.AssemblyName == "AuroraStruct3D.Cameras",
                    $"实际程序集为 {report.AssemblyName}"
                );
                return Task.CompletedTask;
            });

            IReadOnlyList<CameraDiscovery> devices = [];
            await RunCaseAsync(report, "双相机扫描与稳定身份", true, async () =>
            {
                devices = await driver.ScanAsync();
                Ensure(
                    devices.Count == ExpectedCameraCount,
                    $"要求 {ExpectedCameraCount} 台，实际发现 {devices.Count} 台"
                );
                Ensure(
                    devices.All(x => !string.IsNullOrWhiteSpace(x.HardwareId)),
                    "存在空 HardwareId"
                );
                Ensure(
                    devices.Select(x => x.HardwareId)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count() == devices.Count,
                    "存在重复 HardwareId"
                );
                foreach (CameraDiscovery device in devices)
                    report.Devices.Add(ToDeviceResult(device));
            });

            if (devices.Count == ExpectedCameraCount)
            {
                // ScanAsync 为保持稳定枚举会持有全部句柄。生命周期回归必须先完整关闭，
                // 让 Tucam SDK 执行“关闭最后一台后重新初始化”的恢复路径。
                foreach (CameraDiscovery device in devices.Reverse())
                    await SafeStopAndCloseAsync(driver, device.HardwareId);

                foreach (CameraDiscovery device in devices)
                {
                    await RunDeviceRegressionAsync(report, service, driver, device);
                    // 上一个设备测试结束后重新扫描，刷新 SDK 重初始化后的索引映射。
                    devices = await driver.ScanAsync();
                    foreach (CameraDiscovery opened in devices.Reverse())
                        await SafeStopAndCloseAsync(driver, opened.HardwareId);
                }

                await RunCaseAsync(report, "关闭后重新扫描身份一致", true, async () =>
                {
                    IReadOnlyList<CameraDiscovery> rescanned = await driver.ScanAsync();
                    string[] before = devices.Select(x => x.HardwareId)
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    string[] after = rescanned.Select(x => x.HardwareId)
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    Ensure(before.SequenceEqual(after, StringComparer.OrdinalIgnoreCase), "重扫身份发生变化");
                });

                await RunCaseAsync(report, "双相机顺序采集无串台", true, async () =>
                {
                    var signatures = new List<string>();
                    foreach (CameraDiscovery device in devices)
                    {
                        await driver.OpenAsync(device.HardwareId);
                        try
                        {
                            await driver.StartCaptureAsync(device.HardwareId);
                            CameraDriverFrame frame = await driver.GrabFrameAsync(
                                device.HardwareId,
                                8000
                            );
                            signatures.Add(FrameSignature(frame));
                            await driver.StopCaptureAsync(device.HardwareId);
                        }
                        finally
                        {
                            await SafeStopAndCloseAsync(driver, device.HardwareId);
                        }
                    }
                    Ensure(signatures.Count == 2, "未取得两台相机帧");
                    report.Notes.Add(
                        $"顺序帧签名（仅用于串台诊断）：{string.Join(", ", signatures)}"
                    );
                });
            }
        }
        catch (Exception ex)
        {
            report.Cases.Add(
                RegressionCaseResult.Failed("回归运行器未处理异常", true, TimeSpan.Zero, ex)
            );
        }
        finally
        {
            try
            {
                await service.UninitializeAsync();
            }
            catch (Exception ex)
            {
                report.Cases.Add(
                    RegressionCaseResult.Failed("SDK 反初始化", true, TimeSpan.Zero, ex)
                );
            }
        }

        report.Notes.Add("USB 端口交换/拔插后的身份重绑定必须人工操作后再次运行本命令确认。");
        report.FinishedAt = DateTimeOffset.Now;
        report.Passed = report.Cases.Count > 0 && report.Cases.All(x => x.Passed || !x.Blocking);
        await WriteReportAsync(outputDirectory, report);
        PrintSummary(report, outputDirectory);
        return report.Passed ? 0 : 1;
    }

    private static async Task RunDeviceRegressionAsync(
        RegressionReport report,
        TucamCameraService service,
        ICameraDriver driver,
        CameraDiscovery device
    )
    {
        string label = $"{device.Model}/{Mask(device.HardwareId)}";
        await RunCaseAsync(report, $"{label} 生命周期循环 20 次", true, async () =>
        {
            for (int i = 0; i < 20; i++)
            {
                await driver.OpenAsync(device.HardwareId);
                Ensure(driver.IsOpen(device.HardwareId), $"第 {i + 1} 次打开状态错误");
                await driver.CloseAsync(device.HardwareId);
                Ensure(!driver.IsOpen(device.HardwareId), $"第 {i + 1} 次关闭状态错误");
            }
        });

        await driver.OpenAsync(device.HardwareId);
        int index = GetIndex(driver, device.HardwareId);
        CameraParameterSnapshot? snapshot = null;
        try
        {
            snapshot = await CameraParameterSnapshot.CaptureAsync(service, index);

            await RunCaseAsync(report, $"{label} 参数节点读取", true, async () =>
            {
                var nodes = (ICameraParameterNodeProvider)driver;
                string? triggerMode = await nodes.ReadNodeAsync(
                    device.HardwareId,
                    "TriggerMode",
                    "int"
                );
                Ensure(triggerMode != null, "TriggerMode 读取失败");
            });

            await RunCaseAsync(report, $"{label} NodeMap 按需加载", true, async () =>
            {
                var nodeMap = await service.RefreshGenICamNodeMapAsync(index);
                Ensure(nodeMap.Nodes.Count > 0, "NodeMap 未返回节点");
            });

            await RunCaseAsync(report, $"{label} 连续采集 100 帧", true, async () =>
            {
                try
                {
                    await driver.StartCaptureAsync(device.HardwareId);
                    for (int i = 0; i < 100; i++)
                    {
                        CameraDriverFrame frame = await driver.GrabFrameAsync(
                            device.HardwareId,
                            8000
                        );
                        ValidateFrame(frame);
                    }
                }
                finally
                {
                    await SafeStopAsync(driver, device.HardwareId);
                }
            });

            await RunCaseAsync(report, $"{label} 快照 20 次", true, async () =>
            {
                try
                {
                    await driver.StartCaptureAsync(device.HardwareId);
                    for (int i = 0; i < 20; i++)
                    {
                        byte[] image = await driver.GrabJpegAsync(device.HardwareId, 8000);
                        Ensure(image.Length > 128, $"第 {i + 1} 次快照为空");
                    }
                }
                finally
                {
                    await SafeStopAsync(driver, device.HardwareId);
                }
            });

            if (device.Capabilities.HasFlag(CameraCapability.SoftwareTrigger))
            {
                await RunCaseAsync(report, $"{label} 软件触发 20 次", true, async () =>
                {
                    await service.SetGenICamIntAsync(index, "TriggerSource", 1);
                    await service.SetGenICamIntAsync(index, "TriggerMode", 2);
                    try
                    {
                        await driver.StartCaptureAsync(device.HardwareId);
                        for (int i = 0; i < 20; i++)
                        {
                            await driver.SoftwareTriggerAsync(device.HardwareId);
                            CameraDriverFrame frame = await driver.GrabFrameAsync(
                                device.HardwareId,
                                15000
                            );
                            ValidateFrame(frame);
                        }
                    }
                    finally
                    {
                        await SafeStopAsync(driver, device.HardwareId);
                    }
                });
            }
        }
        finally
        {
            await SafeStopAsync(driver, device.HardwareId);
            if (snapshot != null)
            {
                try
                {
                    await snapshot.RestoreAsync(service, index);
                    report.Notes.Add($"{label} 参数已恢复");
                }
                catch (Exception ex)
                {
                    report.Cases.Add(
                        RegressionCaseResult.Failed(
                            $"{label} 参数恢复",
                            true,
                            TimeSpan.Zero,
                            ex
                        )
                    );
                }
            }
            await SafeStopAndCloseAsync(driver, device.HardwareId);
        }
    }

    private static async Task RunCaseAsync(
        RegressionReport report,
        string name,
        bool blocking,
        Func<Task> action
    )
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await action();
            report.Cases.Add(RegressionCaseResult.Succeeded(name, blocking, stopwatch.Elapsed));
            Console.WriteLine($"[PASS] {name} ({stopwatch.Elapsed.TotalSeconds:F2}s)");
        }
        catch (Exception ex)
        {
            report.Cases.Add(RegressionCaseResult.Failed(name, blocking, stopwatch.Elapsed, ex));
            Console.WriteLine($"[FAIL] {name}: {ex.Message}");
        }
    }

    private static async Task SafeStopAndCloseAsync(ICameraDriver driver, string hardwareId)
    {
        await SafeStopAsync(driver, hardwareId);
        if (driver.IsOpen(hardwareId))
            await driver.CloseAsync(hardwareId);
    }

    private static async Task SafeStopAsync(ICameraDriver driver, string hardwareId)
    {
        if (driver.IsCapturing(hardwareId))
            await driver.StopCaptureAsync(hardwareId);
    }

    private static int GetIndex(ICameraDriver driver, string hardwareId) =>
        driver.TryGetRuntimeIndex(hardwareId, out int index)
            ? index
            : throw new InvalidOperationException($"无法解析运行期索引：{Mask(hardwareId)}");

    private static void ValidateFrame(CameraDriverFrame frame)
    {
        Ensure(frame.Width > 0 && frame.Height > 0, "帧尺寸无效");
        Ensure(frame.Data.Length > 0, "帧数据为空");
    }

    private static string FrameSignature(CameraDriverFrame frame)
    {
        byte[] digest = SHA256.HashData(frame.Data.AsSpan(0, Math.Min(frame.Data.Length, 65536)));
        return $"{frame.Width}x{frame.Height}/{Convert.ToHexString(digest)[..12]}";
    }

    private static RegressionDeviceResult ToDeviceResult(CameraDiscovery device) =>
        new()
        {
            DriverId = device.DriverId,
            Model = device.Model,
            HardwareIdMasked = Mask(device.HardwareId),
            HardwareIdHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(device.HardwareId))
            )[..16],
            RuntimeIndex = device.RuntimeIndex,
            ConnectionSummary = device.ConnectionSummary,
            Capabilities = device.Capabilities.ToString(),
        };

    private static string Mask(string value) =>
        value.Length <= 4 ? "****" : $"***{value[^4..]}";

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static string? GetOption(string[] args, string name)
    {
        int index = Array.FindIndex(
            args,
            x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)
        );
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static async Task WriteReportAsync(string directory, RegressionReport report)
    {
        string stamp = report.StartedAt.ToString("yyyyMMdd-HHmmss");
        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(
            Path.Combine(directory, $"tucam-regression-{stamp}.json"),
            JsonSerializer.Serialize(report, jsonOptions),
            Encoding.UTF8
        );

        var text = new StringBuilder()
            .AppendLine("# Tucam 双相机硬件回归")
            .AppendLine()
            .AppendLine($"- 开始：{report.StartedAt:O}")
            .AppendLine($"- 结束：{report.FinishedAt:O}")
            .AppendLine($"- 机器：{report.MachineName}")
            .AppendLine($"- 程序集：{report.AssemblyName} {report.AssemblyVersion}")
            .AppendLine($"- 结论：{(report.Passed ? "通过" : "阻断")}")
            .AppendLine()
            .AppendLine("## 用例");
        foreach (RegressionCaseResult item in report.Cases)
            text.AppendLine(
                $"- [{(item.Passed ? "x" : " ")}] {item.Name}，{item.DurationMs}ms"
                    + (item.Error == null ? string.Empty : $"；{item.Error}")
            );
        text.AppendLine().AppendLine("## 备注");
        foreach (string note in report.Notes)
            text.AppendLine($"- {note}");
        await File.WriteAllTextAsync(
            Path.Combine(directory, $"tucam-regression-{stamp}.md"),
            text.ToString(),
            Encoding.UTF8
        );
    }

    private static void PrintSummary(RegressionReport report, string outputDirectory)
    {
        Console.WriteLine();
        Console.WriteLine(
            $"回归结论：{(report.Passed ? "PASS" : "BLOCKED")}，报告目录：{Path.GetFullPath(outputDirectory)}"
        );
    }

    private sealed class CameraParameterSnapshot
    {
        public required long TriggerMode { get; init; }
        public required long TriggerSource { get; init; }
        public required double ExposureTime { get; init; }
        public required double GlobalGain { get; init; }

        public static async Task<CameraParameterSnapshot> CaptureAsync(
            ITucamCameraService service,
            int index
        ) =>
            new()
            {
                TriggerMode = await service.GetGenICamIntAsync(index, "TriggerMode"),
                TriggerSource = await service.GetGenICamIntAsync(index, "TriggerSource"),
                ExposureTime = await service.GetPropertyValueAsync(
                    index,
                    TUCamIdProp.ExposureTime
                ),
                GlobalGain = await service.GetPropertyValueAsync(index, TUCamIdProp.GlobalGain),
            };

        public async Task RestoreAsync(ITucamCameraService service, int index)
        {
            await service.SetGenICamIntAsync(index, "TriggerSource", TriggerSource);
            await service.SetGenICamIntAsync(index, "TriggerMode", TriggerMode);
            await service.SetPropertyValueAsync(index, TUCamIdProp.ExposureTime, ExposureTime);
            await service.SetPropertyValueAsync(index, TUCamIdProp.GlobalGain, GlobalGain);
        }
    }
}

internal sealed class RegressionReport
{
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
    public string MachineName { get; set; } = string.Empty;
    public string AssemblyName { get; set; } = string.Empty;
    public string AssemblyVersion { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public List<RegressionDeviceResult> Devices { get; } = [];
    public List<RegressionCaseResult> Cases { get; } = [];
    public List<string> Notes { get; } = [];
}

internal sealed class RegressionDeviceResult
{
    public string DriverId { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string HardwareIdMasked { get; set; } = string.Empty;
    public string HardwareIdHash { get; set; } = string.Empty;
    public int RuntimeIndex { get; set; }
    public string? ConnectionSummary { get; set; }
    public string Capabilities { get; set; } = string.Empty;
}

internal sealed record RegressionCaseResult(
    string Name,
    bool Blocking,
    bool Passed,
    long DurationMs,
    string? Error
)
{
    public static RegressionCaseResult Succeeded(string name, bool blocking, TimeSpan duration) =>
        new(name, blocking, true, (long)duration.TotalMilliseconds, null);

    public static RegressionCaseResult Failed(
        string name,
        bool blocking,
        TimeSpan duration,
        Exception exception
    ) => new(name, blocking, false, (long)duration.TotalMilliseconds, exception.ToString());
}
