using System.Runtime.InteropServices;

namespace AuroraStruct3D.Cameras.Tucam.Interop;

/// <summary>
/// SDK初始化参数结构体（对应 TUCAM_INIT）
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct TUCamInit
{
    /// <summary>相机数量（输出）</summary>
    public uint uiCamCount;

    /// <summary>SDK路径（输入，可为空）</summary>
    public IntPtr pstrConfigPath;
}

/// <summary>
/// 打开相机参数结构体（对应 TUCAM_OPEN）
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct TUCamOpen
{
    /// <summary>相机索引（输入，从0开始）</summary>
    public uint uiIdxOpen;

    /// <summary>相机句柄（输出）</summary>
    public IntPtr hIdxTUCam;
}

/// <summary>
/// 相机字符串信息结构体（对应 TUCAM_VALUE_INFO）
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct TUCamValueInfo
{
    /// <summary>信息ID（TUCAM_IDINFO）</summary>
    public int nId;

    /// <summary>数值信息</summary>
    public int nValue;

    /// <summary>文本缓冲区指针</summary>
    public IntPtr pText;

    /// <summary>文本缓冲区大小</summary>
    public int nTextSize;
}

/// <summary>
/// 能力属性结构体（对应 TUCAM_CAPA_ATTR）
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct TUCamCapaAttr
{
    /// <summary>能力ID（输入）</summary>
    public int idCapa;

    /// <summary>最小值（输出）</summary>
    public int nValMin;

    /// <summary>最大值（输出）</summary>
    public int nValMax;

    /// <summary>默认值（输出）</summary>
    public int nValDft;

    /// <summary>步进值（输出）</summary>
    public int nValStep;
}

/// <summary>
/// 属性特征结构体（对应 TUCAM_PROP_ATTR）
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct TUCamPropAttr
{
    /// <summary>属性ID（输入）</summary>
    public int idProp;

    /// <summary>通道索引（输入/输出）</summary>
    public int nIdxChn;

    /// <summary>最小值（输出）</summary>
    public double dbValMin;

    /// <summary>最大值（输出）</summary>
    public double dbValMax;

    /// <summary>默认值（输出）</summary>
    public double dbValDft;

    /// <summary>步进值（输出）</summary>
    public double dbValStep;
}

/// <summary>
/// ROI区域属性结构体（对应 TUCAM_ROI_ATTR）
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct TUCamRoiAttr
{
    /// <summary>ROI使能</summary>
    public int bEnable;

    /// <summary>水平偏移</summary>
    public int nHOffset;

    /// <summary>垂直偏移</summary>
    public int nVOffset;

    /// <summary>宽度</summary>
    public int nWidth;

    /// <summary>高度</summary>
    public int nHeight;
}

/// <summary>
/// 触发属性结构体（对应 TUCAM_TRIGGER_ATTR）
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct TUCamTriggerAttr
{
    /// <summary>触发模式</summary>
    public int nTgrMode;

    /// <summary>曝光模式（0:宽度电平 1:曝光时间）</summary>
    public int nExpMode;

    /// <summary>边沿模式（0:上升沿 1:下降沿）</summary>
    public int nEdgeMode;

    /// <summary>延迟时间</summary>
    public int nDelayTm;

    /// <summary>每次触发的帧数</summary>
    public int nFrames;

    /// <summary>缓冲区帧数</summary>
    public int nBufFrames;
}

/// <summary>
/// 帧数据结构体（对应 TUCAM_FRAME）
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct TUCamFrame
{
    /// <summary>版权+版本签名（8字节）</summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public byte[] szSignature;

    /// <summary>帧头大小</summary>
    public ushort usHeader;

    /// <summary>帧数据偏移</summary>
    public ushort usOffset;

    /// <summary>帧宽度</summary>
    public ushort usWidth;

    /// <summary>帧高度</summary>
    public ushort usHeight;

    /// <summary>帧行步长</summary>
    public uint uiWidthStep;

    /// <summary>像素位深度</summary>
    public byte ucDepth;

    /// <summary>数据格式</summary>
    public byte ucFormat;

    /// <summary>通道数</summary>
    public byte ucChannels;

    /// <summary>每像素字节数</summary>
    public byte ucElemBytes;

    /// <summary>期望的帧数据格式（输入）</summary>
    public byte ucFormatGet;

    /// <summary>帧索引（输入/输出）</summary>
    public uint uiIndex;

    /// <summary>帧大小</summary>
    public uint uiImgSize;

    /// <summary>保留帧数（输入，期望获取的帧数）</summary>
    public uint uiRsdSize;

    /// <summary>直方图大小</summary>
    public uint uiHstSize;

    /// <summary>图像数据缓冲区指针</summary>
    public IntPtr pBuffer;
}

/// <summary>
/// 触发输出属性结构体（对应 TUCAM_TRGOUT_ATTR）
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct TUCamTrgOutAttr
{
    /// <summary>输出端口编号（输入/输出）</summary>
    public int nTgrOutPort;

    /// <summary>输出模式/信号来源（输入/输出）</summary>
    public int nTgrOutMode;

    /// <summary>边沿模式（0:上升沿 1:下降沿）</summary>
    public int nEdgeMode;

    /// <summary>延迟时间（输入/输出）</summary>
    public int nDelayTm;

    /// <summary>脉冲宽度（输入/输出）</summary>
    public int nWidth;
}

/// <summary>
/// 计算ROI属性结构体（用于白平衡/AE区域，对应 TUCAM_CALC_ROI_ATTR）
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct TUCamCalcRoiAttr
{
    /// <summary>ROI使能开关</summary>
    public int bEnable;

    /// <summary>计算类型ID（TUCAM_IDCROI）</summary>
    public int idCalc;

    /// <summary>水平偏移</summary>
    public int nHOffset;

    /// <summary>垂直偏移</summary>
    public int nVOffset;

    /// <summary>ROI宽度</summary>
    public int nWidth;

    /// <summary>ROI高度</summary>
    public int nHeight;
}

/// <summary>
/// 寄存器读写结构体（对应 TUCAM_REG_RW）
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct TUCamRegRw
{
    /// <summary>寄存器类型（TUREG_SN / TUREG_DATA 等）</summary>
    public int nRegType;

    /// <summary>数据缓冲区指针</summary>
    public IntPtr pBuf;

    /// <summary>缓冲区大小（字节）</summary>
    public int nBufSize;
}

// ─────────────────────────────────────────────────────────────────────────────
// GenICam 节点访问类型定义（对应 SDK TUDefine.h 中的 GenICam 部分）
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>GenICam 节点类型枚举（对应 SDK TUELEM_TYPE）</summary>
public enum TuElemType : int
{
    /// <summary>IValue 接口</summary>
    Value = 0x00,

    /// <summary>IBase 接口</summary>
    Base = 0x01,

    /// <summary>IInteger 接口（整数）</summary>
    Integer = 0x02,

    /// <summary>IBoolean 接口（布尔）</summary>
    Boolean = 0x03,

    /// <summary>ICommand 接口（命令）</summary>
    Command = 0x04,

    /// <summary>IFloat 接口（浮点）</summary>
    Float = 0x05,

    /// <summary>IString 接口（字符串）</summary>
    String = 0x06,

    /// <summary>IRegister 接口（寄存器）</summary>
    Register = 0x07,

    /// <summary>ICategory 接口（类别）</summary>
    Category = 0x08,

    /// <summary>IEnumeration 接口（枚举）</summary>
    Enumeration = 0x09,

    /// <summary>IEnumEntry 接口（枚举条目）</summary>
    EnumEntry = 0x0A,

    /// <summary>IPort 接口（端口）</summary>
    Port = 0x0B,
}

/// <summary>GenICam 访问模式枚举（对应 SDK TUACCESS_MODE）</summary>
public enum TuAccessMode : int
{
    /// <summary>未实现</summary>
    NotImplemented = 0x00,

    /// <summary>不可用</summary>
    NotAvailable = 0x01,

    /// <summary>只写</summary>
    WriteOnly = 0x02,

    /// <summary>只读</summary>
    ReadOnly = 0x03,

    /// <summary>读写</summary>
    ReadWrite = 0x04,
}

/// <summary>GenICam 可见性枚举（对应 SDK TU_VISIBILITY）</summary>
public enum TuVisibility : int
{
    /// <summary>初级用户可见</summary>
    Beginner = 0x00,

    /// <summary>专家用户可见</summary>
    Expert = 0x01,

    /// <summary>高级专家可见</summary>
    Guru = 0x02,

    /// <summary>不可见</summary>
    Invisible = 0x03,

    /// <summary>未定义（节点尚未初始化）</summary>
    UndefinedVisibility = 0x10,
}

/// <summary>GenICam 整数/枚举/布尔节点值部分（对应 TUCAM_ELEMENT 内联合体的整数分支，5×Int64=40字节）</summary>
[StructLayout(LayoutKind.Sequential)]
public struct TuElemValueInt
{
    /// <summary>当前值（读写）</summary>
    public long nVal;

    /// <summary>最小值（只读）</summary>
    public long nMin;

    /// <summary>最大值（只读）</summary>
    public long nMax;

    /// <summary>步进值（只读）</summary>
    public long nStep;

    /// <summary>默认值（只读）</summary>
    public long nDefault;
}

/// <summary>GenICam 浮点节点值部分（对应 TUCAM_ELEMENT 内联合体的浮点分支，5×double=40字节）</summary>
[StructLayout(LayoutKind.Sequential)]
public struct TuElemValueFloat
{
    /// <summary>当前值（读写）</summary>
    public double dbVal;

    /// <summary>最小值（只读）</summary>
    public double dbMin;

    /// <summary>最大值（只读）</summary>
    public double dbMax;

    /// <summary>步进值（只读）</summary>
    public double dbStep;

    /// <summary>默认值（只读）</summary>
    public double dbDefault;
}

/// <summary>
/// GenICam 节点值联合体（整数与浮点共享40字节内存，对应 TUCAM_ELEMENT 内 union）
/// </summary>
[StructLayout(LayoutKind.Explicit)]
public struct TuElemValueUnion
{
    /// <summary>整数/枚举/布尔分支（5×Int64 = 40字节）</summary>
    [FieldOffset(0)]
    public TuElemValueInt IntValue;

    /// <summary>浮点分支（5×double = 40字节）</summary>
    [FieldOffset(0)]
    public TuElemValueFloat FloatValue;
}

/// <summary>
/// GenICam 节点描述结构体（对应 SDK TUCAM_ELEMENT）。
/// 用于 TUCAM_GenICam_GetElementValue / TUCAM_GenICam_SetElementValue。
/// 调用前须将 pName 设置为 Marshal.StringToHGlobalAnsi 分配的 ANSI 字符串指针，
/// 调用结束后由调用方负责释放 pName 所指向的非托管内存。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct TucamElement
{
    /// <summary>是否锁定（只读）</summary>
    public byte IsLocked;

    /// <summary>层级（只读）</summary>
    public byte Level;

    /// <summary>表示方式（只读）</summary>
    public ushort Representation;

    /// <summary>节点类型（只读）</summary>
    public TuElemType Type;

    /// <summary>访问模式（只读）</summary>
    public TuAccessMode Access;

    /// <summary>可见性（只读）</summary>
    public TuVisibility Visibility;

    /// <summary>保留字段</summary>
    public int nReserve;

    /// <summary>节点值（整数/浮点联合，读写）</summary>
    public TuElemValueUnion uValue;

    /// <summary>节点名称指针（输入，由调用方分配/释放）</summary>
    public IntPtr pName;

    /// <summary>显示名称指针（输出，由SDK管理）</summary>
    public IntPtr pDisplayName;

    /// <summary>字符串/寄存器数据指针（输出，由SDK管理）</summary>
    public IntPtr pTransfer;

    /// <summary>描述文本指针（输出，由SDK管理）</summary>
    public IntPtr pDesc;

    /// <summary>单位字符串指针（输出，由SDK管理）</summary>
    public IntPtr pUnit;

    /// <summary>枚举条目列表指针（输出，由SDK管理）</summary>
    public IntPtr pEntries;

    /// <summary>轮询时间（输出，由SDK管理）</summary>
    public long PollingTime;

    /// <summary>显示精度（输出，由SDK管理）</summary>
    public long DisplayPrecision;
}
