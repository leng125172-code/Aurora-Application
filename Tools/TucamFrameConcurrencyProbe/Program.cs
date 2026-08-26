using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FringeTool;

const int Success = 1;
Options options = Options.Parse(args);

Console.WriteLine("TUCam 帧并发诊断工具");
Console.WriteLine($"mode={options.Mode}, cameras={options.CameraCount}, frames={options.Frames}, timeout={options.TimeoutMs}ms, " +
    $"cameraMultiple={(options.CameraMultiple > 0 ? options.CameraMultiple : "read-only")}, usbOnly={options.UsbOnly}");

if (options.Mode == TestMode.ProjectorLight)
    return await RunProjectorLight(options);
if (options.Mode == TestMode.ProjectorSequence)
    return await RunProjectorSequence(options);

if (options.Mode == TestMode.SameCameraUnsafe && !options.AllowUnsafe)
{
    Console.Error.WriteLine("same-camera-unsafe 可能导致厂商库 native 崩溃；如需复现，请显式添加 --allow-unsafe。");
    return 64;
}

string configPath = Path.GetFullPath(options.ConfigPath);
IntPtr configPtr = Marshal.StringToHGlobalAnsi(configPath);
var init = new TUCamInit { pstrConfigPath = configPtr };
var cameras = new List<CameraContext>();
bool sdkInitialized = false;
try
{
    if (options.UsbOnly)
    {
        int configRet = Native.TUCAM_Api_Config(0, 1); // TU_CAMERA_TYPE_CONFIG, TU_USB
        Console.WriteLine($"TUCAM_Api_Config(cameraType=USB)={Hex(configRet)}");
        if (configRet != Success)
            return Fail($"USB-only API 配置失败: {Hex(configRet)}");
    }

    int ret = Native.TUCAM_Api_Init(ref init, options.TimeoutMs);
    if (ret != Success)
        return Fail($"TUCAM_Api_Init 失败: {Hex(ret)}");
    sdkInitialized = true;

    Console.WriteLine($"SDK 检测到 {init.uiCamCount} 台相机，hostCameraIndex={init.uiHostCamIdx}，配置目录：{configPath}");
    int wanted = options.Mode == TestMode.SameCameraUnsafe ? 1 : options.CameraCount;
    if (init.uiCamCount < wanted)
        return Fail($"至少需要 {wanted} 台相机，当前只有 {init.uiCamCount} 台。");

    for (int i = 0; i < wanted; i++)
    {
        int deviceIndex = options.CameraOffset + i;
        var open = new TUCamOpen { uiIdxOpen = (uint)deviceIndex };
        ret = Native.TUCAM_Dev_Open(ref open);
        if (ret != Success)
            return Fail($"相机 {deviceIndex} Dev_Open 失败: {Hex(ret)}");

        var context = new CameraContext(deviceIndex, open.hIdxTUCam);
        cameras.Add(context);
        ProbeCameraMultiple(context, options.CameraMultiple);
        if (options.Mode is TestMode.ProjectorHardware or TestMode.ProjectorHardwareSerial or TestMode.CameraHardwareWait)
        {
            int modeRet = SetGenICamInt(context.Handle, "TriggerMode", 1);
            Console.WriteLine($"相机 {deviceIndex} 外触发配置：TriggerMode=Standard(1) ret={Hex(modeRet)}");
            if (modeRet != Success)
                return Fail($"相机 {deviceIndex} TriggerMode 设置失败。");
        }
        else if (options.Mode == TestMode.StereoDiagnostic)
        {
            int modeRet = SetGenICamInt(context.Handle, "TriggerMode", 0);
            Console.WriteLine($"相机 {deviceIndex} 质量诊断配置：TriggerMode=FreeRunning(0) ret={Hex(modeRet)}");
            if (modeRet != Success)
                return Fail($"相机 {deviceIndex} TriggerMode 恢复 FreeRunning 失败。");
        }

        ret = Native.TUCAM_Buf_Alloc(context.Handle, ref context.Frame);
        if (ret != Success)
            return Fail($"相机 {deviceIndex} Buf_Alloc 失败: {Hex(ret)}");
        context.Allocated = true;

        if (options.Mode != TestMode.StereoDiagnostic)
        {
            uint captureMode = options.Mode is TestMode.ProjectorHardware or TestMode.ProjectorHardwareSerial or TestMode.CameraHardwareWait ? 1u : 4u;
            ret = Native.TUCAM_Cap_Start(context.Handle, captureMode);
            if (ret != Success)
                return Fail($"相机 {deviceIndex} Cap_Start 失败: {Hex(ret)}");
            context.Started = true;
            ExecuteCommand(context.Handle, "AcquisitionStart", optional: true);
            Console.WriteLine($"相机 {deviceIndex} 已打开并启动，handle=0x{context.Handle:X}");
        }
        else
        {
            Console.WriteLine($"相机 {deviceIndex} 已打开并分配缓冲区，等待按主→从顺序启动，handle=0x{context.Handle:X}");
        }
    }

    Directory.CreateDirectory(options.OutputDirectory);
    return options.Mode switch
    {
        TestMode.DualSafe => await RunDualSafe(cameras, options),
        TestMode.AlternatingSafe => await RunAlternatingSafe(cameras, options),
        TestMode.StereoDiagnostic => await RunStereoDiagnostic(cameras, options),
        TestMode.ProjectorHardware => await RunProjectorHardware(cameras, options),
        TestMode.ProjectorHardwareSerial => await RunProjectorHardwareSerial(cameras, options),
        TestMode.CameraHardwareWait => RunCameraHardwareWait(cameras, options),
        TestMode.SameCameraUnsafe => await RunSameCameraUnsafe(cameras[0], options),
        _ => throw new ArgumentOutOfRangeException(),
    };
}
catch (DllNotFoundException ex)
{
    return Fail($"未找到 TUCam 动态库：{ex.Message}");
}
catch (Exception ex)
{
    return Fail(ex.ToString());
}
finally
{
    foreach (CameraContext camera in cameras)
    {
        if (camera.Started)
        {
            ExecuteCommand(camera.Handle, "AcquisitionStop", optional: true);
            Native.TUCAM_Buf_AbortWait(camera.Handle);
            Native.TUCAM_Cap_Stop(camera.Handle);
        }
        if (camera.Allocated)
            Native.TUCAM_Buf_Release(camera.Handle);
        if (camera.Handle != IntPtr.Zero)
            Native.TUCAM_Dev_Close(camera.Handle);
    }
    if (sdkInitialized)
        Native.TUCAM_Api_Uninit();
    Marshal.FreeHGlobal(configPtr);
}

static async Task<int> RunDualSafe(List<CameraContext> cameras, Options options)
{
    Console.WriteLine("\n[dual-safe] 两台相机对象独立；统一触发后并行 WaitForFrame 和立即复制。\n");
    var errors = new ConcurrentBag<string>();
    for (int shot = 0; shot < options.Frames; shot++)
    {
        var triggerTimes = new List<(int Camera, long Tick)>();
        foreach (CameraContext camera in cameras)
        {
            int ret = ExecuteCommand(camera.Handle, "TriggerSoftwarePulse");
            triggerTimes.Add((camera.Index, Stopwatch.GetTimestamp()));
            if (ret != Success)
                errors.Add($"shot={shot} cam={camera.Index} trigger={Hex(ret)}");
        }

        FrameResult[] results = await Task.WhenAll(cameras.Select(camera =>
            Task.Run(() => WaitAndCopy(camera, shot, options))));

        double triggerSkewMs = triggerTimes.Count < 2 ? 0 :
            (triggerTimes.Max(x => x.Tick) - triggerTimes.Min(x => x.Tick)) * 1000.0 / Stopwatch.Frequency;
        Console.WriteLine($"shot={shot:D4} triggerSkew={triggerSkewMs:F3}ms");
        foreach (FrameResult result in results.OrderBy(x => x.CameraIndex))
        {
            Console.WriteLine(result.ToLogLine());
            if (!result.Success)
                errors.Add(result.ToLogLine());
        }
    }

    Console.WriteLine($"\n完成：总帧={options.Frames * cameras.Count}，异常={errors.Count}");
    foreach (string error in errors.Take(20))
        Console.WriteLine($"  {error}");
    return errors.IsEmpty ? 0 : 2;
}

static async Task<int> RunSameCameraUnsafe(CameraContext camera, Options options)
{
    Console.WriteLine("\n[same-camera-unsafe] 两个线程向同一 handle、同一 TUCamFrame 同时 WaitForFrame。仅用于复现。\n");
    int failures = 0;
    for (int shot = 0; shot < options.Frames; shot++)
    {
        ExecuteCommand(camera.Handle, "TriggerSoftwarePulse");
        ExecuteCommand(camera.Handle, "TriggerSoftwarePulse");
        using var gate = new ManualResetEventSlim(false);
        Task<FrameResult> a = Task.Run(() => { gate.Wait(); return WaitAndCopyUnsafe(camera, shot, "A", options); });
        Task<FrameResult> b = Task.Run(() => { gate.Wait(); return WaitAndCopyUnsafe(camera, shot, "B", options); });
        gate.Set();
        FrameResult[] results = await Task.WhenAll(a, b);
        foreach (FrameResult result in results)
        {
            Console.WriteLine(result.ToLogLine());
            if (!result.Success) failures++;
        }
    }
    return failures == 0 ? 0 : 2;
}

static Task<int> RunAlternatingSafe(List<CameraContext> cameras, Options options)
{
    Console.WriteLine("\n[alternating-safe] 严格按相机 1→2→1→2 触发、等待并复制。\n");
    int failures = 0;
    int switchCount = checked(options.Frames * cameras.Count);
    for (int sequence = 0; sequence < switchCount; sequence++)
    {
        CameraContext camera = cameras[sequence % cameras.Count];
        var triggerSw = Stopwatch.StartNew();
        int triggerRet = ExecuteCommand(camera.Handle, "TriggerSoftwarePulse");
        FrameResult result = triggerRet == Success
            ? WaitAndCopy(camera, sequence, options)
            : FrameResult.Error(camera.Index, sequence, "alternating", triggerRet, triggerSw.Elapsed.TotalMilliseconds);
        Console.WriteLine($"sequence={sequence:D4} expectedCam={camera.Index} {result.ToLogLine()}");
        if (!result.Success) failures++;
    }
    Console.WriteLine($"\n完成：切换={switchCount}，异常={failures}");
    return Task.FromResult(failures == 0 ? 0 : 2);
}

static Task<int> RunStereoDiagnostic(List<CameraContext> cameras, Options options)
{
    Console.WriteLine("\n[stereo-diagnostic] 按主→从逐台 FreeRunning 取帧，分析曝光并保存左右成对图像。\n");
    options = options with { SaveRaw = true };
    var results = new List<FrameResult>();
    for (int shot = 0; shot < options.Frames; shot++)
    {
        foreach (CameraContext camera in cameras.OrderBy(x => x.Index))
        {
            // 质量诊断不测试同步，使用 Linux so 最稳定的 FreeRunning 路径：主拍完再从拍。
            int startRet = Native.TUCAM_Cap_Start(camera.Handle, 0u);
            camera.Started = startRet == Success;
            FrameResult result = startRet == Success
                ? WaitAndCopy(camera, shot, options)
                : FrameResult.Error(camera.Index, shot, "diagnostic", startRet, 0);
            results.Add(result);
            Console.WriteLine($"shot={shot:D3} {result.ToLogLine()}");
            Native.TUCAM_Buf_AbortWait(camera.Handle);
            if (camera.Started) Native.TUCAM_Cap_Stop(camera.Handle);
            camera.Started = false;
        }
    }

    Console.WriteLine("\n曝光诊断汇总：");
    foreach (IGrouping<int, FrameResult> group in results.Where(x => x.Success).GroupBy(x => x.CameraIndex))
    {
        ExposureMetrics[] values = group.Where(x => x.Exposure is not null).Select(x => x.Exposure!).ToArray();
        if (values.Length == 0) continue;
        double mean = values.Average(x => x.MeanPercent);
        double dark = values.Average(x => x.DarkPercent);
        double saturated = values.Average(x => x.SaturatedPercent);
        string verdict = saturated > 5 ? "严重过曝" : saturated > 1 ? "存在过曝" : dark > 40 ? "明显欠曝" : "曝光基本可用";
        Console.WriteLine($"  cam={group.Key}: mean={mean:F1}% dark={dark:F1}% saturated={saturated:F2}% => {verdict}");
    }
    Console.WriteLine("视角判断：请比较输出目录中同一 shot 的左右 BMP；圆点靶应在两幅图中完整可见并覆盖相近区域。若任一相机缺失靶板边缘或共同区域很小，才属于夹角/视场问题。");
    int failures = results.Count(x => !x.Success);
    Console.WriteLine($"完成：图像={results.Count}，采集异常={failures}，输出={Path.GetFullPath(options.OutputDirectory)}");
    return Task.FromResult(failures == 0 ? 0 : 2);
}

static async Task<int> RunProjectorHardware(List<CameraContext> cameras, Options options)
{
    Console.WriteLine("\n[projector-hardware] 投影仪 T/N 命令产生外触发，两台相机并行等待 TRIG_IN。\n");
    using var projector = new HidProjectorClient(deviceIndex: options.ProjectorIndex);
    await projector.ConnectAsync();
    int failures = 0;
    try
    {
        await projector.SendCommandAsync("B 0");
        await projector.SendCommandAsync("LN");
        await projector.SendCommandAsync("LA 60");
        await Task.Delay(2000);
        if (!await projector.SendCommandAsync("B 2"))
            return Fail("投影仪切换 B 2 单帧触发模式失败。");
        await Task.Delay(500);

        for (int shot = 0; shot < options.Frames; shot++)
        {
            Task<FrameResult>[] waiters = cameras.Select(camera =>
                Task.Run(() => WaitAndCopy(camera, shot, options))).ToArray();
            await Task.Delay(50);
            var commandSw = Stopwatch.StartNew();
            string command = shot == 0 ? "T" : "N";
            bool sent = await projector.SendCommandAsync(command);
            FrameResult[] results = await Task.WhenAll(waiters);
            Console.WriteLine($"shot={shot:D4} projector={command} sent={sent} commandToComplete={commandSw.Elapsed.TotalMilliseconds:F1}ms");
            foreach (FrameResult result in results.OrderBy(x => x.CameraIndex))
            {
                Console.WriteLine(result.ToLogLine());
                if (!sent || !result.Success) failures++;
            }
        }
    }
    finally
    {
        await projector.SendCommandAsync("B 0");
        await projector.DisconnectAsync();
    }
    Console.WriteLine($"\n完成：投影帧={options.Frames}，相机帧={options.Frames * cameras.Count}，异常={failures}");
    return failures == 0 ? 0 : 2;
}

static async Task<int> RunProjectorHardwareSerial(List<CameraContext> cameras, Options options)
{
    Console.WriteLine("\n[projector-hardware-serial] 两台相机先进入硬触发采集；投影仪触发一次后，按 cam0→cam1 串行 Wait/Copy。\n");
    using var projector = new HidProjectorClient(deviceIndex: options.ProjectorIndex);
    await projector.ConnectAsync();
    int failures = 0;
    try
    {
        await projector.SendCommandAsync("B 0");
        await projector.SendCommandAsync("LN");
        await projector.SendCommandAsync("LA 60");
        await Task.Delay(1000);
        if (!await projector.SendCommandAsync("B 2"))
            return Fail("投影仪切换 B 2 单帧触发模式失败。");
        await Task.Delay(500);

        for (int shot = 0; shot < options.Frames; shot++)
        {
            string command = shot == 0 ? "T" : "N";
            var shotSw = Stopwatch.StartNew();
            bool sent = await projector.SendCommandAsync(command);
            Console.WriteLine($"shot={shot:D4} projector={command} sent={sent}");
            if (!sent) { failures += cameras.Count; continue; }

            // 交替读取顺序，用于区分“第二个 Wait 总失败”和“固定某台相机失败”。
            IEnumerable<CameraContext> readOrder = shot % 2 == 0
                ? cameras.OrderBy(x => x.Index)
                : cameras.OrderByDescending(x => x.Index);
            foreach (CameraContext camera in readOrder)
            {
                FrameResult result = WaitAndCopy(camera, shot, options);
                Console.WriteLine(result.ToLogLine());
                if (!result.Success) failures++;
            }
            Console.WriteLine($"shot={shot:D4} pairElapsed={shotSw.Elapsed.TotalMilliseconds:F1}ms");
        }
    }
    finally
    {
        await projector.SendCommandAsync("B 0");
        await projector.DisconnectAsync();
    }
    Console.WriteLine($"\n完成：投影帧={options.Frames}，相机帧={options.Frames * cameras.Count}，异常={failures}");
    return failures == 0 ? 0 : 2;
}

static async Task<int> RunProjectorLight(Options options)
{
    Console.WriteLine("\n[projector-light] 退出条纹播放，使用正式协议开灯并显示纯白。\n");
    using var projector = new HidProjectorClient(deviceIndex: options.ProjectorIndex);
    await projector.ConnectAsync();
    try
    {
        foreach (string command in new[] { "B 0", "LN", "LA 60", "S1" })
        {
            bool sent = await projector.SendCommandAsync(command);
            Console.WriteLine($"projector={command} sent={sent}");
            if (!sent) return Fail($"投影仪命令 {command} 发送失败。");
            await Task.Delay(250);
        }
        Console.WriteLine("开灯指令已全部发送，保持纯白输出。请直接观察投影窗口是否亮起。");
        return 0;
    }
    finally
    {
        await projector.DisconnectAsync();
    }
}

static async Task<int> RunProjectorSequence(Options options)
{
    Console.WriteLine("\n[projector-sequence] 等待相机进程就绪后，以 B2 模式发送 T/N 触发序列。\n");
    using var projector = new HidProjectorClient(deviceIndex: options.ProjectorIndex);
    await projector.ConnectAsync();
    try
    {
        foreach (string command in new[] { "B 0", "LN", "LA 60", "B 2" })
            if (!await projector.SendCommandAsync(command)) return Fail($"投影仪命令 {command} 发送失败。");
        await Task.Delay(2000);
        for (int shot = 0; shot < options.Frames; shot++)
        {
            string command = shot == 0 ? "T" : "N";
            bool sent = await projector.SendCommandAsync(command);
            Console.WriteLine($"shot={shot:D4} projector={command} sent={sent}");
            if (!sent) return 2;
            await Task.Delay(1000);
        }
        return 0;
    }
    finally
    {
        await projector.SendCommandAsync("B 0");
        await projector.DisconnectAsync();
    }
}

static int RunCameraHardwareWait(List<CameraContext> cameras, Options options)
{
    Console.WriteLine("\n[camera-hardware-wait] 单相机独立进程等待投影仪硬触发。\n");
    int failures = 0;
    CameraContext camera = cameras.Single();
    for (int shot = 0; shot < options.Frames; shot++)
    {
        FrameResult result = WaitAndCopy(camera, shot, options);
        Console.WriteLine(result.ToLogLine());
        if (!result.Success) failures++;
    }
    Console.WriteLine($"完成：cam={camera.Index} 帧={options.Frames} 异常={failures}");
    return failures == 0 ? 0 : 2;
}

static FrameResult WaitAndCopy(CameraContext camera, int shot, Options options)
{
    lock (camera.FrameLock)
        return WaitAndCopyCore(camera, ref camera.Frame, shot, "safe", options);
}

static FrameResult WaitAndCopyUnsafe(CameraContext camera, int shot, string consumer, Options options) =>
    WaitAndCopyCore(camera, ref camera.Frame, shot, consumer, options);

static FrameResult WaitAndCopyCore(CameraContext camera, ref TUCamFrame frame, int shot, string consumer, Options options)
{
    var sw = Stopwatch.StartNew();
    int ret = Native.TUCAM_Buf_WaitForFrame(camera.Handle, ref frame, options.TimeoutMs);
    if (ret != Success)
        return FrameResult.Error(camera.Index, shot, consumer, ret, sw.Elapsed.TotalMilliseconds);

    // SDK 缓冲区在下一次 WaitForFrame 时可能失效；数据和元信息必须在此处完成快照。
    ushort width = frame.usWidth;
    ushort height = frame.usHeight;
    uint stride = frame.uiWidthStep;
    uint imageSize = frame.uiImgSize;
    uint sdkFrameIndex = frame.uiIndex;
    IntPtr buffer = frame.pBuffer;
    ushort offset = frame.usOffset;
    if (buffer == IntPtr.Zero || imageSize == 0 || imageSize > 512 * 1024 * 1024)
        return FrameResult.Invalid(camera.Index, shot, consumer, sdkFrameIndex, buffer, width, height, stride, imageSize, sw.Elapsed.TotalMilliseconds);

    byte[] data = new byte[checked((int)imageSize)];
    Marshal.Copy(IntPtr.Add(buffer, offset), data, 0, data.Length);
    ulong hash = Fnv1A64(data);
    bool allSame = data.Length > 0 && data.All(value => value == data[0]);
    ExposureMetrics exposure = AnalyzeExposure(data, width, height, stride);

    if (options.SaveRaw)
    {
        string stem =
            $"cam{camera.Index}_shot{shot:D4}_{consumer}_sdk{sdkFrameIndex}_{hash:X16}.raw";
        string file = Path.Combine(options.OutputDirectory, stem);
        File.WriteAllBytes(file, data);
        SaveRgb16PreviewBmp(Path.ChangeExtension(file, ".bmp"), data, width, height, stride);
    }
    return FrameResult.Ok(camera.Index, shot, consumer, sdkFrameIndex, buffer, width, height, stride, imageSize, hash, allSame, sw.Elapsed.TotalMilliseconds, exposure);
}

static ExposureMetrics AnalyzeExposure(byte[] data, int width, int height, uint stride)
{
    var samples = new List<int>(Math.Max(1, width * height / 64));
    int observedMax = 0;
    for (int y = 0; y < height; y += 8)
    for (int x = 0; x < width; x += 8)
    {
        int p = checked((int)(y * stride) + x * 6);
        if (p + 5 >= data.Length) continue;
        int r = data[p] | data[p + 1] << 8;
        int g = data[p + 2] | data[p + 3] << 8;
        int b = data[p + 4] | data[p + 5] << 8;
        int value = (r + g + b) / 3;
        samples.Add(value);
        observedMax = Math.Max(observedMax, Math.Max(r, Math.Max(g, b)));
    }
    if (samples.Count == 0) return new(0, 0, 0, 0, 0, 0, 0);
    int range = observedMax <= 4095 ? 4095 : 65535;
    samples.Sort();
    int Percentile(double q) => samples[(int)Math.Clamp(Math.Round((samples.Count - 1) * q), 0, samples.Count - 1)];
    double mean = samples.Average() * 100.0 / range;
    double dark = samples.Count(x => x <= range * 0.02) * 100.0 / samples.Count;
    double saturated = samples.Count(x => x >= range * 0.98) * 100.0 / samples.Count;
    return new(mean, Percentile(.01) * 100.0 / range, Percentile(.5) * 100.0 / range,
        Percentile(.99) * 100.0 / range, dark, saturated, samples.Count);
}

static void SaveRgb16PreviewBmp(string path, byte[] data, int width, int height, uint stride)
{
    int outputStride = checked((width * 3 + 3) & ~3);
    int pixelBytes = checked(outputStride * height);
    using var stream = File.Create(path);
    using var writer = new BinaryWriter(stream);
    writer.Write((ushort)0x4D42);
    writer.Write(54 + pixelBytes);
    writer.Write(0);
    writer.Write(54);
    writer.Write(40);
    writer.Write(width);
    writer.Write(height);
    writer.Write((ushort)1);
    writer.Write((ushort)24);
    writer.Write(0);
    writer.Write(pixelBytes);
    writer.Write(2835);
    writer.Write(2835);
    writer.Write(0);
    writer.Write(0);

    byte[] row = new byte[outputStride];
    for (int y = height - 1; y >= 0; y--)
    {
        int sourceRow = checked((int)(y * stride));
        Array.Clear(row);
        for (int x = 0; x < width; x++)
        {
            int source = sourceRow + x * 6;
            ushort r12 = (ushort)(data[source] | data[source + 1] << 8);
            ushort g12 = (ushort)(data[source + 2] | data[source + 3] << 8);
            ushort b12 = (ushort)(data[source + 4] | data[source + 5] << 8);
            int destination = x * 3;
            row[destination] = (byte)Math.Min(255, b12 >> 4);
            row[destination + 1] = (byte)Math.Min(255, g12 >> 4);
            row[destination + 2] = (byte)Math.Min(255, r12 >> 4);
        }
        writer.Write(row);
    }
}

static ulong Fnv1A64(byte[] data)
{
    ulong hash = 14695981039346656037UL;
    foreach (byte value in data)
    {
        hash ^= value;
        hash *= 1099511628211UL;
    }
    return hash;
}

static int ExecuteCommand(IntPtr handle, string name, bool optional = false)
{
    const int elementSize = 128;
    IntPtr element = Marshal.AllocHGlobal(elementSize);
    IntPtr namePtr = Marshal.StringToHGlobalAnsi(name);
    try
    {
        for (int i = 0; i < elementSize; i++) Marshal.WriteByte(element, i, 0);
        int ret = Native.TUCAM_GenICam_ElementAttr(handle, element, namePtr);
        if (ret != Success) return optional ? Success : ret;
        return Native.TUCAM_GenICam_SetElementValue(handle, element);
    }
    finally
    {
        Marshal.FreeHGlobal(namePtr);
        Marshal.FreeHGlobal(element);
    }
}

static int SetGenICamInt(IntPtr handle, string name, long value)
{
    IntPtr namePtr = Marshal.StringToHGlobalAnsi(name);
    try
    {
        var element = new TucamElement { pName = namePtr };
        int ret = Native.TUCAM_GenICam_GetElementValue(handle, ref element, 0);
        if (ret != Success) return ret;
        element.pName = namePtr;
        element.uValue.IntValue.nVal = value;
        return Native.TUCAM_GenICam_SetElementValueRef(handle, ref element, 0);
    }
    finally { Marshal.FreeHGlobal(namePtr); }
}

static void ProbeCameraMultiple(CameraContext camera, int requestedValue)
{
    const int cameraMultipleCapability = 0x17;
    var attr = new TUCamCapaAttr { idCapa = cameraMultipleCapability };
    int attrRet = Native.TUCAM_Capa_GetAttr(camera.Handle, ref attr);
    int current = int.MinValue;
    int getRet = Native.TUCAM_Capa_GetValue(camera.Handle, cameraMultipleCapability, ref current);

    Console.WriteLine(
        $"相机 {camera.Index} CAM_MULTIPLE(0x17)：GetAttr={Hex(attrRet)} " +
        $"range=[{attr.nValMin},{attr.nValMax}] default={attr.nValDft} step={attr.nValStep}；" +
        $"GetValue={Hex(getRet)} value={(getRet == Success ? current : "n/a")}");

    ProbeGenICamMultiple(camera, requestedValue);

    if (requestedValue <= 0)
        return;

    if (attrRet != Success)
    {
        Console.WriteLine($"相机 {camera.Index} 不报告 CAM_MULTIPLE 支持，跳过 SetValue({requestedValue})。");
        return;
    }

    int setRet = Native.TUCAM_Capa_SetValue(camera.Handle, cameraMultipleCapability, requestedValue);
    int verified = int.MinValue;
    int verifyRet = Native.TUCAM_Capa_GetValue(camera.Handle, cameraMultipleCapability, ref verified);
    Console.WriteLine(
        $"相机 {camera.Index} CAM_MULTIPLE SetValue({requestedValue})={Hex(setRet)}；" +
        $"复读={Hex(verifyRet)} value={(verifyRet == Success ? verified : "n/a")}");
}

static void ProbeGenICamMultiple(CameraContext camera, int requestedValue)
{
    IntPtr namePtr = Marshal.StringToHGlobalAnsi("MultipleCameras");
    try
    {
        var element = new TucamElement { pName = namePtr };
        int getRet = Native.TUCAM_GenICam_GetElementValue(camera.Handle, ref element, 0);
        Console.WriteLine(
            $"相机 {camera.Index} GenICam MultipleCameras：Get={Hex(getRet)} " +
            $"value={(getRet == Success ? element.uValue.IntValue.nVal : "n/a")} " +
            $"range=[{element.uValue.IntValue.nMin},{element.uValue.IntValue.nMax}] " +
            $"default={element.uValue.IntValue.nDefault} step={element.uValue.IntValue.nStep}");

        if (requestedValue <= 0 || getRet != Success)
            return;

        element.pName = namePtr;
        element.uValue.IntValue.nVal = requestedValue;
        int setRet = Native.TUCAM_GenICam_SetElementValueRef(camera.Handle, ref element, 0);
        var verified = new TucamElement { pName = namePtr };
        int verifyRet = Native.TUCAM_GenICam_GetElementValue(camera.Handle, ref verified, 0);
        Console.WriteLine(
            $"相机 {camera.Index} GenICam MultipleCameras Set({requestedValue})={Hex(setRet)}；" +
            $"复读={Hex(verifyRet)} value={(verifyRet == Success ? verified.uValue.IntValue.nVal : "n/a")}");
    }
    finally
    {
        Marshal.FreeHGlobal(namePtr);
    }
}

static string Hex(int value) => $"0x{unchecked((uint)value):X8}";
static int Fail(string message) { Console.Error.WriteLine($"[错误] {message}"); return 1; }

internal enum TestMode { DualSafe, AlternatingSafe, StereoDiagnostic, ProjectorHardware, ProjectorHardwareSerial, CameraHardwareWait, ProjectorSequence, ProjectorLight, SameCameraUnsafe }

internal sealed record Options(TestMode Mode, int CameraCount, int CameraOffset, int Frames, int TimeoutMs,
    string ConfigPath, string OutputDirectory, bool SaveRaw, bool AllowUnsafe, int ProjectorIndex,
    int CameraMultiple, bool UsbOnly)
{
    public static Options Parse(string[] args)
    {
        string Get(string name, string fallback) => args.FirstOrDefault(x => x.StartsWith(name + "="))?.Split('=', 2)[1] ?? fallback;
        TestMode mode = Get("--mode", "dual-safe") switch
        {
            "dual-safe" => TestMode.DualSafe,
            "alternating-safe" => TestMode.AlternatingSafe,
            "stereo-diagnostic" => TestMode.StereoDiagnostic,
            "projector-hardware" => TestMode.ProjectorHardware,
            "projector-hardware-serial" => TestMode.ProjectorHardwareSerial,
            "camera-hardware-wait" => TestMode.CameraHardwareWait,
            "projector-sequence" => TestMode.ProjectorSequence,
            "projector-light" => TestMode.ProjectorLight,
            "same-camera-unsafe" => TestMode.SameCameraUnsafe,
            string value => throw new ArgumentException($"未知 --mode={value}")
        };
        return new(mode, int.Parse(Get("--camera-count", "2")), int.Parse(Get("--camera-offset", "0")),
            int.Parse(Get("--frames", "20")), int.Parse(Get("--timeout-ms", "3000")),
            Get("--config-path", "."), Get("--output", "frame-probe-output"),
            args.Contains("--save-raw"), args.Contains("--allow-unsafe"),
            int.Parse(Get("--projector-index", "0")),
            int.Parse(Get("--camera-multiple", "0")),
            args.Contains("--usb-only"));
    }
}

internal sealed class CameraContext(int index, IntPtr handle)
{
    public int Index { get; } = index;
    public IntPtr Handle { get; } = handle;
    public object FrameLock { get; } = new();
    public TUCamFrame Frame = TUCamFrame.Create();
    public bool Allocated;
    public bool Started;
}

internal sealed record FrameResult(bool Success, int CameraIndex, int Shot, string Consumer, int ReturnCode,
    uint SdkFrameIndex, IntPtr Buffer, ushort Width, ushort Height, uint Stride, uint Size, ulong Hash,
    bool AllSame, double ElapsedMs, string? Detail, ExposureMetrics? Exposure)
{
    public static FrameResult Ok(int c, int s, string n, uint i, IntPtr b, ushort w, ushort h, uint st, uint z, ulong hash, bool same, double ms, ExposureMetrics exposure) =>
        new(true, c, s, n, 1, i, b, w, h, st, z, hash, same, ms, null, exposure);
    public static FrameResult Error(int c, int s, string n, int ret, double ms) =>
        new(false, c, s, n, ret, 0, IntPtr.Zero, 0, 0, 0, 0, 0, false, ms, "WaitForFrame失败", null);
    public static FrameResult Invalid(int c, int s, string n, uint i, IntPtr b, ushort w, ushort h, uint st, uint z, double ms) =>
        new(false, c, s, n, 1, i, b, w, h, st, z, 0, false, ms, "非法缓冲区/尺寸", null);
    public string ToLogLine() =>
        $"  cam={CameraIndex} consumer={Consumer} ok={Success} ret={Hex(ReturnCode)} sdkFrame={SdkFrameIndex} " +
        $"buffer=0x{Buffer:X} {Width}x{Height} stride={Stride} size={Size} hash={Hash:X16} allSame={AllSame} elapsed={ElapsedMs:F1}ms {Exposure} {Detail}";
    private static string Hex(int value) => $"0x{unchecked((uint)value):X8}";
}

internal sealed record ExposureMetrics(double MeanPercent, double P01Percent, double P50Percent,
    double P99Percent, double DarkPercent, double SaturatedPercent, int Samples)
{
    public override string ToString() =>
        $"exposure(mean={MeanPercent:F1}% p01={P01Percent:F1}% p50={P50Percent:F1}% p99={P99Percent:F1}% dark={DarkPercent:F1}% sat={SaturatedPercent:F2}% n={Samples})";
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal struct TUCamInit { public uint uiCamCount; public int uiHostCamIdx; public IntPtr pstrConfigPath; }
[StructLayout(LayoutKind.Sequential)]
internal struct TUCamOpen { public uint uiIdxOpen; public IntPtr hIdxTUCam; }
[StructLayout(LayoutKind.Sequential)]
internal struct TUCamCapaAttr
{
    public int idCapa, nValMin, nValMax, nValDft, nValStep;
}
[StructLayout(LayoutKind.Sequential)]
internal struct TUCamFrame
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public byte[] szSignature;
    public ushort usHeader, usOffset, usWidth, usHeight;
    public uint uiWidthStep;
    public byte ucDepth, ucFormat, ucChannels, ucElemBytes, ucFormatGet;
    public uint uiIndex, uiImgSize, uiRsdSize, uiHstSize;
    public IntPtr pBuffer;
    public static TUCamFrame Create() => new() { szSignature = new byte[8], ucFormatGet = 0x11, uiRsdSize = 1 };
}

[StructLayout(LayoutKind.Sequential)] internal struct TuElemValueInt { public long nVal, nMin, nMax, nStep, nDefault; }
[StructLayout(LayoutKind.Explicit)] internal struct TuElemValueUnion { [FieldOffset(0)] public TuElemValueInt IntValue; }
[StructLayout(LayoutKind.Sequential)]
internal struct TucamElement
{
    public byte IsLocked, Level;
    public ushort Representation;
    public int Type, Access, Visibility, nReserve;
    public TuElemValueUnion uValue;
    public IntPtr pName, pDisplayName, pTransfer, pDesc, pUnit, pEntries;
    public long PollingTime, DisplayPrecision;
}

internal static class Native
{
    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)] public static extern int TUCAM_Api_Config(int configType, int configValue);
    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)] public static extern int TUCAM_Api_Init(ref TUCamInit value, int timeout);
    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)] public static extern int TUCAM_Api_Uninit();
    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)] public static extern int TUCAM_Dev_Open(ref TUCamOpen value);
    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)] public static extern int TUCAM_Dev_Close(IntPtr handle);
    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)] public static extern int TUCAM_Capa_GetAttr(IntPtr handle, ref TUCamCapaAttr attr);
    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)] public static extern int TUCAM_Capa_GetValue(IntPtr handle, int capability, ref int value);
    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)] public static extern int TUCAM_Capa_SetValue(IntPtr handle, int capability, int value);
    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)] public static extern int TUCAM_Buf_Alloc(IntPtr handle, ref TUCamFrame frame);
    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)] public static extern int TUCAM_Buf_Release(IntPtr handle);
    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)] public static extern int TUCAM_Buf_AbortWait(IntPtr handle);
    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)] public static extern int TUCAM_Buf_WaitForFrame(IntPtr handle, ref TUCamFrame frame, int timeout);
    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)] public static extern int TUCAM_Cap_Start(IntPtr handle, uint mode);
    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)] public static extern int TUCAM_Cap_Stop(IntPtr handle);
    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)] public static extern int TUCAM_GenICam_ElementAttr(IntPtr handle, IntPtr element, IntPtr name, int xml = 0);
    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)] public static extern int TUCAM_GenICam_SetElementValue(IntPtr handle, IntPtr element, int xml = 0);
    [DllImport("TUCam", EntryPoint = "TUCAM_GenICam_GetElementValue", CallingConvention = CallingConvention.Cdecl)] public static extern int TUCAM_GenICam_GetElementValue(IntPtr handle, ref TucamElement element, int xml);
    [DllImport("TUCam", EntryPoint = "TUCAM_GenICam_SetElementValue", CallingConvention = CallingConvention.Cdecl)] public static extern int TUCAM_GenICam_SetElementValueRef(IntPtr handle, ref TucamElement element, int xml);
}
