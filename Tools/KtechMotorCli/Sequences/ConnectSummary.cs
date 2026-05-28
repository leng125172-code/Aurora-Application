using System.Text;
using AuroraStruct3D.RS485.Ktech;
using Microsoft.Extensions.Logging;

namespace KtechMotorCli.Sequences;

/// <summary>
/// 连接成功后将 Demo formInfoRefresh / formSettingRefresh / formCalibRefresh / formUi_UpdateFromDeviceType
/// 对控件的赋值，翻译为控制台表格输出。
/// <para>下拉框 items 以 "index → value" 列举，枚举来源均标注对应 Demo 数组名。</para>
/// </summary>
internal static class ConnectSummary
{
    // ───── Demo dataStreamClass 中的枚举字符串数组 ─────────────────────────────────────

    /// <summary>encoderType[0..9]（来自 Demo dataStreamClass 构造函数）</summary>
    private static readonly string[] EncoderTypeNames =
    [
        "AS5600", // 0
        "AS5047P", // 1
        "AS5048A", // 2
        "AS5048B", // 3
        "TLE5012B", // 4
        "14Bit Encoder", // 5
        "14Bit Encoder", // 6
        "18Bit Encoder", // 7
        "21Bit Encoder", // 8
        "19Bit Encoder", // 9
    ];

    /// <summary>encoderPosition[0..1]</summary>
    private static readonly string[] EncoderPositionNames = ["Normal", "Reverse"];

    /// <summary>motorPhaseSequence[0..1]</summary>
    private static readonly string[] PhaseSequenceNames = ["Normal", "Reverse"];

    // ───── Demo comboBox items（来自 WinForms 设计器，需与 selectedIndex 对应）─────────

    /// <summary>comboBoxBusType items：index 0=无, 1=RS485, 2=CAN</summary>
    private static readonly string[] BusTypeItems = ["None", "RS485", "CAN"];

    /// <summary>comboBoxRs485Baudrate items：对应 KTECH 固件支持的常见波特率</summary>
    private static readonly string[] Rs485BaudrateItems =
    [
        "9600", // 0
        "19200", // 1
        "38400", // 2
        "57600", // 3
        "115200", // 4
        "230400", // 5
        "460800", // 6
        "921600", // 7
    ];

    /// <summary>comboBoxCanBaudrate items</summary>
    private static readonly string[] CanBaudrateItems =
    [
        "250 kbps", // 0
        "500 kbps", // 1
        "1 Mbps", // 2
    ];

    /// <summary>comboBoxBroadcastMode items：0=Disable, 1=Enable</summary>
    private static readonly string[] BroadcastModeItems = ["Disable", "Enable"];

    /// <summary>comboBoxSpinDirection items：0=Normal, 1=Reverse</summary>
    private static readonly string[] SpinDirectionItems = ["Normal", "Reverse"];

    /// <summary>
    /// 保护开关下拉值转显示文本。Demo 中 protectXxxEnable 的语义：
    /// 0=功能不支持(不可用), 1=禁用, 2=慢速, 3=快速（但 comboBox 绑定时 index = value-1）
    /// comboBoxProtectXxxEnable items: [Disable, Slow, Fast]（index 0/1/2）
    /// </summary>
    private static readonly string[] ProtectEnableItems = ["Disable", "Slow", "Fast"];

    /// <summary>comboBoxBrakeResEnable items: [Disable, Enable]（index 0/1）</summary>
    private static readonly string[] BrakeResEnableItems = ["Disable", "Enable"];

    /// <summary>
    /// comboBoxSelectControlMode items（MS 型 0x2011 对应 "Open Control"，其他为 "Torque Control"）
    /// Demo formUi_UpdateFromDeviceType 按设备类型动态覆盖 index 0 项
    /// </summary>
    private static readonly string[] ControlModeItemsMs =
    [
        "Open Control",
        "Speed Control",
        "Multi Angle Control 1",
        "Multi Angle Control 2",
        "Single Angle Control 1",
        "Single Angle Control 2",
        "Increment Angle Control 1",
        "Increment Angle Control 2",
    ];
    private static readonly string[] ControlModeItemsMfMg =
    [
        "Torque Control",
        "Speed Control",
        "Multi Angle Control 1",
        "Multi Angle Control 2",
        "Single Angle Control 1",
        "Single Angle Control 2",
        "Increment Angle Control 1",
        "Increment Angle Control 2",
    ];

    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 打印 Demo 连接完成后所有界面控件的等效值。
    /// 对应 Demo timer2_Elapsed step==5 后依次调用的三个 formXxxRefresh()。
    /// </summary>
    public static void Print(
        ILogger logger,
        KtechDeviceType deviceType,
        KtechProductInfo info,
        IKtechCalib calib,
        KtechSetting setting
    )
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(calib);
        ArgumentNullException.ThrowIfNull(setting);

        var sb = new StringBuilder();

        // ── formInfoRefresh ──────────────────────────────────────────────────────
        sb.AppendLine();
        sb.AppendLine(
            "┌─────── formInfoRefresh ─────────────────────────────────────────────────┐"
        );
        sb.AppendLine($"│  Driver  : {info.DriverName, -20}  (labelDriverName)");
        sb.AppendLine($"│  Motor   : {info.MotorName, -20}  (labelMotorName)");
        sb.AppendLine($"│  HW Ver  : {info.HardwareVersion, -10}  (labelHardwareVersion)");
        sb.AppendLine($"│  Motor FW: {info.MotorVersion, -10}  (labelMotorVersion)");
        sb.AppendLine($"│  FW Ver  : {info.FirmwareVersion, -10}  (labelFirmwareVersion)");
        sb.AppendLine($"│  Chip ID : {info.ChipId}  (labelChipId)");
        sb.AppendLine(
            "└─────────────────────────────────────────────────────────────────────────┘"
        );

        // ── formUi_UpdateFromDeviceType ──────────────────────────────────────────
        sb.AppendLine();
        sb.AppendLine(
            "┌─────── formUi_UpdateFromDeviceType ─────────────────────────────────────┐"
        );
        bool isMsType = deviceType == KtechDeviceType.Ms || deviceType == KtechDeviceType.Mh;
        bool isMgType = deviceType == KtechDeviceType.Mg || deviceType == KtechDeviceType.MgE;
        string[] controlModeItems = isMsType ? ControlModeItemsMs : ControlModeItemsMfMg;
        sb.AppendLine($"│  DeviceType = {deviceType} (0x{(int)deviceType:X4})");
        if (isMsType)
        {
            sb.AppendLine("│  labelMaxTorqueCurrent → \"Max Power\" (MS型改为功率)");
            sb.AppendLine("│  label27/30 (Control label) → \"Power\"");
            sb.AppendLine("│  numericUpDownMaxTorque.Maximum = 850  (MS型 W 上限)");
        }
        else
        {
            sb.AppendLine("│  labelMaxTorqueCurrent → \"Max Torque Current\"");
            sb.AppendLine("│  label27/30 (Control label) → \"Torque Current\"");
            sb.AppendLine("│  numericUpDownMaxTorque.Maximum = 2000  (mA 上限)");
        }
        sb.AppendLine("│");
        sb.AppendLine("│  comboBoxSelectControlMode items：");
        for (int i = 0; i < controlModeItems.Length; i++)
        {
            sb.AppendLine($"│    [{i}] {controlModeItems[i]}");
        }
        sb.AppendLine("│  → SelectedIndex = 0  (默认选中第一个控制模式)");
        sb.AppendLine("│");
        if (isMgType)
        {
            sb.AppendLine("│  减速器编码器相关控件：Enabled  (MG/MG_E 型有减速器)");
        }
        else
        {
            sb.AppendLine("│  减速器编码器相关控件：Disabled  (非 MG 型无减速器)");
            sb.AppendLine("│  电机编码器零点相关控件：Enabled");
        }
        sb.AppendLine(
            "└─────────────────────────────────────────────────────────────────────────┘"
        );

        // ── formCalibRefresh ─────────────────────────────────────────────────────
        sb.AppendLine();
        sb.AppendLine(
            "┌─────── formCalibRefresh ────────────────────────────────────────────────┐"
        );
        sb.AppendLine($"│  numericUpDownMotorPoles.Value  = {calib.MotorPoles}  (极对数)");

        string encTypeName =
            calib.EncoderType <= 9 ? EncoderTypeNames[calib.EncoderType] : "Unknown";
        sb.AppendLine($"│  textBoxEncoderType.Text        = \"{encTypeName}\"");
        sb.AppendLine($"│    (encoderType[] 完整映射：)");
        for (int i = 0; i < EncoderTypeNames.Length; i++)
        {
            string mark = i == calib.EncoderType ? " ← 当前" : "";
            sb.AppendLine($"│      [{i}] {EncoderTypeNames[i]}{mark}");
        }

        string encPosName =
            calib.EncoderPos < EncoderPositionNames.Length
                ? EncoderPositionNames[calib.EncoderPos]
                : "Unknown";
        sb.AppendLine($"│  textBoxEncoderPosition.Text    = \"{encPosName}\"");
        sb.AppendLine(
            $"│    (encoderPosition[]: [0]=Normal [1]=Reverse)  ← 当前 index={calib.EncoderPos}"
        );

        sb.AppendLine(
            $"│  textBoxMotorEncoderAlignRatio.Text = \"{calib.MotorEncoderAlignRatio}\""
        );
        sb.AppendLine($"│  textBoxMotorEncoderOffset.Text     = \"{calib.MotorEncoderAlignBias}\"");

        string phaseName =
            calib.MotorPhaseSequence < PhaseSequenceNames.Length
                ? PhaseSequenceNames[calib.MotorPhaseSequence]
                : "Unknown";
        sb.AppendLine($"│  textBoxMotorPhaseSequence.Text = \"{phaseName}\"");
        sb.AppendLine(
            $"│    (motorPhaseSequence[]: [0]=Normal [1]=Reverse)  ← 当前 index={calib.MotorPhaseSequence}"
        );

        float alignVoltage = calib.MotorEncoderAlignVoltage / 100f;
        sb.AppendLine(
            $"│  numericUpDownAlignVoltage.Value = {alignVoltage:F2}  (V = raw {calib.MotorEncoderAlignVoltage} / 100)"
        );
        sb.AppendLine(
            $"│  textBoxMotorZeroPosition.Text  = \"{calib.EncoderOffset}\"  (encoder_offset)"
        );
        sb.AppendLine(
            $"│  AlignFlag={calib.MotorEncoderAlignFlag}  OffsetFlag={calib.EncoderOffsetFlag}  SavedFlag=0x{calib.SavedFlag:X8}"
        );
        sb.AppendLine(
            "└─────────────────────────────────────────────────────────────────────────┘"
        );

        // ── formSettingRefresh ───────────────────────────────────────────────────
        sb.AppendLine();
        sb.AppendLine(
            "┌─────── formSettingRefresh ──────────────────────────────────────────────┐"
        );
        sb.AppendLine($"│  numericUpDownDriverID.Value    = {setting.DriverId}");
        sb.AppendLine($"│");
        AppendComboField(sb, "comboBoxBusType", setting.BusType, BusTypeItems);
        bool rs485Enabled = setting.BusType == 1;
        bool canEnabled = setting.BusType == 2;
        AppendComboField(
            sb,
            "comboBoxRs485Baudrate",
            setting.Rs485BaudRate,
            Rs485BaudrateItems,
            enabled: rs485Enabled,
            hint: rs485Enabled ? "✓ Enabled" : "✗ Disabled (BusType≠RS485)"
        );
        AppendComboField(
            sb,
            "comboBoxCanBaudrate",
            setting.CanBaudRate,
            CanBaudrateItems,
            enabled: canEnabled,
            hint: canEnabled ? "✓ Enabled" : "✗ Disabled (BusType≠CAN)"
        );
        sb.AppendLine($"│");
        AppendComboField(sb, "comboBoxBroadcastMode", setting.BroadcastMode, BroadcastModeItems);
        AppendComboField(sb, "comboBoxSpinDirection", setting.SpinDirection, SpinDirectionItems);
        sb.AppendLine($"│");
        sb.AppendLine($"│  ── 保护开关（0=不支持/不可用, value 1..3 → comboBox index 0..2）");
        AppendProtectField(
            sb,
            "MotorTemp",
            setting.ProtectMotorTempEnable,
            setting.ProtectMotorTemp,
            "℃"
        );
        AppendProtectField(
            sb,
            "DriverTemp",
            setting.ProtectDriverTempEnable,
            setting.ProtectDriverTemp,
            "℃"
        );
        AppendProtectVoltField(
            sb,
            "UnderVoltage",
            setting.ProtectUnderVoltageEnable,
            setting.ProtectUnderVoltage
        );
        AppendProtectVoltField(
            sb,
            "OverVoltage",
            setting.ProtectOverVoltageEnable,
            setting.ProtectOverVoltage
        );
        AppendProtectCurrentField(
            sb,
            "OverCurrent",
            setting.ProtectOverCurrentEnable,
            setting.ProtectOverCurrent,
            setting.ProtectOverCurrentTime
        );
        AppendProtectTimedField(sb, "Stall", setting.ProtectStallEnable, setting.ProtectStallTime);
        AppendProtectTimedField(
            sb,
            "LostInput",
            setting.ProtectLostInputEnable,
            setting.ProtectLostInputTime
        );
        sb.AppendLine($"│");
        sb.AppendLine($"│  ── 制动电阻（comboBoxBrakeResEnable items: [0]=Disable [1]=Enable）");
        bool brakeEnabled = setting.BrakeResEnable >= 2;
        string brakeIdx = setting.BrakeResEnable switch
        {
            0 => "不支持/不可用",
            1 => "0 (Disable)",
            2 => "1 (Enable)",
            _ => $"? (raw={setting.BrakeResEnable})",
        };
        sb.AppendLine(
            $"│    comboBoxBrakeResEnable.SelectedIndex = {brakeIdx}  Enabled={brakeEnabled}"
        );
        sb.AppendLine(
            $"│    numericUpDownBrakeResOnVoltage.Value = {setting.BrakeResOnVoltage / 100m:F2} V  (raw/100)"
        );
        sb.AppendLine($"│");
        sb.AppendLine($"│  ── PID 参数");
        sb.AppendLine(
            $"│    Angle  PID  Kp={setting.AnglePidKp}  Ki={setting.AnglePidKi}  Kd={setting.AnglePidKd}"
        );
        sb.AppendLine(
            $"│    Speed  PID  Kp={setting.SpeedPidKp}  Ki={setting.SpeedPidKi}  Kd={setting.SpeedPidKd}"
        );
        string currentNote = isMsType ? "(MS型 currentKp/Ki 控件 Disabled)" : "";
        sb.AppendLine(
            $"│    Current PID  Kp={setting.CurrentPidKp}  Ki={setting.CurrentPidKi}  Kd={setting.CurrentPidKd}  {currentNote}"
        );
        sb.AppendLine($"│    currentRamp = {setting.CurrentRamp}");
        sb.AppendLine($"│");
        sb.AppendLine($"│  ── 运动限制");
        string torqueLabel = isMsType ? "Max Power (W)" : "Max Torque Current (mA)";
        sb.AppendLine(
            $"│    numericUpDownMaxTorque.Value  = {setting.MaxTorque}   ({torqueLabel})"
        );
        sb.AppendLine(
            $"│    numericUpDownMaxSpeed.Value   = {setting.MaxSpeed / 100m:F2}  dps  (raw {setting.MaxSpeed} / 100)"
        );
        sb.AppendLine(
            $"│    numericUpDownMaxAngle.Value   = {setting.MaxAngle / 100m:F2}  °   (raw {setting.MaxAngle} / 100)"
        );
        sb.AppendLine($"│    numericUpDownSpeedRamp.Value  = {setting.SpeedRamp}  (dps/s)");
        sb.AppendLine($"│");
        sb.AppendLine(
            $"│  SavedFlag = 0x{setting.SavedFlag:X8}  UniqueId = 0x{setting.UniqueId:X8}"
        );
        sb.AppendLine(
            "└─────────────────────────────────────────────────────────────────────────┘"
        );

        logger.LogInformation("{Summary}", sb.ToString());
    }

    // ─────── 辅助方法 ────────────────────────────────────────────────────────────

    private static void AppendComboField(
        StringBuilder sb,
        string name,
        byte selectedIndex,
        string[] items,
        bool? enabled = null,
        string? hint = null
    )
    {
        string selected =
            selectedIndex < items.Length ? items[selectedIndex] : $"?({selectedIndex})";
        string enableStr = enabled.HasValue ? $"  Enabled={enabled}" : "";
        sb.AppendLine(
            $"│  {name}.SelectedIndex = {selectedIndex} → \"{selected}\"{enableStr}  {hint ?? ""}"
        );
        sb.AppendLine($"│    items: " + string.Join(", ", items.Select((v, i) => $"[{i}]={v}")));
    }

    private static void AppendProtectField(
        StringBuilder sb,
        string fieldName,
        byte enable,
        byte threshold,
        string unit
    )
    {
        (string comboText, bool comboEnabled) = DecodeProtectEnable(enable);
        sb.AppendLine(
            $"│    {fieldName}: comboBox={comboText} Enabled={comboEnabled}  threshold={threshold}{unit}"
        );
    }

    private static void AppendProtectVoltField(
        StringBuilder sb,
        string fieldName,
        byte enable,
        ushort rawValue
    )
    {
        (string comboText, bool comboEnabled) = DecodeProtectEnable(enable);
        sb.AppendLine(
            $"│    {fieldName}: comboBox={comboText} Enabled={comboEnabled}  value={rawValue / 100m:F2} V  (raw/100)"
        );
    }

    private static void AppendProtectCurrentField(
        StringBuilder sb,
        string fieldName,
        byte enable,
        ushort current,
        ushort time
    )
    {
        (string comboText, bool comboEnabled) = DecodeProtectEnable(enable);
        sb.AppendLine(
            $"│    {fieldName}: comboBox={comboText} Enabled={comboEnabled}  current={current / 100m:F2} A  time={time} ms"
        );
    }

    private static void AppendProtectTimedField(
        StringBuilder sb,
        string fieldName,
        byte enable,
        ushort time
    )
    {
        (string comboText, bool comboEnabled) = DecodeProtectEnable(enable);
        sb.AppendLine(
            $"│    {fieldName}: comboBox={comboText} Enabled={comboEnabled}  time={time} ms"
        );
    }

    /// <summary>
    /// 按 Demo formSettingRefresh 的逻辑解码保护开关：
    /// 0=不支持，1=Disable(index 0), 2=Slow(index 1), 3=Fast(index 2)
    /// </summary>
    private static (string displayText, bool enabled) DecodeProtectEnable(byte value) =>
        value switch
        {
            0 => ("不支持（控件 Disabled）", false),
            1 => ($"[0]={ProtectEnableItems[0]}", true),
            2 => ($"[1]={ProtectEnableItems[1]}", true),
            3 => ($"[2]={ProtectEnableItems[2]}", true),
            _ => ($"?({value})", false),
        };
}
