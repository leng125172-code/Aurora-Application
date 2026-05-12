using System.Runtime.InteropServices;

namespace AuroraStruct3D.Tucam.Interop;

/// <summary>
/// TUCam SDK 原生P/Invoke声明。
/// Windows使用 TUCam.dll，Linux(ARM64)使用 libTUCam.so。
/// </summary>
public static class TUCamNative
{
    // 根据运行平台选择不同的库名称
#if RUNTIME_LINUX
    private const string DllName = "libTUCam.so";
#else
    private const string DllName = "TUCam.dll";
#endif

    //
    // ── 初始化与反初始化 ──────────────────────────────────────────────────────────
    //

    /// <summary>初始化SDK，获取连接的相机数量</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Api_Init(ref TUCamInit pInitParam, int nTimeOut = 1000);

    /// <summary>反初始化SDK，释放所有相机资源</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Api_Uninit();

    //
    // ── 设备操作 ──────────────────────────────────────────────────────────────────
    //

    /// <summary>打开指定索引的相机</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Dev_Open(ref TUCamOpen pOpenParam);

    /// <summary>关闭相机</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Dev_Close(IntPtr hTUCam);

    /// <summary>获取相机信息（打开后调用）</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Dev_GetInfo(IntPtr hTUCam, ref TUCamValueInfo pInfo);

    /// <summary>获取相机信息（初始化后即可调用，使用相机索引）</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Dev_GetInfoEx(uint uiICam, ref TUCamValueInfo pInfo);

    //
    // ── 能力控制 ──────────────────────────────────────────────────────────────────
    //

    /// <summary>获取能力属性范围</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Capa_GetAttr(IntPtr hTUCam, ref TUCamCapaAttr pAttr);

    /// <summary>获取能力当前值</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Capa_GetValue(IntPtr hTUCam, int nCapa, ref int pnVal);

    /// <summary>设置能力值</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Capa_SetValue(IntPtr hTUCam, int nCapa, int nVal);

    //
    // ── 属性控制 ──────────────────────────────────────────────────────────────────
    //

    /// <summary>获取属性范围</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Prop_GetAttr(IntPtr hTUCam, ref TUCamPropAttr pAttr);

    /// <summary>获取属性当前值</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Prop_GetValue(
        IntPtr hTUCam,
        int nProp,
        ref double pdbVal,
        int nChn = 0
    );

    /// <summary>设置属性值</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Prop_SetValue(
        IntPtr hTUCam,
        int nProp,
        double dbVal,
        int nChn = 0
    );

    //
    // ── 缓冲区控制 ────────────────────────────────────────────────────────────────
    //

    /// <summary>分配帧缓冲区</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Buf_Alloc(IntPtr hTUCam, ref TUCamFrame pFrame);

    /// <summary>释放帧缓冲区</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Buf_Release(IntPtr hTUCam);

    /// <summary>中止等待帧</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Buf_AbortWait(IntPtr hTUCam);

    /// <summary>等待一帧数据就绪</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Buf_WaitForFrame(
        IntPtr hTUCam,
        ref TUCamFrame pFrame,
        int nTimeOut = 1000
    );

    /// <summary>拷贝帧数据</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Buf_CopyFrame(IntPtr hTUCam, ref TUCamFrame pFrame);

    //
    // ── 采集控制 ──────────────────────────────────────────────────────────────────
    //

    /// <summary>设置ROI区域</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Cap_SetROI(IntPtr hTUCam, TUCamRoiAttr roiAttr);

    /// <summary>获取ROI区域</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Cap_GetROI(IntPtr hTUCam, ref TUCamRoiAttr pRoiAttr);

    /// <summary>设置触发参数</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Cap_SetTrigger(IntPtr hTUCam, TUCamTriggerAttr tgrAttr);

    /// <summary>获取触发参数</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Cap_GetTrigger(
        IntPtr hTUCam,
        ref TUCamTriggerAttr pTgrAttr
    );

    /// <summary>发出软件触发信号</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Cap_DoSoftwareTrigger(IntPtr hTUCam, uint uiMode = 0);

    /// <summary>开始采集（uiMode 见 TUCamCaptureMode）</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Cap_Start(IntPtr hTUCam, uint uiMode);

    /// <summary>停止采集</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Cap_Stop(IntPtr hTUCam);
}
