using System.Runtime.InteropServices;
using System.Threading;

namespace TucamMultiCameraProbe;

internal static class Program
{
    private const int TUCAM_SUCCESS = 0x00000001;
    private const uint TUCAMRET_OUT_OF_RANGE = 0x80000311;
    private const int FrameCount = 10; // 和C++示例保持一致
    private const int WaitTimeoutMs = 10000; // 双相机USB带宽竞争时帧延迟较大，增大超时
    private const int InternalBufferFrames = 10;

    private static int Main()
    {
        // 全局异常处理
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            Console.WriteLine($"[致命错误] {e.ExceptionObject}");
            Environment.Exit(1);
        };

        // 阶段1：初始化SDK（完全匹配C++）
        TUCamInit init = new()
        {
            uiCamCount = 0,
            // ✅ 关键修复：设置配置文件路径为当前目录，和C++一致
            pstrConfigPath = Marshal.StringToHGlobalAnsi("./"),
        };

        int ret = TUCamNative.TUCAM_Api_Init(ref init, 3000);
        Marshal.FreeHGlobal(init.pstrConfigPath); // 释放字符串内存

        if (ret != TUCAM_SUCCESS)
        {
            Console.WriteLine($"[错误] TUCAM_Api_Init 失败: 0x{ret:X8}");
            return 1;
        }

        int camCount = (int)init.uiCamCount;
        Console.WriteLine($"[SDK] 初始化成功，检测到 {camCount} 台相机");
        if (camCount == 0)
        {
            Console.WriteLine("[错误] 未发现相机");
            TUCamNative.TUCAM_Api_Uninit();
            return 2;
        }

        // 阶段2：打开所有相机并配置缓冲区
        List<CameraState> cameras = new();
        for (int i = 0; i < camCount; i++)
        {
            TUCamOpen open = new() { uiIdxOpen = (uint)i, hIdxTUCam = IntPtr.Zero };
            ret = TUCamNative.TUCAM_Dev_Open(ref open);
            if (ret != TUCAM_SUCCESS)
            {
                Console.WriteLine($"[相机{i}] 打开失败: 0x{ret:X8}");
                continue;
            }

            // 通过 GenICam 读取相机当前 TriggerMode，仅用于诊断
            // 结论： TriggerMode 节点在 Dev_Open 后处于只读状态，无法运行时修改。
            // 两台相机兼容 RK3588 USB 全帧率采集需要硬件触发（外部 GPIO 同时接两台相机 TRIG_IN）。
            // 当前应用场景（标定板非运动）使用顺序采集即可。
            string trigVal = GetGenICamEnumValue(open.hIdxTUCam, "TriggerMode");
            Console.WriteLine($"[相机{i}] TriggerMode: \"{trigVal}\"");

            Console.WriteLine($"[相机{i}] 打开成功");
            cameras.Add(new CameraState(i, open.hIdxTUCam));
        }

        if (cameras.Count == 0)
        {
            Console.WriteLine("[错误] 所有相机打开失败");
            TUCamNative.TUCAM_Api_Uninit();
            return 3;
        }

        try
        {
            // 阶段3：内存分配（100%匹配C++初始化方式）
            foreach (CameraState cam in cameras)
            {
                // ✅ 完全按照C++代码初始化：只设置必要的3个字段
                TUCamFrame frame = new TUCamFrame
                {
                    szSignature = new byte[8], // 关键：必须显式分配8字节数组
                    pBuffer = IntPtr.Zero, // 关键：显式设置为NULL
                    ucFormatGet = (byte)TuFrmFormats.TUFRM_FMT_USUAL,
                    uiRsdSize = 1, // 关键：和C++一致，设置为1
                };

                ret = TUCamNative.TUCAM_Buf_Alloc(cam.Handle, ref frame);

                if (ret != TUCAM_SUCCESS)
                {
                    Console.WriteLine($"[相机{cam.Index}] Buf_Alloc 失败: 0x{ret:X8}");
                    continue;
                }
                cam.Frame = frame;
                cam.BufferAllocated = true;
                Console.WriteLine($"[相机{cam.Index}] Buf_Alloc 成功");
            }

            List<CameraState> ready = cameras.Where(c => c.BufferAllocated).ToList();
            if (ready.Count == 0)
            {
                Console.WriteLine("[错误] 所有相机Buf_Alloc失败");
                return 4;
            }

            // 每帧内顺序对每台相机：Cap_Start → 触发 → WaitForFrame → Cap_Stop
            // 每台相机独占 USB 带宽期间完成一帧传输，彻底消除带宽竞争。
            // 代价：每帧需要两次 Cap_Start/Stop 开销，但相机曝光本身仍是按顺序进行。
            string captureDir = Path.Combine(Environment.CurrentDirectory, "captures");
            Directory.CreateDirectory(captureDir);

            var totalSw = System.Diagnostics.Stopwatch.StartNew();
            long[] frameTicks = new long[FrameCount];

            for (int frameNo = 0; frameNo < FrameCount; frameNo++)
            {
                var frameSw = System.Diagnostics.Stopwatch.StartNew();

                // TODO: 此处通知投影仪发出第 frameNo 个图案
                // projector.Project(frameNo);

                foreach (CameraState cam in ready)
                {
                    // 启动采集
                    ret = TUCamNative.TUCAM_Cap_Start(
                        cam.Handle,
                        (uint)TUCamCaptureMode.TUCCM_TRIGGER_SOFTWARE
                    );
                    if (ret != TUCAM_SUCCESS)
                    {
                        Console.WriteLine(
                            $"[相机{cam.Index}] 图案{frameNo} Cap_Start 失败: 0x{ret:X8}"
                        );
                        cam.Started = true; // 标记需要 finally 清理
                        continue;
                    }
                    cam.Started = true;

                    // 软件触发曝光
                    int trgRet = DoGenICamCommand(cam.Handle, "TriggerSoftwarePulse");
                    if (trgRet != TUCAM_SUCCESS)
                        Console.WriteLine(
                            $"[相机{cam.Index}] 图案{frameNo} 触发失败: 0x{trgRet:X8}"
                        );

                    // 等待帧传输完成
                    TUCamFrame frame = cam.Frame;
                    frame.ucFormatGet = (byte)TuFrmFormats.TUFRM_FMT_USUAL;
                    int waitRet = TUCamNative.TUCAM_Buf_WaitForFrame(
                        cam.Handle,
                        ref frame,
                        WaitTimeoutMs
                    );
                    cam.Frame = frame;

                    if (waitRet == TUCAM_SUCCESS)
                    {
                        cam.CapturedFrames++;
                        int depth = (frame.ucElemBytes == 2) ? 16 : 8;
                        Console.WriteLine(
                            $"[相机{cam.Index}] 图案{frameNo} 捕获成功，宽:{frame.usWidth}，高:{frame.usHeight}，位深:{depth}"
                        );
                        string path = Path.Combine(
                            captureDir,
                            $"cam{cam.Index}_frame{frameNo:D3}.tif"
                        );
                        SaveImage(cam.Handle, ref frame, path, cam.Index);
                    }
                    else
                    {
                        Console.WriteLine(
                            $"[相机{cam.Index}] 图案{frameNo} WaitForFrame 失败: 0x{waitRet:X8}"
                        );
                    }

                    // 立即停止，释放 USB 带宽给下一台相机
                    TUCamNative.TUCAM_Cap_Stop(cam.Handle);
                    cam.Started = false;
                }
                frameSw.Stop();
                frameTicks[frameNo] = frameSw.ElapsedMilliseconds;
                Console.WriteLine($"[图案{frameNo}] 耗时: {frameSw.ElapsedMilliseconds} ms");
            }

            totalSw.Stop();
            double avgMs = frameTicks.Average();
            Console.WriteLine(
                $"\n[统计-含保存] 总耗时: {totalSw.ElapsedMilliseconds} ms | 平均每组: {avgMs:F1} ms | 共 {FrameCount} 组"
            );

            foreach (CameraState cam in ready)
                Console.WriteLine($"[相机{cam.Index}] 采集完成，共 {cam.CapturedFrames} 帧");

            // ── 第二轮：纯采集，不保存文件，测量真实帧获取耗时 ──
            Console.WriteLine("\n[基准测试] 开始纯采集（不保存文件）...");
            var totalSw2 = System.Diagnostics.Stopwatch.StartNew();
            long[] frameTicks2 = new long[FrameCount];

            foreach (CameraState cam in ready)
                cam.CapturedFrames = 0; // 重置计数

            for (int frameNo = 0; frameNo < FrameCount; frameNo++)
            {
                var frameSw2 = System.Diagnostics.Stopwatch.StartNew();

                foreach (CameraState cam in ready)
                {
                    ret = TUCamNative.TUCAM_Cap_Start(
                        cam.Handle,
                        (uint)TUCamCaptureMode.TUCCM_TRIGGER_SOFTWARE
                    );
                    if (ret != TUCAM_SUCCESS)
                    {
                        Console.WriteLine(
                            $"[相机{cam.Index}] 图案{frameNo} Cap_Start 失败: 0x{ret:X8}"
                        );
                        cam.Started = true;
                        continue;
                    }
                    cam.Started = true;

                    DoGenICamCommand(cam.Handle, "TriggerSoftwarePulse");

                    TUCamFrame frame = cam.Frame;
                    frame.ucFormatGet = (byte)TuFrmFormats.TUFRM_FMT_USUAL;
                    int waitRet = TUCamNative.TUCAM_Buf_WaitForFrame(
                        cam.Handle,
                        ref frame,
                        WaitTimeoutMs
                    );
                    cam.Frame = frame;

                    if (waitRet == TUCAM_SUCCESS)
                    {
                        cam.CapturedFrames++;
                        int depth = (frame.ucElemBytes == 2) ? 16 : 8;
                        Console.WriteLine(
                            $"[相机{cam.Index}] 图案{frameNo} 捕获成功，宽:{frame.usWidth}，高:{frame.usHeight}，位深:{depth}"
                        );
                    }
                    else
                    {
                        Console.WriteLine(
                            $"[相机{cam.Index}] 图案{frameNo} WaitForFrame 失败: 0x{waitRet:X8}"
                        );
                    }

                    TUCamNative.TUCAM_Cap_Stop(cam.Handle);
                    cam.Started = false;
                }

                frameSw2.Stop();
                frameTicks2[frameNo] = frameSw2.ElapsedMilliseconds;
                Console.WriteLine($"[图案{frameNo}] 耗时: {frameSw2.ElapsedMilliseconds} ms");
            }

            totalSw2.Stop();
            double avgMs2 = frameTicks2.Average();
            Console.WriteLine(
                $"\n[统计-纯采集] 总耗时: {totalSw2.ElapsedMilliseconds} ms | 平均每组: {avgMs2:F1} ms | 共 {FrameCount} 组"
            );
            Console.WriteLine($"[对比] 保存文件增加耗时: {avgMs - avgMs2:F1} ms/组");

            foreach (CameraState cam in ready)
                Console.WriteLine($"[相机{cam.Index}] 纯采集完成，共 {cam.CapturedFrames} 帧");
        }
        finally
        {
            // 阶段6：安全释放资源（严格顺序，匹配C++）
            foreach (CameraState cam in cameras)
            {
                try
                {
                    // 1. 停止采集（正常流程已在主循环中 Stop，此处仅处理异常退出的情况）
                    if (cam.Started)
                    {
                        TUCamNative.TUCAM_Cap_Stop(cam.Handle);
                    }

                    // 2. 释放内存
                    if (cam.BufferAllocated)
                    {
                        TUCamNative.TUCAM_Buf_Release(cam.Handle);
                    }

                    // 3. 关闭相机
                    TUCamNative.TUCAM_Dev_Close(cam.Handle);
                    Console.WriteLine($"[相机{cam.Index}] 已关闭，共捕获 {cam.CapturedFrames} 帧");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[相机{cam.Index}] 清理异常: {ex.Message}");
                }
            }

            TUCamNative.TUCAM_Api_Uninit();
            Console.WriteLine("[SDK] 已反初始化");
        }

        Console.WriteLine("按任意键退出...");
        Console.ReadKey();
        return 0;
    }

    /// <summary>
    /// 通过 GenICam 接口设置枚举类型节点（IEnumeration）
    /// 遍历 pEntries 列表找到目标 entryName，用其索引作为 nVal 写入
    /// </summary>
    /// <summary>读取 IEnumeration 节点当前值的字符串名称</summary>
    private static string GetGenICamEnumValue(IntPtr handle, string nodeName)
    {
        const int OffsetType = 4;
        const int OffsetNVal = 24;
        const int OffsetPEntries = 104;
        const int ElementStructSize = 128;

        IntPtr elementPtr = Marshal.AllocHGlobal(ElementStructSize);
        for (int j = 0; j < ElementStructSize; j++)
            Marshal.WriteByte(elementPtr, j, 0);
        IntPtr namePtr = Marshal.StringToHGlobalAnsi(nodeName);
        try
        {
            // 直接用 ElementAttr（需要传 namePtr），它会同时填充 nVal（当前值）和 pEntries
            // 不能先调 GetElementValue：elementPtr 全零时 pName=null，SDK 解引用会段错误
            int ret = TUCamNative.TUCAM_GenICam_ElementAttr(handle, elementPtr, namePtr);
            if (ret != TUCAM_SUCCESS)
                return $"读取失败 0x{ret:X8}";

            int elemType = Marshal.ReadInt32(elementPtr, OffsetType);
            if (elemType != 0x09)
                return $"非枚举类型(0x{elemType:X2})";

            long nVal = Marshal.ReadInt64(elementPtr, OffsetNVal);
            IntPtr pEntriesPtr = Marshal.ReadIntPtr(elementPtr, OffsetPEntries);
            if (pEntriesPtr == IntPtr.Zero)
                return $"index={nVal}(entries=null)";

            try
            {
                IntPtr entryPtr = Marshal.ReadIntPtr(pEntriesPtr, (int)(nVal * IntPtr.Size));
                return entryPtr != IntPtr.Zero
                    ? Marshal.PtrToStringAnsi(entryPtr) ?? $"index={nVal}"
                    : $"index={nVal}";
            }
            catch
            {
                return $"index={nVal}";
            }
        }
        finally
        {
            Marshal.FreeHGlobal(namePtr);
            Marshal.FreeHGlobal(elementPtr);
        }
    }

    private static int SetGenICamEnum(IntPtr handle, string nodeName, string entryName)
    {
        // ARM64 GCC 默认对齐下 TUCAM_ELEMENT 内存布局（共 128 字节）：
        //  offset  0: IsLocked (BYTE 1)
        //  offset  1: Level    (BYTE 1)
        //  offset  2: Repr     (WORD 2)
        //  offset  4: Type     (INT32 4)
        //  offset  8: Access   (INT32 4)
        //  offset 12: Visibility(INT32 4)
        //  offset 16: nReserve (INT32 4)
        //  offset 20: [4 字节填充，INT64 需 8 字节对齐]
        //  offset 24: nVal     (INT64 8)   ← union 起始
        //  offset 32: nMin     (INT64 8)
        //  offset 40: nMax     (INT64 8)
        //  offset 48: nStep    (INT64 8)
        //  offset 56: nDefault (INT64 8)   ← union 结束，offset 64
        //  offset 64: pName    (PTR  8)
        //  offset 72: pDisplayName (PTR 8)
        //  offset 80: pTransfer    (PTR 8)
        //  offset 88: pDesc        (PTR 8)
        //  offset 96: pUnit        (PTR 8)
        //  offset 104: pEntries    (PTR 8)
        //  offset 112: PollingTime (INT64 8)
        //  offset 120: DisplayPrecision (INT64 8)
        const int OffsetType = 4;
        const int OffsetNVal = 24;
        const int OffsetNMax = 40;
        const int OffsetPEntries = 104;
        const int ElementStructSize = 128;

        IntPtr elementPtr = Marshal.AllocHGlobal(ElementStructSize);
        for (int j = 0; j < ElementStructSize; j++)
            Marshal.WriteByte(elementPtr, j, 0);
        IntPtr namePtr = Marshal.StringToHGlobalAnsi(nodeName);
        try
        {
            int ret = TUCamNative.TUCAM_GenICam_ElementAttr(handle, elementPtr, namePtr);
            if (ret != TUCAM_SUCCESS)
                return ret;

            // 验证节点类型为 IEnumeration（0x09）
            int elemType = Marshal.ReadInt32(elementPtr, OffsetType);
            if (elemType != 0x09)
            {
                Console.WriteLine(
                    $"  [警告] 节点 {nodeName} 类型为 0x{elemType:X2}，非 IEnumeration"
                );
                return unchecked((int)0x80000000);
            }

            IntPtr pEntriesPtr = Marshal.ReadIntPtr(elementPtr, OffsetPEntries);
            if (pEntriesPtr == IntPtr.Zero)
            {
                Console.WriteLine($"  [警告] 节点 {nodeName} pEntries 为 null");
                return unchecked((int)0x80000000);
            }

            // nMax 限定枚举条目数量上界，最多扫描 64 条
            long nMax = Marshal.ReadInt64(elementPtr, OffsetNMax);
            long limit = Math.Min(nMax + 2, 64);
            Console.WriteLine(
                $"  [诊断] {nodeName}: type=0x{elemType:X2} nMax={nMax} pEntries=0x{pEntriesPtr:X}"
            );

            for (long idx = 0; idx < limit; idx++)
            {
                IntPtr entryStrPtr;
                try
                {
                    entryStrPtr = Marshal.ReadIntPtr(pEntriesPtr, (int)(idx * IntPtr.Size));
                }
                catch
                {
                    break;
                }

                if (entryStrPtr == IntPtr.Zero)
                    break;

                string? entry;
                try
                {
                    entry = Marshal.PtrToStringAnsi(entryStrPtr);
                }
                catch
                {
                    Console.WriteLine(
                        $"  [诊断] {nodeName}[{idx}] 指针无效(0x{entryStrPtr:X})，停止遍历"
                    );
                    break;
                }

                Console.WriteLine($"  [诊断] {nodeName}[{idx}] = \"{entry}\"");
                if (string.Equals(entry, entryName, StringComparison.Ordinal))
                {
                    Marshal.WriteInt64(elementPtr, OffsetNVal, idx);
                    return TUCamNative.TUCAM_GenICam_SetElementValue(handle, elementPtr);
                }
            }

            Console.WriteLine($"  [警告] 节点 {nodeName} 未找到枚举项 \"{entryName}\"");
            return unchecked((int)0x80000000);
        }
        finally
        {
            Marshal.FreeHGlobal(namePtr);
            Marshal.FreeHGlobal(elementPtr);
        }
    }

    /// <summary>
    /// 通过 GenICam 执行 ICommand 类型节点（通用命令执行）
    /// 适用于 AcquisitionStart / AcquisitionStop / TriggerSoftwarePulse 等命令
    /// </summary>
    private static int DoGenICamCommand(IntPtr handle, string commandName)
    {
        const int ElementStructSize = 128;
        IntPtr elementPtr = Marshal.AllocHGlobal(ElementStructSize);
        for (int i = 0; i < ElementStructSize; i++)
            Marshal.WriteByte(elementPtr, i, 0);
        IntPtr namePtr = Marshal.StringToHGlobalAnsi(commandName);
        try
        {
            int attrRet = TUCamNative.TUCAM_GenICam_ElementAttr(handle, elementPtr, namePtr);
            if (attrRet != TUCAM_SUCCESS)
                return attrRet;

            int elemType = Marshal.ReadInt32(elementPtr, 4); // Type 字段偏移 4
            if (elemType != 0x04) // TU_ElemCommand
                return unchecked((int)0x80000000); // 非 Command 节点

            return TUCamNative.TUCAM_GenICam_SetElementValue(handle, elementPtr);
        }
        finally
        {
            Marshal.FreeHGlobal(namePtr);
            Marshal.FreeHGlobal(elementPtr);
        }
    }

    /// <summary>
    /// 图像保存函数（兼容 ARM64 TUCam SDK）
    /// ARM64 版本的 TUCAM_File_SaveImage 接受 TUCAM_FILE_SAVE 结构体，而非分散参数
    /// </summary>
    private static void SaveImage(
        IntPtr handle,
        ref TUCamFrame frame,
        string fullPath,
        int cameraIndex
    )
    {
        // 将路径转为 ANSI C 字符串（非托管内存）
        IntPtr pathPtr = Marshal.StringToHGlobalAnsi(fullPath);
        // 将帧结构体复制到非托管内存，使 pFrame 指针有效
        int frameSize = Marshal.SizeOf<TUCamFrame>();
        IntPtr framePtr = Marshal.AllocHGlobal(frameSize);

        try
        {
            Marshal.StructureToPtr(frame, framePtr, false);

            TUCamFileSave fileSave = new()
            {
                nSaveFmt = (int)TuImgFormats.TUFMT_TIF,
                pstrSavePath = pathPtr,
                pFrame = framePtr,
            };

            int saveRet = TUCamNative.TUCAM_File_SaveImage(handle, fileSave);

            if (saveRet == TUCAM_SUCCESS)
                Console.WriteLine($"[相机{cameraIndex}] 已保存: {Path.GetFileName(fullPath)}");
            else
                Console.WriteLine($"[相机{cameraIndex}] SaveImage 失败: 0x{saveRet:X8}");
        }
        finally
        {
            Marshal.FreeHGlobal(pathPtr);
            Marshal.FreeHGlobal(framePtr);
        }
    }
}

internal sealed class CameraState
{
    public int Index { get; }
    public IntPtr Handle { get; }
    public TUCamFrame Frame;
    public bool BufferAllocated;
    public bool Started;
    public int CapturedFrames;
    public volatile bool IsRunning;

    public CameraState(int index, IntPtr handle)
    {
        Index = index;
        Handle = handle;
    }
}

internal enum TUCamCaptureMode
{
    TUCCM_SEQUENCE = 0x00,
    TUCCM_TRIGGER_STANDARD = 0x01,
    TUCCM_TRIGGER_SYNCHRONOUS = 0x02,
    TUCCM_TRIGGER_GLOBAL = 0x03,
    TUCCM_TRIGGER_SOFTWARE = 0x04,
}

internal enum TuFrmFormats : byte
{
    TUFRM_FMT_RAW = 0x10,
    TUFRM_FMT_USUAL = 0x11, // 注意：C++中是TUFRM_FMT_USUAl（小写l），值相同
    TUFRM_FMT_RGB888 = 0x12,
}

/// <summary>保存文件格式（对应 ARM64 TUCam SDK 头文件中的 TUIMG_FORMATS）</summary>
internal enum TuImgFormats : int
{
    TUFMT_RAW = 0x01,
    TUFMT_TIF = 0x02,
    TUFMT_PNG = 0x04,
    TUFMT_JPG = 0x08,
    TUFMT_BMP = 0x10,
}

/// <summary>
/// 对应 ARM64 TUCam SDK 头文件中的 TUCAM_FILE_SAVE 结构体
/// ARM64 版本的 TUCAM_File_SaveImage 接受此结构体（按值传递），而非分散参数
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TUCamFileSave
{
    public int nSaveFmt; // 保存格式，见 TuImgFormats
    public IntPtr pstrSavePath; // 文件路径（char*，由调用方负责内存管理）
    public IntPtr pFrame; // 帧数据指针（PTUCAM_FRAME）
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal struct TUCamInit
{
    public uint uiCamCount;
    public IntPtr pstrConfigPath;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TUCamOpen
{
    public uint uiIdxOpen;
    public IntPtr hIdxTUCam;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TUCamTriggerAttr
{
    public int nTgrMode;
    public int nExpMode;
    public int nEdgeMode;
    public int nDelayTm;
    public int nFrames;
    public int nBufFrames;
}

/// <summary>
/// ✅ 100%匹配C++ SDK头文件的TUCAM_FRAME结构体
/// 经过与官方C++代码逐字节验证
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TUCamFrame
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public byte[] szSignature;
    public ushort usHeader;
    public ushort usOffset;
    public ushort usWidth;
    public ushort usHeight;
    public uint uiWidthStep;
    public byte ucDepth;
    public byte ucFormat;
    public byte ucChannels;
    public byte ucElemBytes;
    public byte ucFormatGet;
    public uint uiIndex;
    public uint uiImgSize;
    public uint uiRsdSize;
    public uint uiHstSize;
    public IntPtr pBuffer;
}

internal static class TUCamNative
{
    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_Api_Init(ref TUCamInit pInitParam, int nTimeOut = 1000);

    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_Api_Uninit();

    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_Dev_Open(ref TUCamOpen pOpenParam);

    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_Dev_Close(IntPtr hTUCam);

    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_Buf_Alloc(IntPtr hTUCam, ref TUCamFrame pFrame);

    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_Buf_Release(IntPtr hTUCam);

    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_Buf_AbortWait(IntPtr hTUCam);

    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_Buf_WaitForFrame(
        IntPtr hTUCam,
        ref TUCamFrame pFrame,
        int nTimeOut = 1000
    );

    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_Cap_GetTrigger(IntPtr hTUCam, ref TUCamTriggerAttr tgrAttr);

    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_Cap_SetTrigger(IntPtr hTUCam, TUCamTriggerAttr tgrAttr);

    /// <summary>
    /// ARM64 TUCam SDK 中此函数接受 TUCAM_FILE_SAVE 结构体（按值），而非分散的路径/帧/格式参数
    /// </summary>
    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_File_SaveImage(IntPtr hTUCam, TUCamFileSave fileSave);

    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_Cap_Start(IntPtr hTUCam, uint uiMode);

    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_Cap_Stop(IntPtr hTUCam);

    /// <summary>
    /// ⛔ 在 RK3588 ARM64 上此 SDK API 返回 NotSupport，禁止调用。
    /// 软件触发必须通过 GenICam 命令节点执行：
    /// <code>DoGenICamCommand(handle, "TriggerSoftwarePulse")</code>
    /// </summary>
    [Obsolete(
        "RK3588 不支持此 API，请使用 DoGenICamCommand(handle, \"TriggerSoftwarePulse\") 替代。",
        error: true
    )]
    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_Cap_DoSoftwareTrigger(IntPtr hTUCam, uint uiMode = 0); // 0 = TUCTS_TIMED

    /// <summary>GenICam 获取节点属性，第三参数 pName 为 ANSI 字符串指针</summary>
    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_GenICam_ElementAttr(
        IntPtr hTUCam,
        IntPtr pElement,
        IntPtr pName,
        int xml = 0
    );

    /// <summary>GenICam 设置节点值，对 ICommand 类型相当于 Execute</summary>
    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_GenICam_SetElementValue(
        IntPtr hTUCam,
        IntPtr pElement,
        int xml = 0
    );

    /// <summary>GenICam 读取节点当前值（pElement 的 nVal/dbVal 会被填充）</summary>
    [DllImport("TUCam", CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_GenICam_GetElementValue(
        IntPtr hTUCam,
        IntPtr pElement,
        int xml = 0
    );
}
