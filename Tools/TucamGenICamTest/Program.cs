// TUCam GenICam 节点诊断工具
// 排查 DeviceVendorName / DeviceModelName / DeviceManufacturerInfo / DeviceVersion / DeviceUserID 读不上来的问题
// 发布到 ARM 设备后运行，确保 libTUCam.so 已在 LD_LIBRARY_PATH 中

using System.Runtime.InteropServices;
using System.Text;
using OpenCvSharp;

namespace TucamGenICamTest;

// ── P/Invoke ──────────────────────────────────────────────────────────────────

internal static class TUCamNative
{
#if RUNTIME_LINUX
    private const string DllName = "libTUCam.so";
#else
    private const string DllName = "TUCam.dll";
#endif

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_Api_Init(ref TUCamInit p, int timeout = 3000);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_Api_Uninit();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_Dev_Open(ref TUCamOpen p);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_Dev_Close(IntPtr h);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_Dev_GetInfo(IntPtr h, ref TUCamValueInfo p);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_GenICam_ElementAttr(
        IntPtr h,
        ref TucamElement e,
        IntPtr pName,
        int xml
    );

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_GenICam_ElementAttrNext(
        IntPtr h,
        ref TucamElement e,
        IntPtr pName,
        int xml
    );

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_GenICam_GetElementValue(IntPtr h, ref TucamElement e, int xml);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_GenICam_SetElementValue(IntPtr h, ref TucamElement e, int xml);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_Buf_Alloc(IntPtr h, ref TUCamFrame pFrame);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_Buf_Release(IntPtr h);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_Buf_AbortWait(IntPtr h);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_Buf_WaitForFrame(IntPtr h, ref TUCamFrame pFrame, int timeout);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_Cap_Start(IntPtr h, uint uiMode);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int TUCAM_Cap_Stop(IntPtr h);
}

// ── 结构体 ────────────────────────────────────────────────────────────────────

[StructLayout(LayoutKind.Sequential)]
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
internal struct TUCamValueInfo
{
    public int nId;
    public int nValue;
    public IntPtr pText;
    public int nTextSize;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TuElemValueInt
{
    public long nVal;
    public long nMin,
        nMax,
        nStep,
        nDefault;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TuElemValueFloat
{
    public double dbVal,
        dbMin,
        dbMax,
        dbStep,
        dbDefault;
}

[StructLayout(LayoutKind.Explicit)]
internal struct TuElemValueUnion
{
    [FieldOffset(0)]
    public TuElemValueInt IntValue;

    [FieldOffset(0)]
    public TuElemValueFloat FloatValue;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TUCamFrame
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public byte[] szSignature; // 版权签名（8字节）
    public ushort usHeader; // 帧头大小
    public ushort usOffset; // 数据偏移量
    public ushort usWidth; // 图像宽度
    public ushort usHeight; // 图像高度
    public uint uiWidthStep; // 行步长
    public byte ucDepth; // 位深度
    public byte ucFormat; // 实际数据格式
    public byte ucChannels; // 通道数
    public byte ucElemBytes; // 每像素字节数
    public byte ucFormatGet; // 期望格式（输入，0=原始）
    public uint uiIndex; // 帧序号
    public uint uiImgSize; // 图像数据字节数
    public uint uiRsdSize; // 保留帧数（输入，1=单帧）
    public uint uiHstSize; // 直方图大小
    public IntPtr pBuffer; // 图像数据指针
}

[StructLayout(LayoutKind.Sequential)]
internal struct TucamElement
{
    public byte IsLocked,
        Level;
    public ushort Representation;
    public int Type;
    public int Access;
    public int Visibility;
    public int nReserve;
    public TuElemValueUnion uValue;
    public IntPtr pName;
    public IntPtr pDisplayName;
    public IntPtr pTransfer;
    public IntPtr pDesc;
    public IntPtr pUnit;
    public IntPtr pEntries;
    public long PollingTime,
        DisplayPrecision;
}

// ── 主程序 ────────────────────────────────────────────────────────────────────

internal static class Program
{
    private const int TUIDI_BUS = 0x00;
    private const int TUIDI_VENDOR = 0x01;
    private const int TUIDI_PRODUCT = 0x02;
    private const int TUIDI_VERSION_API = 0x04;
    private const int TUIDI_VERSION_FRMW = 0x05;
    private const int TUIDI_VERSION_FPGA = 0x06;
    private const int TUIDI_VERSION_DRIVER = 0x07;
    private const int TUIDI_CAMERA_MODEL = 0x09;
    private const int TUCAM_SUCCESS = 1; // TUCamRet.Success = 0x00000001
    private const int TU_CAMERA_XML = 0;
    private const int TU_CAMERABASE_XML = 3;

    private static string TypeName(int t) =>
        t switch
        {
            0 => "Value",
            1 => "Base",
            2 => "Integer",
            3 => "Boolean",
            4 => "Command",
            5 => "Float",
            6 => "String",
            7 => "Register",
            8 => "Category",
            9 => "Enumeration",
            10 => "EnumEntry",
            11 => "Port",
            _ => $"Unknown({t})",
        };

    private static string AccessName(int a) =>
        a switch
        {
            0 => "NotImplemented",
            1 => "NotAvailable",
            2 => "WriteOnly",
            3 => "ReadOnly",
            4 => "ReadWrite",
            _ => $"Unknown({a})",
        };

    static unsafe void Main(string[] args)
    {
        Console.WriteLine("=== TUCam GenICam 节点诊断工具 ===");
        Console.WriteLine(
            $"OS: {RuntimeInformation.OSDescription}  Arch: {RuntimeInformation.OSArchitecture}"
        );
        Console.WriteLine("默认模式：从 DLL/相机内部 XML 枚举并打印所有 GenICam 节点。");
        Console.WriteLine(
            "可选参数：--probe 探测选择器依赖；--legacy 运行旧的固定节点读写测试；--capture 同时运行拍照/自动曝光测试。"
        );
        Console.WriteLine();

        TUCamInit init = default;
        int ret = TUCamNative.TUCAM_Api_Init(ref init, 3000);
        Console.WriteLine($"[Init] ret={ret}  camCount={init.uiCamCount}");
        if (ret != TUCAM_SUCCESS || init.uiCamCount == 0)
        {
            Console.WriteLine($"初始化失败或无相机（ret=0x{ret:X8}，Success=0x00000001）");
            return;
        }

        TUCamOpen open = new() { uiIdxOpen = 0 };
        ret = TUCamNative.TUCAM_Dev_Open(ref open);
        Console.WriteLine($"[Open] ret={ret}  handle=0x{open.hIdxTUCam:X}");
        if (ret != TUCAM_SUCCESS)
        {
            TUCamNative.TUCAM_Api_Uninit();
            Console.WriteLine($"打开相机失败（ret=0x{ret:X8}）");
            return;
        }
        IntPtr h = open.hIdxTUCam;

        try
        {
            Console.WriteLine();
            Console.WriteLine("─── TUCAM_Dev_GetInfo ───────────────────────────────────────");
            (int id, string name)[] infos =
            [
                (TUIDI_BUS, "BUS"),
                (TUIDI_VENDOR, "VENDOR"),
                (TUIDI_PRODUCT, "PRODUCT"),
                (TUIDI_VERSION_API, "VERSION_API"),
                (TUIDI_VERSION_FRMW, "VERSION_FRMW"),
                (TUIDI_VERSION_FPGA, "VERSION_FPGA"),
                (TUIDI_VERSION_DRIVER, "VERSION_DRIVER"),
                (TUIDI_CAMERA_MODEL, "CAMERA_MODEL"),
            ];
            foreach ((int id, string name) in infos)
            {
                byte[] buf = new byte[512];
                fixed (byte* p = buf)
                {
                    TUCamValueInfo info = new()
                    {
                        nId = id,
                        pText = (IntPtr)p,
                        nTextSize = buf.Length,
                    };
                    int r = TUCamNative.TUCAM_Dev_GetInfo(h, ref info);
                    string text = Encoding.ASCII.GetString(buf).TrimEnd('\0');
                    Console.WriteLine($"  [{name}] ret={r}  nValue={info.nValue}  text=\"{text}\"");
                }
            }

            GenICamNodeDumper.DumpAll(h);

            bool runProbe = HasArg(args, "--probe");
            if (runProbe)
            {
                GenICamNodeDumper.ProbeSelectorDependencies(h);
            }

            bool runLegacy = HasArg(args, "--legacy");
            bool runCapture = HasArg(args, "--capture");
            if (!runLegacy && !runCapture)
            {
                Console.WriteLine();
                Console.WriteLine("动态节点枚举完成。旧的固定节点读写、拍照和自动曝光测试已跳过。");
                return;
            }

            Console.WriteLine();
            Console.WriteLine("─── GetElementValue (xml=0, TU_CAMERA_XML) ──────────────────");
            string[] nodes =
            [
                "DeviceVendorName",
                "DeviceModelName",
                "DeviceManufacturerInfo",
                "DeviceVersion",
                "DeviceUserID",
                "DeviceSerialNumber",
            ];
            foreach (string node in nodes)
                PrintGet(h, node, TU_CAMERA_XML);

            Console.WriteLine();
            Console.WriteLine("─── GetElementValue (xml=3, TU_CAMERABASE_XML) ──────────────");
            foreach (string node in nodes)
                PrintGet(h, node, TU_CAMERABASE_XML);

            Console.WriteLine();
            Console.WriteLine("─── ElementAttr (xml=0) ─────────────────────────────────────");
            foreach (string node in nodes)
                PrintAttr(h, node, TU_CAMERA_XML);

            Console.WriteLine();
            Console.WriteLine(
                "─── DeviceSerialNumber 专项读取（DeviceControl / RO / Expert / String） ─────"
            );
            TestReadSerialNumber(h);

            Console.WriteLine();
            Console.WriteLine("─── DeviceUserID 写入测试 ───────────────────────────────────");
            TestSetUserID(h);

            if (runCapture)
            {
                Console.WriteLine();
                Console.WriteLine(
                    "─── 拍照测试（排查空图） ──────────────────────────────────────────────"
                );
                TestCapture(h);
                Console.WriteLine();
                Console.WriteLine(
                    "─── 自动曝光采集（OpenCV 亮度分析） ────────────────────────────"
                );
                AutoExposureCapture(h);
            }
        }
        finally
        {
            TUCamNative.TUCAM_Dev_Close(h);
            TUCamNative.TUCAM_Api_Uninit();
            Console.WriteLine();
            Console.WriteLine("相机已关闭，SDK 已反初始化");
        }
    }

    private static bool HasArg(string[] args, string name)
    {
        return args.Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));
    }

    private static void PrintGet(IntPtr h, string nodeName, int xml)
    {
        IntPtr pName = Marshal.StringToHGlobalAnsi(nodeName);
        try
        {
            TucamElement e = default;
            e.pName = pName;
            int ret = TUCamNative.TUCAM_GenICam_GetElementValue(h, ref e, xml);
            string transfer =
                e.pTransfer != IntPtr.Zero
                    ? $"\"{Marshal.PtrToStringAnsi(e.pTransfer) ?? "<null>"}\""
                    : "<IntPtr.Zero>";
            string dispName =
                e.pDisplayName != IntPtr.Zero ? Marshal.PtrToStringAnsi(e.pDisplayName) ?? "" : "";
            Console.WriteLine($"  [{nodeName}]");
            Console.WriteLine(
                $"    ret={ret}  Type={TypeName(e.Type)}  Access={AccessName(e.Access)}  Visibility={e.Visibility}"
            );
            Console.WriteLine($"    nVal(len)={e.uValue.IntValue.nVal}  pTransfer={transfer}");
            if (!string.IsNullOrEmpty(dispName))
                Console.WriteLine($"    DisplayName=\"{dispName}\"");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [{nodeName}] 异常: {ex.Message}");
        }
        finally
        {
            Marshal.FreeHGlobal(pName);
        }
    }

    private static void PrintAttr(IntPtr h, string nodeName, int xml)
    {
        IntPtr pName = Marshal.StringToHGlobalAnsi(nodeName);
        try
        {
            TucamElement e = default;
            int ret = TUCamNative.TUCAM_GenICam_ElementAttr(h, ref e, pName, xml);
            string transfer =
                e.pTransfer != IntPtr.Zero
                    ? $"\"{Marshal.PtrToStringAnsi(e.pTransfer) ?? "<null>"}\""
                    : "<IntPtr.Zero>";
            Console.WriteLine(
                $"  [Attr:{nodeName}] ret={ret}  Type={TypeName(e.Type)}  Access={AccessName(e.Access)}  pTransfer={transfer}"
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [Attr:{nodeName}] 异常: {ex.Message}");
        }
        finally
        {
            Marshal.FreeHGlobal(pName);
        }
    }

    private static void TestCapture(IntPtr h)
    {
        // 0. 拍照前：先读取相机位深信息
        long pixelSize = GenICamGetInt(h, "PixelSize");
        Console.WriteLine(
            $"  PixelSize = {pixelSize} bit"
                + (
                    pixelSize > 8
                        ? "（高位深，将对帧数据自动做位深转换）"
                        : "（标准8bit，直接使用）"
                )
        );

        // 1. 采集前：读取并打印当前曝光时间，然后设置为合理值（微秒）
        long curExposure = GenICamGetInt(h, "ExposureTime");
        Console.WriteLine($"  ExposureTime 当前值 = {curExposure} μs");
        if (curExposure <= 0)
        {
            // 读取失败或值为 0，强制写入 20 ms
            long setUs = 20_000;
            int setRet = GenICamSetInt(h, "ExposureTime", setUs);
            Console.WriteLine($"  ExposureTime 设置 {setUs} μs → ret=0x{setRet:X8}");
        }
        else if (curExposure < 1_000)
        {
            // 当前曝光 < 1 ms，太短，调整为 20 ms
            long setUs = 20_000;
            int setRet = GenICamSetInt(h, "ExposureTime", setUs);
            Console.WriteLine($"  ExposureTime 过短，调整为 {setUs} μs → ret=0x{setRet:X8}");
        }
        else
        {
            Console.WriteLine($"  ExposureTime 无需调整");
        }

        // 1. 分配帧缓冲区
        TUCamFrame frame = new() { uiRsdSize = 1 };
        int allocRet = TUCamNative.TUCAM_Buf_Alloc(h, ref frame);
        Console.WriteLine($"  Buf_Alloc  ret=0x{allocRet:X8}  ok={allocRet == TUCAM_SUCCESS}");
        if (allocRet != TUCAM_SUCCESS)
            return;

        try
        {
            // 2. 以 Sequence 模式启动采集（0=Sequence，1=TriggerStandard）
            int startRet = TUCamNative.TUCAM_Cap_Start(h, 0);
            Console.WriteLine($"  Cap_Start  ret=0x{startRet:X8}  ok={startRet == TUCAM_SUCCESS}");
            if (startRet != TUCAM_SUCCESS)
                return;

            try
            {
                // 3. 等帧，超时 5000 ms
                int waitRet = TUCamNative.TUCAM_Buf_WaitForFrame(h, ref frame, 5000);
                Console.WriteLine(
                    $"  WaitFrame  ret=0x{waitRet:X8}  ok={waitRet == TUCAM_SUCCESS}"
                );

                // 4. 打印帧元数据
                Console.WriteLine($"  Width={frame.usWidth}  Height={frame.usHeight}");
                Console.WriteLine(
                    $"  Depth={frame.ucDepth}bit  Channels={frame.ucChannels}  ElemBytes={frame.ucElemBytes}"
                );
                Console.WriteLine(
                    $"  Format={frame.ucFormat}  uiImgSize={frame.uiImgSize}  WidthStep={frame.uiWidthStep}"
                );
                Console.WriteLine(
                    $"  pBuffer=0x{frame.pBuffer:X}  usOffset={frame.usOffset}  FrameIdx={frame.uiIndex}"
                );

                if (waitRet == TUCAM_SUCCESS && frame.pBuffer != IntPtr.Zero && frame.uiImgSize > 0)
                {
                    // 5. 拷贝图像数据
                    int size = (int)frame.uiImgSize;
                    byte[] data = new byte[size];
                    Marshal.Copy(frame.pBuffer + frame.usOffset, data, 0, size);

                    // 6. 统计非零字节，判断是否全黑
                    long nonZero = 0;
                    for (int i = 0; i < data.Length; i++)
                        if (data[i] != 0)
                            nonZero++;
                    double nonZeroPct = 100.0 * nonZero / data.Length;
                    Console.WriteLine(
                        $"  非零字节: {nonZero}/{data.Length} ({nonZeroPct:F2}%)  → {(nonZero == 0 ? "⚠ 全零/空图" : "有像素数据")}"
                    );

                    // 7. 打印前 32 字节（十六进制）
                    int showBytes = Math.Min(32, data.Length);
                    string hex = BitConverter.ToString(data, 0, showBytes).Replace("-", " ");
                    Console.WriteLine($"  前{showBytes}字节: {hex}");

                    // 8. 根据帧实际位深做位深转换，保存为 PNG
                    string outPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        $"tucam_frame_{frame.usWidth}x{frame.usHeight}_d{frame.ucDepth}ch{frame.ucChannels}.png"
                    );
                    unsafe
                    {
                        fixed (byte* ptr = data)
                        {
                            using Mat bgrMat = FrameDataToMat(ptr, frame);
                            Cv2.ImWrite(outPath, bgrMat);
                        }
                    }
                    Console.WriteLine($"  已保存 PNG → {outPath}");
                }
                else
                {
                    Console.WriteLine("  ⚠ 未获取到有效帧数据");
                }
            }
            finally
            {
                TUCamNative.TUCAM_Buf_AbortWait(h);
                TUCamNative.TUCAM_Cap_Stop(h);
            }
        }
        finally
        {
            TUCamNative.TUCAM_Buf_Release(h);
        }
    }

    private static void TestReadSerialNumber(IntPtr h)
    {
        // 分别用 xml=0（TU_CAMERA_XML）和 xml=3（TU_CAMERABASE_XML）各读一次
        foreach (int xml in new[] { TU_CAMERA_XML, TU_CAMERABASE_XML })
        {
            string xmlLabel = xml == TU_CAMERA_XML ? "TU_CAMERA_XML(0)" : "TU_CAMERABASE_XML(3)";
            IntPtr pName = Marshal.StringToHGlobalAnsi("DeviceSerialNumber");
            try
            {
                TucamElement e = default;
                e.pName = pName;
                int ret = TUCamNative.TUCAM_GenICam_GetElementValue(h, ref e, xml);
                string value =
                    e.pTransfer != IntPtr.Zero
                        ? Marshal.PtrToStringAnsi(e.pTransfer) ?? "<null>"
                        : "<IntPtr.Zero>";
                Console.WriteLine($"  [{xmlLabel}]");
                Console.WriteLine($"    ret=0x{ret:X8}  ok={ret == TUCAM_SUCCESS}");
                Console.WriteLine(
                    $"    Type={TypeName(e.Type)}  Access={AccessName(e.Access)}  Visibility={e.Visibility}"
                );
                Console.WriteLine($"    Value=\"{value}\"");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [{xmlLabel}] 异常: {ex.Message}");
            }
            finally
            {
                Marshal.FreeHGlobal(pName);
            }
        }
    }

    /// <summary>    /// 将 TUCam 帧原始数据转换为 8bit BGR Mat，自动识别位深并做归一化转换。
    /// - ElemBytes=1：8bit 数据；3通道按 RGB→BGR，单通道直接使用。
    /// - ElemBytes=2：10/12bit 数据（uint16 LE）；ConvertTo 右移取高 8 位后输出 BGR。
    /// </summary>
    private static unsafe Mat FrameDataToMat(byte* ptr, TUCamFrame frame)
    {
        int width = frame.usWidth;
        int height = frame.usHeight;
        int channels = frame.ucChannels > 0 ? frame.ucChannels : 3;
        int elemBytes = frame.ucElemBytes > 0 ? frame.ucElemBytes : 1;
        int bitDepth = frame.ucDepth; // TUCam: 0 或 8 = 8bit，10、12、16 = 高位深
        long widthStep = frame.uiWidthStep;

        if (elemBytes <= 1)
        {
            // ── 8bit 路径 ───────────────────────────────────────────────────────
            if (channels >= 3)
            {
                // RGB888 → BGR888
                using Mat rgb = Mat.FromPixelData(
                    height,
                    width,
                    MatType.CV_8UC3,
                    (IntPtr)ptr,
                    widthStep
                );
                Mat bgr = new Mat();
                Cv2.CvtColor(rgb, bgr, ColorConversionCodes.RGB2BGR);
                return bgr;
            }
            else
            {
                // 灰度 8bit，直接克隆
                return Mat.FromPixelData(height, width, MatType.CV_8UC1, (IntPtr)ptr, widthStep)
                    .Clone();
            }
        }
        else
        {
            // ── 10/12/16bit 路径 ──────────────────────────────────────────────────
            // SDK 有时不填 ucDepth，此时尝试从 PixelSize 读到的全局位深信息已处理，
            // 对于 ElemBytes=2 且 ucDepth≤8 的情况，保守按 12bit 处理。
            if (bitDepth <= 8)
                bitDepth = 12;
            int shift = bitDepth - 8; // 12bit→4, 10bit→2, 16bit→8
            double scale = 1.0 / (1 << shift); // ConvertTo 的缩放系数

            if (channels >= 3)
            {
                // 3通道高位深 RGB → 8bit BGR
                using Mat raw16 = Mat.FromPixelData(
                    height,
                    width,
                    MatType.CV_16UC3,
                    (IntPtr)ptr,
                    widthStep
                );
                using Mat rgb8 = new Mat();
                raw16.ConvertTo(rgb8, MatType.CV_8UC3, scale);
                Mat bgr = new Mat();
                Cv2.CvtColor(rgb8, bgr, ColorConversionCodes.RGB2BGR);
                return bgr;
            }
            else
            {
                // 单通道高位深灰度 → 8bit
                using Mat raw16 = Mat.FromPixelData(
                    height,
                    width,
                    MatType.CV_16UC1,
                    (IntPtr)ptr,
                    widthStep
                );
                Mat gray8 = new Mat();
                raw16.ConvertTo(gray8, MatType.CV_8UC1, scale);
                return gray8;
            }
        }
    }

    /// <summary>    /// 自动曝光采集：迭代调节曝光时间，直到帧的均値灰度接近目标值，然后保存最终图像。
    /// 策略：新曝光 = 旧曝光 × (目标亮度 / 当前均值)，限幅在 [1ms, 2s]，最多迭代 12 次。
    /// </summary>
    private static void AutoExposureCapture(IntPtr h)
    { // 先读取相机位深，供后续帧数据转换使用
        long pixelSize = GenICamGetInt(h, "PixelSize");
        Console.WriteLine(
            $"  PixelSize = {pixelSize} bit"
                + (pixelSize > 8 ? "（高位深，将对帧数据做位深转换）" : "（标准8bit，直接使用）")
        );
        const double targetBrightness = 120.0; // 目标均值灰度（0-255，约47%）
        const double tolerance = 8.0; // 收敛容差
        const int maxIterations = 12; // 最大迭代次数
        const long minExposureUs = 1_000; // 最小曝光 1 ms
        const long maxExposureUs = 2_000_000; // 最大曝光 2 s

        long exposureUs = GenICamGetInt(h, "ExposureTime");
        if (exposureUs <= 0)
            exposureUs = 20_000;
        Console.WriteLine($"  自动曝光起始: {exposureUs} μs = {exposureUs / 1000.0:F1} ms");

        double meanBrightness = 0;
        bool converged = false;

        for (int iter = 0; iter < maxIterations; iter++)
        {
            // 写入曝光时间
            GenICamSetInt(h, "ExposureTime", exposureUs);

            // 分配帧缓冲
            TUCamFrame frame = new() { uiRsdSize = 1 };
            if (TUCamNative.TUCAM_Buf_Alloc(h, ref frame) != TUCAM_SUCCESS)
            {
                Console.WriteLine("  ⚠ Buf_Alloc 失败，放弃自动曝光");
                return;
            }

            try
            {
                if (TUCamNative.TUCAM_Cap_Start(h, 0) != TUCAM_SUCCESS)
                {
                    Console.WriteLine("  ⚠ Cap_Start 失败，放弃自动曝光");
                    return;
                }

                try
                {
                    // 等待一帧
                    int waitRet = TUCamNative.TUCAM_Buf_WaitForFrame(h, ref frame, 5000);
                    if (
                        waitRet != TUCAM_SUCCESS
                        || frame.pBuffer == IntPtr.Zero
                        || frame.uiImgSize == 0
                    )
                    {
                        Console.WriteLine($"  ⚠ WaitFrame 失败 ret=0x{waitRet:X8}");
                        return;
                    }

                    // 拷贝帧数据
                    byte[] data = new byte[(int)frame.uiImgSize];
                    Marshal.Copy(frame.pBuffer + frame.usOffset, data, 0, data.Length);

                    // OpenCV 计算灰度均值（自动处理位深转换）
                    unsafe
                    {
                        fixed (byte* ptr = data)
                        {
                            using Mat bgrMat = FrameDataToMat(ptr, frame);
                            using Mat gray = new Mat();
                            Cv2.CvtColor(bgrMat, gray, ColorConversionCodes.BGR2GRAY);
                            meanBrightness = Cv2.Mean(gray).Val0;

                            Console.WriteLine(
                                $"  [迭代{iter + 1:D2}/{maxIterations}]  曝光={exposureUs, 10} μs"
                                    + $"  均值灰度={meanBrightness, 6:F1}  目标={targetBrightness}"
                            );

                            if (Math.Abs(meanBrightness - targetBrightness) <= tolerance)
                            {
                                // 收敛：保存最终图像
                                converged = true;
                                string outPath = Path.Combine(
                                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                                    $"tucam_autoexposure_{frame.usWidth}x{frame.usHeight}_{exposureUs}us.png"
                                );
                                Cv2.ImWrite(outPath, bgrMat);
                                Console.WriteLine($"  ✓ 收敛！已保存 → {outPath}");
                            }
                            else
                            {
                                // 比例调节：防止均值过小导致曝光暴增
                                double effectiveMean = Math.Max(meanBrightness, 1.0);
                                long newExposure = (long)(
                                    exposureUs * targetBrightness / effectiveMean
                                );
                                exposureUs = Math.Clamp(newExposure, minExposureUs, maxExposureUs);
                            }
                        }
                    }
                }
                finally
                {
                    TUCamNative.TUCAM_Buf_AbortWait(h);
                    TUCamNative.TUCAM_Cap_Stop(h);
                }
            }
            finally
            {
                TUCamNative.TUCAM_Buf_Release(h);
            }

            if (converged)
                break;
        }

        if (!converged)
            Console.WriteLine(
                $"  ⚠ 达到最大迭代次数（{maxIterations}次），最终亮度={meanBrightness:F1}"
            );
        Console.WriteLine($"  最终曝光时间: {exposureUs} μs = {exposureUs / 1000.0:F1} ms");
    }

    /// <summary>
    /// 通过 GenICam 整数节点读取当前值，失败返回 -1。
    /// 模式：先 GetElementValue 填充元数据，再读 IntValue.nVal。
    /// </summary>
    private static long GenICamGetInt(IntPtr h, string nodeName)
    {
        IntPtr pName = Marshal.StringToHGlobalAnsi(nodeName);
        try
        {
            TucamElement e = default;
            e.pName = pName;
            int ret = TUCamNative.TUCAM_GenICam_GetElementValue(h, ref e, 0);
            if (ret != TUCAM_SUCCESS)
            {
                Console.WriteLine($"  GenICamGetInt [{nodeName}] Get 失败 ret=0x{ret:X8}");
                return -1;
            }
            return e.uValue.IntValue.nVal;
        }
        finally
        {
            Marshal.FreeHGlobal(pName);
        }
    }

    /// <summary>
    /// 通过 GenICam 整数节点写入值。
    /// 模式：先 GetElementValue 填充元数据，再修改 IntValue.nVal 后 SetElementValue。
    /// </summary>
    private static int GenICamSetInt(IntPtr h, string nodeName, long value)
    {
        IntPtr pName = Marshal.StringToHGlobalAnsi(nodeName);
        try
        {
            TucamElement e = default;
            e.pName = pName;
            // 先 Get 获取节点元数据（类型/访问权限等）
            int getRet = TUCamNative.TUCAM_GenICam_GetElementValue(h, ref e, 0);
            if (getRet != TUCAM_SUCCESS)
            {
                Console.WriteLine($"  GenICamSetInt [{nodeName}] Get 失败 ret=0x{getRet:X8}，跳过");
                return getRet;
            }
            e.pName = pName; // SDK 可能修改 pName，重置为调用方管理的指针
            e.uValue.IntValue.nVal = value;
            return TUCamNative.TUCAM_GenICam_SetElementValue(h, ref e, 0);
        }
        finally
        {
            Marshal.FreeHGlobal(pName);
        }
    }

    private static void TestSetUserID(IntPtr h)
    {
        IntPtr pName = Marshal.StringToHGlobalAnsi("DeviceUserID");
        IntPtr pNewVal = IntPtr.Zero;
        try
        {
            TucamElement e = default;
            e.pName = pName;
            int getRet = TUCamNative.TUCAM_GenICam_GetElementValue(h, ref e, TU_CAMERA_XML);
            string cur =
                e.pTransfer != IntPtr.Zero ? Marshal.PtrToStringAnsi(e.pTransfer) ?? "" : "<null>";
            Console.WriteLine(
                $"  Get: ret={getRet}  Access={AccessName(e.Access)}  Type={TypeName(e.Type)}  current=\"{cur}\""
            );

            if (getRet == TUCAM_SUCCESS && (e.Access == 2 || e.Access == 4))
            {
                pNewVal = Marshal.StringToHGlobalAnsi("2");
                e.pTransfer = pNewVal;
                int setRet = TUCamNative.TUCAM_GenICam_SetElementValue(h, ref e, TU_CAMERA_XML);
                Console.WriteLine($"  Set(\"2\"): ret={setRet}");
                if (setRet == TUCAM_SUCCESS)
                {
                    e.pName = pName;
                    e.pTransfer = IntPtr.Zero;
                    int verRet = TUCamNative.TUCAM_GenICam_GetElementValue(h, ref e, TU_CAMERA_XML);
                    string newVal =
                        e.pTransfer != IntPtr.Zero
                            ? Marshal.PtrToStringAnsi(e.pTransfer) ?? ""
                            : "<null>";
                    Console.WriteLine($"  验证回读: ret={verRet}  value=\"{newVal}\"");
                }
            }
            else
            {
                Console.WriteLine($"  跳过写入（Access={AccessName(e.Access)} 或 Get 失败）");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pName);
            if (pNewVal != IntPtr.Zero)
                Marshal.FreeHGlobal(pNewVal);
        }
    }
}
