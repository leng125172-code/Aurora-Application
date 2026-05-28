// 从 Documents/瓴控Demo/serovMotor/saveCalibMsMfMh_struct.cs 拷贝（仅改 namespace 与 Sequential 显式标注）
using System.Runtime.InteropServices;

namespace KtechStructSizeProbe;

[StructLayout(LayoutKind.Sequential)]
public struct saveCalibMsMfMh_struct
{
    public byte motorPoles;
    public byte encoderType;
    public byte encoderPos;
    public byte motorPhaseSequence;
    public uint motorEncoder_alignBias;
    public ushort motorEncoder_alignRatio;
    public ushort motorEncoder_alignVoltage;
    public byte motorEncoder_alignFlag;
    public uint encoder_offset;
    public byte encoder_offsetFlag;
    public uint savedFlag;
}
