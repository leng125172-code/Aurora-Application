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

    /// <summary>
    /// ⛔ 在 RK3588 ARM64 上此 SDK API 返回 NotSupport，禁止调用。
    /// 软件触发必须通过 GenICam 命令节点执行：
    /// <c>GenICamSetInt(handle, "TriggerSoftwarePulse", 1)</c>（即 DoSoftwareTriggerAsync 内部实现）。
    /// </summary>
    [Obsolete(
        "RK3588 不支持此 API，请调用 ITucamCameraService.DoSoftwareTriggerAsync() 替代。",
        error: true
    )]
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Cap_DoSoftwareTrigger(IntPtr hTUCam, uint uiMode = 0);

    /// <summary>开始采集（uiMode 见 TUCamCaptureMode）</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Cap_Start(IntPtr hTUCam, uint uiMode);

    /// <summary>停止采集</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Cap_Stop(IntPtr hTUCam);

    //
    // ── 触发输出控制 ──────────────────────────────────────────────────────────────
    //

    /// <summary>设置触发输出参数（TUCAM_Cap_SetTriggerOut）</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Cap_SetTriggerOut(
        IntPtr hTUCam,
        TUCamTrgOutAttr tgroutAttr
    );

    /// <summary>获取触发输出参数（TUCAM_Cap_GetTriggerOut）</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Cap_GetTriggerOut(
        IntPtr hTUCam,
        ref TUCamTrgOutAttr pTgrOutAttr
    );

    //
    // ── 计算ROI（测光/白平衡区域）────────────────────────────────────────────────
    //

    /// <summary>设置计算用ROI区域（用于白平衡/AE测光区域）</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Calc_SetROI(IntPtr hTUCam, TUCamCalcRoiAttr roiAttr);

    /// <summary>获取计算用ROI区域</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Calc_GetROI(IntPtr hTUCam, ref TUCamCalcRoiAttr pRoiAttr);

    //
    // ── 供应商属性控制 ────────────────────────────────────────────────────────────
    //

    /// <summary>获取供应商属性值</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Vendor_Prop_GetValue(
        IntPtr hTUCam,
        int nProp,
        ref double pdbVal,
        int nChn = 0
    );

    /// <summary>设置供应商属性值</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Vendor_Prop_SetValue(
        IntPtr hTUCam,
        int nProp,
        double dbVal,
        int nChn = 0
    );

    //
    // ── 寄存器读写 ────────────────────────────────────────────────────────────────
    //

    /// <summary>读取寄存器（可用于读取序列号 TUREG_SN）</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_Reg_Read(IntPtr hTUCam, ref TUCamRegRw regRW);

    //
    // ── 用户配置文件 ──────────────────────────────────────────────────────────────
    //

    /// <summary>加载用户配置文件（需在采集停止后调用）</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern TUCamRet TUCAM_File_LoadProfiles(
        IntPtr hTUCam,
        [MarshalAs(UnmanagedType.LPStr)] string pPrfName
    );

    /// <summary>保存用户配置文件</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern TUCamRet TUCAM_File_SaveProfiles(
        IntPtr hTUCam,
        [MarshalAs(UnmanagedType.LPStr)] string pPrfName
    );

    //
    // ── GenICam 节点访问 ──────────────────────────────────────────────────────────
    //

    /// <summary>
    /// 查询 GenICam 节点属性元数据（含类型、访问模式、值范围等）。
    /// pName 为节点名称指针（独立参数），pNote 用于接收节点元数据。
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_GenICam_ElementAttr(
        IntPtr hTUCam,
        ref TucamElement pNote,
        IntPtr pName,
        int xml
    );

    /// <summary>
    /// 枚举 GenICam 节点：传入上一个节点名称（或种子 "Root" / 空 / null），SDK 返回下一个节点元数据。
    /// 用于动态遍历整个 NodeMap，配合 ElementAttr 使用。
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_GenICam_ElementAttrNext(
        IntPtr hTUCam,
        ref TucamElement pNote,
        IntPtr pName,
        int xml
    );

    /// <summary>
    /// 读取 GenICam 节点的当前值及元数据（含范围/默认值等）。
    /// 调用前须将 pNote.pName 设为指向节点名称的 ANSI 字符串指针（由调用方管理内存）。
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_GenICam_GetElementValue(
        IntPtr hTUCam,
        ref TucamElement pNote,
        int xml
    );

    /// <summary>
    /// 写入 GenICam 节点值。
    /// 调用前须先调用 TUCAM_GenICam_GetElementValue 填充节点元数据，
    /// 再修改 uValue 后调用此接口，pNote.pName 须在整个调用过程中保持有效。
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern TUCamRet TUCAM_GenICam_SetElementValue(
        IntPtr hTUCam,
        ref TucamElement pNote,
        int xml
    );
}
