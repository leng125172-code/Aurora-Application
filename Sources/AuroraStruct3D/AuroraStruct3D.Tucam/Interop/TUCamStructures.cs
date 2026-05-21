using System.Runtime.InteropServices;

namespace AuroraStruct3D.Tucam.Interop;

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
