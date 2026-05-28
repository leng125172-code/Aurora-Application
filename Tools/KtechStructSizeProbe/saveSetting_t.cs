// 从 Documents/瓴控Demo/serovMotor/saveSetting_t.cs 拷贝
using System.Runtime.InteropServices;

namespace KtechStructSizeProbe;

[StructLayout(LayoutKind.Sequential)]
public struct saveSetting_t
{
    public byte driverId;
    public byte busType;
    public byte rs485BaudRate;
    public byte canBaudRate;
    public byte broadcastMode;
    public byte spinDirection;
    public byte protectMotorTempEnable;
    public byte protectDriverTempEnable;
    public byte protectUnderVoltageEnable;
    public byte protectOverVoltageEnable;
    public byte protectOverCurrentEnable;
    public byte protectShortCircuitEnable;
    public byte protectStallEnable;
    public byte protectLostInputEnable;
    public byte protectMotorTemp;
    public byte protectDriverTemp;
    public ushort protectUnderVoltage;
    public ushort protectOverVoltage;
    public ushort protectOverCurrent;
    public ushort protectOverCurrentTime;
    public ushort protectStallTime;
    public ushort protectLostInputTime;
    public byte brakeResEnable;
    public ushort brakeResOnVoltage;
    public byte inputType;
    public byte pwmInputControlMode;
    public ushort pwmInputMinValue;
    public ushort pwmInputMaxValue;
    public ushort pwmInputCenterValue;
    public ushort pwmInputDeadband;
    public ushort pwmToTorqueRatio;
    public ushort pwmToSpeedRatio;
    public ushort pwmToAngleRatio;
    public ushort pulsesPerCircle;
    public ushort anglePidKp;
    public ushort anglePidKi;
    public ushort anglePidKd;
    public ushort speedPidKp;
    public ushort speedPidKi;
    public ushort speedPidKd;
    public ushort currentPidKp;
    public ushort currentPidKi;
    public ushort currentPidKd;
    public short maxTorque;
    public int maxSpeed;
    public long maxAngle;
    public short currentRamp;
    public int speedRamp;
    public uint uniqueId;
    public uint savedFlag;
}
