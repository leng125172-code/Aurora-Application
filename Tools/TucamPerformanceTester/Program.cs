using System.Diagnostics;
using System.Threading.Channels;
using AuroraStruct3D.Cameras.Tucam;
using AuroraStruct3D.Cameras.Tucam.Interop;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace TucamPerformanceTester;

class Program
{
    static async Task Main(string[] args)
    {
        if (args.Any(x => string.Equals(x, "--regression", StringComparison.OrdinalIgnoreCase)))
        {
            using var regressionMutex = new Mutex(
                initiallyOwned: false,
                name: "AuroraStruct3D.TucamPerformanceTester.HardwareRegression"
            );
            bool lockAcquired;
            try
            {
                lockAcquired = regressionMutex.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                lockAcquired = true;
            }

            if (!lockAcquired)
            {
                Console.Error.WriteLine(
                    "已有 Tucam 硬件回归进程正在运行。为避免多个进程同时控制相机，本次执行已拒绝。"
                );
                Environment.ExitCode = 2;
                return;
            }

            try
            {
                Environment.ExitCode = await TucamRegressionRunner.RunAsync(args);
            }
            finally
            {
                regressionMutex.ReleaseMutex();
            }
            return;
        }

        Console.WriteLine("======================================");
        Console.WriteLine("  TUCam 摄像头性能调试工具");
        Console.WriteLine("======================================");
        Console.WriteLine();

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Warning);
        });
        var logger = loggerFactory.CreateLogger<TucamCameraService>();
        var service = new TucamCameraService(logger);

        try
        {
            int cameraCount = await service.InitializeAsync();
            Console.WriteLine($"检测到 {cameraCount} 台相机");

            if (cameraCount == 0)
            {
                Console.WriteLine("未检测到相机，请检查连接");
                return;
            }

            for (int i = 0; i < cameraCount; i++)
            {
                string model = await service.GetModelByIndexAsync(i);
                Console.WriteLine($"  [{i}] {model}");
            }

            Console.WriteLine();
            Console.Write("请选择相机索引 (0-): ");
            int cameraIndex = int.Parse(Console.ReadLine() ?? "0");

            await service.OpenCameraAsync(cameraIndex);
            Console.WriteLine($"相机 {cameraIndex} 已打开");

            await PrintCameraInfo(service, cameraIndex);

            await ConfigureCameraForPerformance(service, cameraIndex);

            await RunPerformanceTests(service, cameraIndex);

            await service.CloseCameraAsync(cameraIndex);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"错误: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        finally
        {
            await service.UninitializeAsync();
        }

        Console.WriteLine();
        Console.WriteLine("按任意键退出...");
        Console.ReadKey();
    }

    static async Task ConfigureCameraForPerformance(ITucamCameraService service, int cameraIndex)
    {
        Console.WriteLine();
        Console.WriteLine("正在配置相机性能参数...");

        try
        {
            await service.SetGenICamIntAsync(cameraIndex, "TriggerMode", 0);
            Console.WriteLine("  ✓ TriggerMode = Off (自由运行模式)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ 无法设置 TriggerMode: {ex.Message}");
        }

        try
        {
            await service.SetGenICamIntAsync(cameraIndex, "ExposureTime", 10000);
            Console.WriteLine("  ✓ ExposureTime = 10000 μs (10ms)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ 无法设置 ExposureTime: {ex.Message}");
        }

        try
        {
            await service.SetGenICamIntAsync(cameraIndex, "AcquisitionMode", 0);
            Console.WriteLine("  ✓ AcquisitionMode = Continuous");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ 无法设置 AcquisitionMode: {ex.Message}");
        }

        try
        {
            await service.SetGenICamFloatAsync(cameraIndex, "AcquisitionFrameRate", 45);
            Console.WriteLine("  ✓ AcquisitionFrameRate = 45 FPS");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ 无法设置 AcquisitionFrameRate: {ex.Message}");
        }

        Console.WriteLine("相机参数配置完成");
    }

    static async Task PrintCameraInfo(ITucamCameraService service, int cameraIndex)
    {
        Console.WriteLine();
        Console.WriteLine("相机信息:");

        var roi = await service.GetRoiAsync(cameraIndex);
        Console.WriteLine($"  分辨率: {roi.nWidth} x {roi.nHeight}");

        var trigger = await service.GetTriggerAsync(cameraIndex);
        Console.WriteLine($"  触发模式: {trigger.nTgrMode}");

        long? frameRate = await service.GetGenICamIntAsync(cameraIndex, "AcquisitionFrameRate");
        Console.WriteLine($"  采集帧率: {frameRate ?? 0} FPS");

        var exposureAttr = await service.GetPropertyAttrAsync(
            cameraIndex,
            TUCamIdProp.ExposureTime
        );
        Console.WriteLine(
            $"  曝光时间范围: {exposureAttr.dbValMin:F0} - {exposureAttr.dbValMax:F0} μs"
        );

        var gainAttr = await service.GetPropertyAttrAsync(cameraIndex, TUCamIdProp.GlobalGain);
        Console.WriteLine($"  增益范围: {gainAttr.dbValMin:F2} - {gainAttr.dbValMax:F2}");

        string? triggerModeStr = await service.GetGenICamStringAsync(cameraIndex, "TriggerMode");
        Console.WriteLine($"  TriggerMode: {triggerModeStr ?? "未知"}");

        string? acquisitionModeStr = await service.GetGenICamStringAsync(
            cameraIndex,
            "AcquisitionMode"
        );
        Console.WriteLine($"  AcquisitionMode: {acquisitionModeStr ?? "未知"}");

        long? exposureTime = await service.GetGenICamIntAsync(cameraIndex, "ExposureTime");
        Console.WriteLine($"  当前曝光时间: {exposureTime ?? 0} μs");
    }

    static async Task RunPerformanceTests(ITucamCameraService service, int cameraIndex)
    {
        Console.WriteLine();
        Console.WriteLine("======================================");
        Console.WriteLine("  性能测试菜单");
        Console.WriteLine("======================================");
        Console.WriteLine("1. 测试1: 纯抓帧性能（不编码）");
        Console.WriteLine("2. 测试2: 抓帧+JPEG编码性能（原始分辨率）");
        Console.WriteLine("3. 测试3: 连续采集模式测试");
        Console.WriteLine("4. 测试4: 图片输出分辨率检测");
        Console.WriteLine("5. 测试5: SignalR推送性能测试");
        Console.WriteLine("6. 测试6: 实时预览模拟（含延迟测量）");
        Console.WriteLine("7. 测试7: SDK底层函数直接测试");
        Console.WriteLine("8. 测试8: 原始分辨率完整测试");
        Console.WriteLine("9. 退出测试");
        Console.WriteLine();

        while (true)
        {
            Console.Write("请选择测试项 (1-9): ");
            string? choice = Console.ReadLine();

            switch (choice)
            {
                case "1":
                    await TestRawGrabPerformance(service, cameraIndex);
                    break;
                case "2":
                    await TestJpegEncodePerformance(service, cameraIndex);
                    break;
                case "3":
                    await TestContinuousCapture(service, cameraIndex);
                    break;
                case "4":
                    await TestImageOutputResolution(service, cameraIndex);
                    break;
                case "5":
                    await TestSignalRPerformance(service, cameraIndex);
                    break;
                case "6":
                    await TestRealTimePreview(service, cameraIndex);
                    break;
                case "7":
                    await TestSdkDirectPerformance(service, cameraIndex);
                    break;
                case "8":
                    await Test2448x2048Resolution(service, cameraIndex);
                    break;
                case "9":
                    return;
                default:
                    Console.WriteLine("无效选项，请重新输入");
                    break;
            }

            Console.WriteLine();
            Console.WriteLine("按任意键继续...");
            Console.ReadKey();
            Console.WriteLine();
        }
    }

    static async Task TestRawGrabPerformance(ITucamCameraService service, int cameraIndex)
    {
        Console.WriteLine();
        Console.WriteLine("测试1: 纯抓帧性能（不编码）");
        Console.WriteLine("----------------------------");

        await service.StartCaptureAsync(cameraIndex);

        const int frameCount = 100;
        var durations = new long[frameCount];
        var frameIndices = new int[frameCount];

        for (int i = 0; i < frameCount; i++)
        {
            long start = Stopwatch.GetTimestamp();
            var frameData = await service.GrabFrameAsync(cameraIndex);
            long end = Stopwatch.GetTimestamp();

            durations[i] = Stopwatch.GetElapsedTime(start, end).Ticks / 10000;
            frameIndices[i] = (int)frameData.FrameIndex;

            if ((i + 1) % 10 == 0)
            {
                Console.Write($"\r进度: {i + 1}/{frameCount}");
            }
        }
        Console.WriteLine();

        await service.StopCaptureAsync(cameraIndex);

        AnalyzeResults(durations, "纯抓帧");

        int droppedFrames = 0;
        for (int i = 1; i < frameIndices.Length; i++)
        {
            if (frameIndices[i] - frameIndices[i - 1] > 1)
            {
                droppedFrames += frameIndices[i] - frameIndices[i - 1] - 1;
            }
        }
        Console.WriteLine($"丢帧数: {droppedFrames}");
    }

    static async Task TestJpegEncodePerformance(ITucamCameraService service, int cameraIndex)
    {
        Console.WriteLine();
        Console.WriteLine("测试2: 抓帧+JPEG编码性能（原始分辨率）");
        Console.WriteLine("----------------------------");

        await service.StartCaptureAsync(cameraIndex);

        const int frameCount = 50;
        var durations = new long[frameCount];
        var jpegSizes = new int[frameCount];

        for (int i = 0; i < frameCount; i++)
        {
            long start = Stopwatch.GetTimestamp();
            var result = await service.GrabFrameRawAsync(
                cameraIndex,
                maxWidth: 0, // 0 表示原始分辨率，不缩放
                jpegQuality: 75
            );
            long end = Stopwatch.GetTimestamp();

            durations[i] = Stopwatch.GetElapsedTime(start, end).Ticks / 10000;
            jpegSizes[i] = result.Length;

            Console.Write($"\r进度: {i + 1}/{frameCount}");
        }
        Console.WriteLine();

        await service.StopCaptureAsync(cameraIndex);

        AnalyzeResults(durations, "抓帧+JPEG编码（原始分辨率）");
        Console.WriteLine(
            $"JPEG平均大小: {jpegSizes.Average():F0} bytes ({jpegSizes.Average() / 1024:F1} KB)"
        );
        Console.WriteLine($"JPEG最大大小: {jpegSizes.Max() / 1024:F1} KB");
        Console.WriteLine($"JPEG最小大小: {jpegSizes.Min() / 1024:F1} KB");
    }

    static async Task TestContinuousCapture(ITucamCameraService service, int cameraIndex)
    {
        Console.WriteLine();
        Console.WriteLine("测试3: 连续采集模式测试");
        Console.WriteLine("----------------------------");
        Console.Write("测试时长（秒）: ");
        int durationSeconds = int.Parse(Console.ReadLine() ?? "10");

        await service.StartCaptureAsync(cameraIndex);

        int frameCount = 0;
        long startTime = Stopwatch.GetTimestamp();

        Console.WriteLine("采集开始...");
        Console.WriteLine("每秒输出一次统计:");

        for (int second = 0; second < durationSeconds; second++)
        {
            int framesThisSecond = 0;
            long secondStart = Stopwatch.GetTimestamp();

            while (Stopwatch.GetElapsedTime(secondStart, Stopwatch.GetTimestamp()).TotalSeconds < 1)
            {
                try
                {
                    await service.GrabFrameAsync(cameraIndex, timeoutMs: 50);
                    frameCount++;
                    framesThisSecond++;
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("Timeout"))
                {
                    break;
                }
            }

            Console.WriteLine($"  第 {second + 1} 秒: {framesThisSecond} 帧");
        }

        long endTime = Stopwatch.GetTimestamp();
        double elapsedSeconds = Stopwatch.GetElapsedTime(startTime, endTime).TotalSeconds;

        await service.StopCaptureAsync(cameraIndex);

        Console.WriteLine();
        Console.WriteLine($"总帧数: {frameCount}");
        Console.WriteLine($"总时间: {elapsedSeconds:F2} 秒");
        Console.WriteLine($"平均帧率: {frameCount / elapsedSeconds:F1} FPS");
    }

    static async Task TestImageOutputResolution(ITucamCameraService service, int cameraIndex)
    {
        Console.WriteLine();
        Console.WriteLine("测试4: 图片输出分辨率检测");
        Console.WriteLine("----------------------------");

        await service.StartCaptureAsync(cameraIndex);

        // 获取原始分辨率
        var roi = await service.GetRoiAsync(cameraIndex);
        Console.WriteLine($"相机原始分辨率: {roi.nWidth} x {roi.nHeight}");

        // 测试不同的maxWidth值
        int[] maxWidthValues = new[] { 0, 2448, 1280, 960, 640 };

        foreach (int maxWidth in maxWidthValues)
        {
            Console.WriteLine();
            Console.WriteLine($"测试 maxWidth = {(maxWidth == 0 ? "原始分辨率" : maxWidth)}");

            try
            {
                var jpegFrame = await service.GrabFrameRawAsync(
                    cameraIndex,
                    maxWidth: maxWidth,
                    jpegQuality: 75
                );

                // 使用SkiaSharp解码JPEG来获取实际分辨率
                using var stream = new MemoryStream(jpegFrame);
                using var codec = SKCodec.Create(stream);
                Console.WriteLine($"  JPEG文件大小: {jpegFrame.Length / 1024:F1} KB");
                Console.WriteLine($"  解码后实际分辨率: {codec.Info.Width} x {codec.Info.Height}");

                if (maxWidth > 0)
                {
                    double scale = (double)codec.Info.Width / roi.nWidth;
                    Console.WriteLine($"  缩放比例: {scale:P2}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  测试失败: {ex.Message}");
            }
        }

        await service.StopCaptureAsync(cameraIndex);
    }

    static async Task TestSignalRPerformance(ITucamCameraService service, int cameraIndex)
    {
        Console.WriteLine();
        Console.WriteLine("测试5: SignalR推送性能测试");
        Console.WriteLine("----------------------------");

        Console.Write(
            "请输入SignalR Hub服务器地址 (例如: http://localhost:5000/signalr-hubs/camera): "
        );
        string? serverUrl = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            Console.WriteLine("未提供服务器地址，跳过测试");
            return;
        }

        Console.Write("请输入相机ID (GUID): ");
        string? cameraIdStr = Console.ReadLine();
        if (
            string.IsNullOrWhiteSpace(cameraIdStr) || !Guid.TryParse(cameraIdStr, out Guid cameraId)
        )
        {
            Console.WriteLine("无效的相机ID");
            return;
        }

        Console.Write("测试帧数: ");
        int frameCount = int.Parse(Console.ReadLine() ?? "100");

        Console.WriteLine("正在连接SignalR...");

        var connection = new HubConnectionBuilder().WithUrl(serverUrl).Build();

        var frameReceiveTimes = new List<long>();
        var frameReceived = new TaskCompletionSource<bool>();
        var frameCountReceived = 0;
        object lockObj = new();

        // 注册接收回调
        connection.On<string, byte[]>(
            "ReceiveCameraFrameAsync",
            (receivedCameraId, frame) =>
            {
                if (receivedCameraId == cameraId.ToString())
                {
                    long receiveTime = Stopwatch.GetTimestamp();
                    lock (lockObj)
                    {
                        frameReceiveTimes.Add(receiveTime);
                        frameCountReceived++;
                        if (frameCountReceived >= frameCount)
                        {
                            frameReceived.TrySetResult(true);
                        }
                    }
                }
            }
        );

        try
        {
            await connection.StartAsync();
            Console.WriteLine("SignalR连接已建立");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SignalR连接失败: {ex.Message}");
            return;
        }

        await service.StartCaptureAsync(cameraIndex);

        Console.WriteLine("开始性能测试...");

        var sendTimes = new List<long>();
        var sendDurations = new List<long>();

        for (int i = 0; i < frameCount; i++)
        {
            try
            {
                long sendStart = Stopwatch.GetTimestamp();

                var jpegFrame = await service.GrabFrameRawAsync(
                    cameraIndex,
                    maxWidth: 0,
                    jpegQuality: 75
                );

                // 记录发送时间
                sendTimes.Add(sendStart);

                long sendEnd = Stopwatch.GetTimestamp();
                sendDurations.Add(Stopwatch.GetElapsedTime(sendStart, sendEnd).Ticks / 10000);

                if ((i + 1) % 10 == 0)
                {
                    Console.Write($"\r已发送: {i + 1}/{frameCount}");
                }

                // 简单的频率控制，避免发送太快
                await Task.Delay(10);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n发送帧 {i} 时出错: {ex.Message}");
            }
        }
        Console.WriteLine();

        // 等待接收完成，最多30秒
        Console.WriteLine("等待接收完成...");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await frameReceived.Task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("等待超时");
        }

        await service.StopCaptureAsync(cameraIndex);
        await connection.StopAsync();
        await connection.DisposeAsync();

        // 分析结果
        Console.WriteLine();
        Console.WriteLine("测试结果:");
        Console.WriteLine($"  发送帧数: {sendTimes.Count}");
        Console.WriteLine($"  接收帧数: {frameReceiveTimes.Count}");
        Console.WriteLine(
            $"  丢帧率: {(1 - (double)frameReceiveTimes.Count / sendTimes.Count):P2}"
        );

        if (sendDurations.Count > 0)
        {
            Console.WriteLine($"  平均发送耗时: {sendDurations.Average():F1} ms");
            Console.WriteLine($"  最大发送耗时: {sendDurations.Max()} ms");
            Console.WriteLine($"  最小发送耗时: {sendDurations.Min()} ms");
        }

        // 尝试计算延迟（如果接收顺序和发送顺序一致）
        if (sendTimes.Count > 0 && frameReceiveTimes.Count > 0)
        {
            int minCount = Math.Min(sendTimes.Count, frameReceiveTimes.Count);
            var latencies = new List<long>();
            for (int i = 0; i < minCount; i++)
            {
                long latency = (
                    Stopwatch.GetElapsedTime(sendTimes[i], frameReceiveTimes[i]).Ticks / 10000
                );
                latencies.Add(Math.Max(0, latency));
            }
            Console.WriteLine($"  平均延迟: {latencies.Average():F1} ms");
            Console.WriteLine($"  最大延迟: {latencies.Max()} ms");
            Console.WriteLine($"  最小延迟: {latencies.Min()} ms");
        }
    }

    static async Task TestRealTimePreview(ITucamCameraService service, int cameraIndex)
    {
        Console.WriteLine();
        Console.WriteLine("测试6: 实时预览模拟（含延迟测量）");
        Console.WriteLine("----------------------------");
        Console.Write("测试时长（秒）: ");
        int durationSeconds = int.Parse(Console.ReadLine() ?? "10");

        await service.StartCaptureAsync(cameraIndex);

        int frameCount = 0;
        long startTime = Stopwatch.GetTimestamp();
        long signalRInterval = 1000 / 30;
        long lastSignalRSent = 0;
        var frameDelays = new List<long>();

        Console.WriteLine("模拟预览开始...");
        Console.WriteLine("格式: [帧数] 耗时=xxx ms, 延迟=xxx ms, FPS=xx.x");

        for (int second = 0; second < durationSeconds; second++)
        {
            for (int i = 0; i < 60; i++)
            {
                long iterStart = Stopwatch.GetTimestamp();

                try
                {
                    var result = await service.GrabFrameRawAsync(
                        cameraIndex,
                        maxWidth: 0, // 原始分辨率
                        jpegQuality: 75
                    );
                    frameCount++;

                    long now =
                        Stopwatch.GetElapsedTime(startTime, Stopwatch.GetTimestamp()).Ticks / 10000;
                    frameDelays.Add(
                        Stopwatch.GetElapsedTime(iterStart, Stopwatch.GetTimestamp()).Ticks / 10000
                    );

                    if (now - lastSignalRSent >= signalRInterval)
                    {
                        double fps = 1000.0 / frameDelays.Last();
                        Console.WriteLine(
                            $"[{frameCount}] 耗时={frameDelays.Last():F0}ms, 延迟={now - lastSignalRSent:F0}ms, FPS={fps:F1}"
                        );
                        lastSignalRSent = now;
                    }
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("Timeout"))
                {
                    // ignore
                }

                Thread.Sleep(1);
            }
        }

        long endTime = Stopwatch.GetTimestamp();
        double elapsedSeconds = Stopwatch.GetElapsedTime(startTime, endTime).TotalSeconds;

        await service.StopCaptureAsync(cameraIndex);

        Console.WriteLine();
        Console.WriteLine("统计结果:");
        Console.WriteLine($"总帧数: {frameCount}");
        Console.WriteLine($"总时间: {elapsedSeconds:F2} 秒");
        Console.WriteLine($"平均帧率: {frameCount / elapsedSeconds:F1} FPS");
        Console.WriteLine($"平均帧耗时: {frameDelays.Average():F1} ms");
        Console.WriteLine($"最大帧耗时: {frameDelays.Max()} ms");
        Console.WriteLine($"最小帧耗时: {frameDelays.Min()} ms");

        Console.WriteLine();
        Console.WriteLine("优化建议:");
        double avgDelay = frameDelays.Average();
        if (avgDelay > 50)
        {
            Console.WriteLine("  ⚠️  帧耗时较高，建议:");
            Console.WriteLine("    - 检查曝光时间是否过长");
        }
        else if (avgDelay > 30)
        {
            Console.WriteLine("  ⚠️  帧耗时中等，建议:");
            Console.WriteLine("    - 确保使用连续采集模式");
        }
        else
        {
            Console.WriteLine("  ✅  帧耗时正常");
        }
    }

    static async Task TestSdkDirectPerformance(ITucamCameraService service, int cameraIndex)
    {
        Console.WriteLine();
        Console.WriteLine("测试7: SDK底层函数直接测试");
        Console.WriteLine("----------------------------");
        Console.WriteLine("此测试直接调用SDK的WaitForFrame，跳过所有额外处理");

        await service.StartCaptureAsync(cameraIndex);

        const int frameCount = 100;
        var durations = new long[frameCount];

        for (int i = 0; i < frameCount; i++)
        {
            long start = Stopwatch.GetTimestamp();
            await service.GrabFrameAsync(cameraIndex);
            long end = Stopwatch.GetTimestamp();

            durations[i] = Stopwatch.GetElapsedTime(start, end).Ticks / 10000;

            if ((i + 1) % 10 == 0)
            {
                Console.Write($"\r进度: {i + 1}/{frameCount}");
            }
        }
        Console.WriteLine();

        await service.StopCaptureAsync(cameraIndex);

        AnalyzeResults(durations, "SDK直接调用");
    }

    static async Task Test2448x2048Resolution(ITucamCameraService service, int cameraIndex)
    {
        Console.WriteLine();
        Console.WriteLine("测试8: 原始分辨率完整测试");
        Console.WriteLine("----------------------------");

        await service.StartCaptureAsync(cameraIndex);

        const int frameCount = 30;
        var durations = new long[frameCount];
        var jpegSizes = new int[frameCount];

        for (int i = 0; i < frameCount; i++)
        {
            long start = Stopwatch.GetTimestamp();
            var result = await service.GrabFrameRawAsync(cameraIndex, maxWidth: 0, jpegQuality: 75);
            long end = Stopwatch.GetTimestamp();

            durations[i] = Stopwatch.GetElapsedTime(start, end).Ticks / 10000;
            jpegSizes[i] = result.Length;

            if ((i + 1) % 5 == 0)
            {
                Console.Write($"\r进度: {i + 1}/{frameCount}");
            }
        }
        Console.WriteLine();

        await service.StopCaptureAsync(cameraIndex);

        AnalyzeResults(durations, "原始分辨率完整测试");
        Console.WriteLine(
            $"JPEG平均大小: {jpegSizes.Average():F0} bytes ({jpegSizes.Average() / 1024:F1} KB)"
        );
        Console.WriteLine($"JPEG最大大小: {jpegSizes.Max() / 1024:F1} KB");
        Console.WriteLine($"JPEG最小大小: {jpegSizes.Min() / 1024:F1} KB");
    }

    static void AnalyzeResults(long[] durations, string testName)
    {
        double avgDuration = durations.Average();
        double minDuration = durations.Min();
        double maxDuration = durations.Max();
        double avgFps = 1000.0 / avgDuration;

        Console.WriteLine();
        Console.WriteLine($"【{testName}】性能分析");
        Console.WriteLine($"----------------------------");
        Console.WriteLine($"测试帧数: {durations.Length}");
        Console.WriteLine($"平均耗时: {avgDuration:F1} ms");
        Console.WriteLine($"最小耗时: {minDuration:F1} ms");
        Console.WriteLine($"最大耗时: {maxDuration:F1} ms");
        Console.WriteLine($"平均帧率: {avgFps:F1} FPS");
        Console.WriteLine($"抖动率:   {(maxDuration - minDuration) / avgDuration * 100:F1} %");

        double p95 = durations.OrderBy(d => d).ElementAt((int)(durations.Length * 0.95));
        double p99 = durations.OrderBy(d => d).ElementAt((int)(durations.Length * 0.99));
        Console.WriteLine($"P95耗时:  {p95:F1} ms");
        Console.WriteLine($"P99耗时:  {p99:F1} ms");

        Console.WriteLine();
        Console.WriteLine("性能评估:");
        if (avgFps >= 30)
        {
            Console.WriteLine("  ✅ 优秀 - 满足实时预览需求");
        }
        else if (avgFps >= 15)
        {
            Console.WriteLine("  ⚠️  中等 - 可用于预览，但可能有延迟");
        }
        else
        {
            Console.WriteLine("  ❌ 较差 - 需要优化");
        }
    }
}
