using System;
using System.ComponentModel;
using System.Drawing;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Timers;
using System.Windows.Forms;
using WindowsFormsApplication1.Properties;

namespace serovMotor;

public class FormMainSetting : Form
{
	private delegate void NowDownloadProgress(int nowValue);

	private delegate void DownloadFinish(bool finish);

	private delegate void SerialSettingReset();

	public System.Timers.Timer timer1 = new System.Timers.Timer();

	public System.Timers.Timer timer2 = new System.Timers.Timer();

	private Ymodem ymodem = new Ymodem();

	private Thread downloadThread;

	public byte deviceConnectStep;

	public bool deviceConnected;

	public byte cmdWaitAck;

	public byte rs485Id;

	public ulong commandErrorCount;

	private bool comPortClosing;

	private bool Listening;

	private string firmwareName = "";

	public saveSetting_t saveSetting;

	public saveCalibMsMfMh_struct saveCalibMsMfMh;

	public saveCalibMg_struct saveCalibMg;

	public dataStreamClass dataStream = new dataStreamClass();

	public ushort deviceType;

	public ushort deviceTypePrevious;

	public short busVoltageValue;

	public short busCurrentValue;

	public byte MotorErrorFlagValue;

	public sbyte motorTemperatureValue;

	public short torqueCurrentValue;

	public short speedValue;

	public ushort encoderValue;

	public short iaValue;

	public short ibValue;

	public short icValue;

	public long angleValue;

	public uint singleAngleValue;

	private IContainer components;

	private ComboBox comboBoxSelectCom;

	private Label label1;

	private Button buttonConnect;

	private Label label6;

	private ComboBox comboBoxSelectBaudRate;

	private NumericUpDown numericUpDownSelectId;

	private Label label23;

	private TabPage tabPage1;

	private Label labelMotorName;

	private Button buttonReadProductInfo;

	private Label labelFirmwareVersoin;

	private Label labelDriverName;

	private Label labelHardwareVersion;

	private TabPage tabPage2;

	private TextBox textBoxEncoderType;

	private TextBox textBoxMotorPhaseSequence;

	private TextBox textBoxMotorEncoderOffset;

	private TextBox textBoxMotorZeroPosition;

	private TextBox textBoxMotorEncoderAlignRatio;

	private Button buttonReadSaveCalib;

	private Button buttonWriteSaveCalib;

	private NumericUpDown numericUpDownAlignVoltage;

	private Label label9;

	private Label label8;

	private Label label7;

	private Button buttonSetReducerEncoderZeroPosition;

	private Label label5;

	private Button buttonMotorEncoderAlign;

	private Label label4;

	private Label label3;

	private Label label2;

	private NumericUpDown numericUpDownMotorPoles;

	private TabPage tabPage3;

	private NumericUpDown numericUpDownCurrentKi;

	private Label labelMaxTorqueCurrent;

	private NumericUpDown numericUpDownMaxTorque;

	private NumericUpDown numericUpDownCurrentKp;

	private Button buttonReadSaveSetting;

	private NumericUpDown numericUpDownSpeedRamp;

	private NumericUpDown numericUpDownMaxSpeed;

	private Label labelSpeedRamp;

	private Label labelMaxSpeed;

	private NumericUpDown numericUpDownSpeedKi;

	private NumericUpDown numericUpDownSpeedKp;

	private Button buttonWriteSaveSetting;

	private NumericUpDown numericUpDownMaxAngle;

	private Label labelMaxAngle;

	private NumericUpDown numericUpDownAngleKi;

	private Label label17;

	private NumericUpDown numericUpDownAngleKp;

	private Label label16;

	private NumericUpDown numericUpDownDriverID;

	private ComboBox comboBoxRs485Baudrate;

	private Label label15;

	private Label label13;

	private TabPage tabPage4;

	private Button buttonMotorOff;

	private Panel panel1;

	private NumericUpDown numericUpDownControlAngle;

	private Button buttonSendControlCommand;

	private NumericUpDown numericUpDownControlSpeed;

	private Label label35;

	private CheckBox checkBoxControlReverse;

	private Label label36;

	private RichTextBox richTextBoxLog;

	private Label label38;

	private Label label12;

	private Label labelErrorCount;

	private Label label25;

	private Label label26;

	private Label label27;

	private Panel panelWriteProgressFull;

	private Button buttonOpenFirmware;

	private Button buttonWriteFirmware;

	private Panel panelWriteProgress;

	private TextBox textBoxFilePath;

	private Button buttonSetAnglePidRam;

	private TextBox textBoxEncoderPosition;

	private Label label28;

	private Label labelMotorVersion;

	private Label labelChipId;

	private PictureBox pictureBox1;

	private TabPage tabPage5;

	private Label label46;

	private PictureBox pictureBox3;

	private Label label47;

	private PictureBox pictureBox2;

	private PictureBox pictureBox4;

	private ComboBox comboBoxCanBaudrate;

	private Label label45;

	private ComboBox comboBoxBusType;

	private Label label48;

	private ComboBox comboBoxBroadcastMode;

	private Label label50;

	private ComboBox comboBoxSpinDirection;

	private Label label49;

	private Panel panel2;

	private ComboBox comboBoxSelectControlMode;

	private Label label29;

	private NumericUpDown numericUpDownControlTorque;

	private Label label30;

	private Label label32;

	private Label labelEncoder;

	private Label labelSpeed;

	private Label labelIq;

	private Label labelMotorTemperature;

	private Label labelBusVoltage;

	private Panel panel6;

	private Panel panel7;

	private TabControl TabControl;

	private Panel panel8;

	private Label label33;

	private Label label34;

	private Label label37;

	private Panel panel3;

	private ComboBox comboBoxProtectDriverTempEnable;

	private NumericUpDown numericUpDownProtectDriverTemp;

	private Label label31;

	private ComboBox comboBoxBrakeResEnable;

	private ComboBox comboBoxProtectLostInputEnable;

	private ComboBox comboBoxProtectOverVoltageEnable;

	private ComboBox comboBoxProtectUnderVoltageEnable;

	private ComboBox comboBoxProtectMotorTempEnable;

	private NumericUpDown numericUpDownBrakeResOnVoltage;

	private Label label41;

	private NumericUpDown numericUpDownProtectOverVoltage;

	private Label label51;

	private Label label39;

	private NumericUpDown numericUpDownProtectMotorTemp;

	private Label label40;

	private NumericUpDown numericUpDownProtectUnderVoltage;

	private NumericUpDown numericUpDownProtectLostInputTime;

	private Label label14;

	private Button buttonSetCurrentPidRam;

	private Button buttonSetSpeedPidRam;

	private Panel panel4;

	private Panel panel9;

	private Panel panel5;

	private Label label10;

	private Label label11;

	private Panel panel10;

	private Label label20;

	private Panel panel11;

	private Label label42;

	private ComboBox comboBoxProtectOverCurrentEnable;

	private NumericUpDown numericUpDownProtectOverCurrent;

	private Label label21;

	private Label label43;

	private ComboBox comboBoxProtectShortCircuitEnable;

	private Label label44;

	private Label label53;

	private ComboBox comboBoxProtectStallEnable;

	private NumericUpDown numericUpDownProtectStallTime;

	private Label label52;

	private NumericUpDown numericUpDownProtectOverCurrentTime;

	private Panel panel12;

	private Button buttonClearLog;

	private TextBox textBoxReducerZeroPosition;

	private Label label56;

	private Button buttonReducerEncoderAlign;

	private Label label55;

	private TextBox textBoxReducerAlignValue;

	private Label label54;

	private NumericUpDown numericUpDownReductionRatio;

	private Panel panel15;

	private Panel panel13;

	private Panel panel14;

	private Button buttonSetMotorEncoderZeroPosition;

	private Label label58;

	private Label label57;

	private Panel panel16;

	private Button buttonMotorStop;

	private Button buttonReadState2;

	private Button buttonReadState1;

	private Button buttonReadState3;

	private Button buttonReadMultiAngle;

	private Button buttonReadSingleAngle;

	private CheckBox checkBoxUnderVoltageProtection;

	private CheckBox checkBoxOverVoltageProtection;

	private CheckBox checkBoxMotorTemperatureProtection;

	private CheckBox checkBoxDriverTemperatureProtection;

	private CheckBox checkBoxLostInputProtection;

	private CheckBox checkBoxMotorStallProtection;

	private CheckBox checkBoxShortCircuitProtection;

	private CheckBox checkBoxOverCurrentProtection;

	private ToolTip toolTip1;

	private Panel panel17;

	private Panel panel19;

	private Label labelIc;

	private Label label66;

	private Label labelIb;

	private Label label64;

	private Label labelIa;

	private Label label61;

	private Panel panel18;

	private Panel panel21;

	private TextBox textBoxMultiAngle;

	private TextBox textBoxSingleAngle;

	private Button buttonMotorOn;

	private NumericUpDown numericUpDownCurrentRamp;

	private Label labelCurrentRamp;

	private Button buttonClearMotorLoops;

	private Button buttonSetMaxTorqueCurrentRam;

	private Button buttonSetSpeedRampRam;

	private Button buttonSetMaxSpeedRam;

	private Button buttonSetMotorZeroRam;

	private Button buttonClearError;

	private Button buttonMotorRestore;

	private Panel panel20;

	private Button buttonBrakeRelease;

	private Button buttonBrake;

	private Panel panel22;

	private Button buttonResetSaveSetting;

	private Button buttonRebootDevice;

	private Label label18;

	private Label labelBusCurrent;

	public FormMainSetting()
	{
		InitializeComponent();
		toolTip1.SetToolTip(checkBoxUnderVoltageProtection, "Under Voltage Protection");
		toolTip1.SetToolTip(checkBoxOverVoltageProtection, "Over Voltage Protection");
		toolTip1.SetToolTip(checkBoxDriverTemperatureProtection, "Driver Temperature Protection");
		toolTip1.SetToolTip(checkBoxMotorTemperatureProtection, "Motor Temperature Protection");
		toolTip1.SetToolTip(checkBoxOverCurrentProtection, "Over Current Protection");
		toolTip1.SetToolTip(checkBoxShortCircuitProtection, "Short Circuit Protection");
		toolTip1.SetToolTip(checkBoxMotorStallProtection, "Stall Protection");
		toolTip1.SetToolTip(checkBoxLostInputProtection, "Lost Input Protection");
		toolTip1.SetToolTip(labelMaxTorqueCurrent, "Max Torque Current");
		toolTip1.SetToolTip(labelMaxSpeed, "Max Speed(dps)");
		toolTip1.SetToolTip(labelMaxAngle, "Max Angle(degree)");
		toolTip1.SetToolTip(labelSpeedRamp, "Speed Ramp(dps/s)");
		toolTip1.SetToolTip(labelCurrentRamp, "Current Ramp");
		rs485Id = (byte)numericUpDownSelectId.Value;
		comboBoxSelectBaudRate.SelectedIndex = 4;
		General.userSerialPort.BaudRate = 115200;
		General.userSerialPort.StopBits = StopBits.One;
		General.userSerialPort.Parity = Parity.None;
		General.userSerialPort.NewLine = " ";
		General.userSerialPort.RtsEnable = false;
		General.userSerialPort.DataReceived += comPort_DataReceived;
		timer1.Elapsed += timer1_Elapsed;
		timer1.Enabled = false;
		timer1.AutoReset = false;
		timer2.Elapsed += timer2_Elapsed;
		timer2.Enabled = false;
		timer2.AutoReset = false;
	}

	private void comPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
	{
		string logString = "";
		if (comPortClosing)
		{
			return;
		}
		Listening = true;
		int n = General.userSerialPort.BytesToRead;
		byte[] buf = new byte[n];
		General.userSerialPort.Read(buf, 0, n);
		dataStream.receiveBuffer.AddRange(buf);
		Invoke((EventHandler)delegate
		{
			for (byte b = 0; b < n; b++)
			{
				logString = logString + buf[b].ToString("X2") + " ";
			}
			RichTextBox richTextBox = richTextBoxLog;
			richTextBox.Text = richTextBox.Text + "RX:  " + logString + "\n";
			richTextBoxLog.Select(richTextBoxLog.TextLength, 0);
			richTextBoxLog.ScrollToCaret();
		});
		Listening = false;
		dataStream.receiveDataParse();
		if (!dataStream.receiveSuccess || dataStream.receiveId != rs485Id)
		{
			return;
		}
		switch (dataStream.receiveCmd)
		{
		case 31:
			if (cmdWaitAck != 31 || dataStream.receiveCmdDataSize != 2)
			{
				break;
			}
			cmdWaitAck = 0;
			deviceType = (ushort)(dataStream.receiveCmdData[0] + (dataStream.receiveCmdData[1] << 8));
			timer1.Enabled = false;
			if (deviceType == 8209 || deviceType == 8225 || deviceType == 8241 || deviceType == 8242 || deviceType == 8257)
			{
				Invoke((EventHandler)delegate
				{
					formUi_UpdateFromDeviceType();
				});
			}
			else if (deviceConnectStep == 0)
			{
				MessageBox.Show("Wrong device", "Error");
			}
			else
			{
				deviceConnectStep = 11;
			}
			break;
		case 16:
			if (cmdWaitAck == 16 && dataStream.receiveCmdDataSize == 0)
			{
				cmdWaitAck = 0;
				deviceConnected = true;
				timer1.Enabled = false;
			}
			break;
		case 17:
			if (cmdWaitAck != 17 || dataStream.receiveCmdDataSize != 0)
			{
				break;
			}
			cmdWaitAck = 0;
			deviceConnected = false;
			timer1.Enabled = false;
			if (General.userSerialPort.IsOpen)
			{
				comPortClosing = true;
				while (Listening)
				{
					Application.DoEvents();
				}
				General.userSerialPort.Close();
				comPortClosing = false;
			}
			Invoke((EventHandler)delegate
			{
				buttonConnect.Enabled = true;
				buttonConnect.BackColor = SystemColors.Window;
				buttonConnect.Text = "CONNECT";
			});
			break;
		case 18:
		{
			if (cmdWaitAck != 18 || dataStream.receiveCmdDataSize != 58)
			{
				break;
			}
			cmdWaitAck = 0;
			timer1.Enabled = false;
			for (int num = 0; num < 20; num++)
			{
				dataStream.driverName[num] = dataStream.receiveCmdData[num];
			}
			for (int num2 = 0; num2 < 20; num2++)
			{
				dataStream.motorName[num2] = dataStream.receiveCmdData[num2 + 20];
			}
			for (int num3 = 0; num3 < 12; num3++)
			{
				dataStream.uniqueId[num3] = dataStream.receiveCmdData[num3 + 40];
			}
			dataStream.hardwareVersion = (ushort)(dataStream.receiveCmdData[52] + ((dataStream.receiveCmdData[53] << 8) & 0xFF00));
			dataStream.motorVersion = (ushort)(dataStream.receiveCmdData[54] + ((dataStream.receiveCmdData[55] << 8) & 0xFF00));
			dataStream.firmwareVersion = (ushort)(dataStream.receiveCmdData[56] + ((dataStream.receiveCmdData[57] << 8) & 0xFF00));
			if (deviceConnectStep == 0)
			{
				Invoke((EventHandler)delegate
				{
					formInfoRefresh();
				});
				if (dataStream.firmwareVersion < 10)
				{
					MessageBox.Show("Wrong firmware version", "Error");
				}
			}
			else if (dataStream.firmwareVersion < 10)
			{
				deviceConnectStep = 12;
			}
			break;
		}
		case 22:
			if (deviceType == 8241 || deviceType == 8242)
			{
				if (cmdWaitAck != 22 || dataStream.receiveCmdDataSize != dataStream.saveCalibMg_GetSize())
				{
					break;
				}
				cmdWaitAck = 0;
				saveCalibMg = (saveCalibMg_struct)dataStream.structUpdate(saveCalibMg.GetType(), dataStream.receiveCmdDataSize);
				timer1.Enabled = false;
				if (deviceConnectStep == 0)
				{
					Invoke((EventHandler)delegate
					{
						formCalibRefresh();
					});
				}
			}
			else
			{
				if ((deviceType != 8209 && deviceType != 8225 && deviceType != 8257) || cmdWaitAck != 22 || dataStream.receiveCmdDataSize != dataStream.saveCalibMsMfMh_GetSize())
				{
					break;
				}
				cmdWaitAck = 0;
				saveCalibMsMfMh = (saveCalibMsMfMh_struct)dataStream.structUpdate(saveCalibMsMfMh.GetType(), dataStream.receiveCmdDataSize);
				timer1.Enabled = false;
				if (deviceConnectStep == 0)
				{
					Invoke((EventHandler)delegate
					{
						formCalibRefresh();
					});
				}
			}
			break;
		case 23:
			if (cmdWaitAck == 23 && dataStream.receiveCmdDataSize == 0)
			{
				cmdWaitAck = 0;
				timer1.Enabled = false;
			}
			break;
		case 20:
			if (cmdWaitAck != 20 || dataStream.receiveCmdDataSize != dataStream.saveSetting_GetSize())
			{
				break;
			}
			cmdWaitAck = 0;
			saveSetting = (saveSetting_t)dataStream.structUpdate(saveSetting.GetType(), dataStream.receiveCmdDataSize);
			timer1.Enabled = false;
			if (deviceConnectStep == 0)
			{
				Invoke((EventHandler)delegate
				{
					formSettingRefresh();
				});
			}
			break;
		case 21:
			if (cmdWaitAck == 21 && dataStream.receiveCmdDataSize == 0)
			{
				cmdWaitAck = 0;
				timer1.Enabled = false;
			}
			break;
		case 72:
			if (cmdWaitAck == 72 && dataStream.receiveCmdDataSize == 0)
			{
				cmdWaitAck = 0;
				timer1.Enabled = false;
			}
			break;
		case 24:
			if (deviceType == 8241 || deviceType == 8242)
			{
				if (cmdWaitAck == 24 && dataStream.receiveCmdDataSize == 7)
				{
					cmdWaitAck = 0;
					timer1.Enabled = false;
					Invoke((EventHandler)delegate
					{
						formCalibAlignRatioRefresh();
					});
				}
			}
			else if ((deviceType == 8209 || deviceType == 8225 || deviceType == 8257) && cmdWaitAck == 24 && dataStream.receiveCmdDataSize == 7)
			{
				cmdWaitAck = 0;
				timer1.Enabled = false;
				Invoke((EventHandler)delegate
				{
					formCalibAlignRatioRefresh();
				});
			}
			break;
		case 25:
			if (deviceType == 8241 || deviceType == 8242)
			{
				if (cmdWaitAck == 25 && dataStream.receiveCmdDataSize == 7)
				{
					cmdWaitAck = 0;
					timer1.Enabled = false;
					Invoke((EventHandler)delegate
					{
						formCalibEncoderOffsetRefresh();
					});
				}
			}
			else if ((deviceType == 8209 || deviceType == 8225 || deviceType == 8257) && cmdWaitAck == 25 && dataStream.receiveCmdDataSize == 7)
			{
				cmdWaitAck = 0;
				timer1.Enabled = false;
				Invoke((EventHandler)delegate
				{
					formCalibEncoderOffsetRefresh();
				});
			}
			break;
		case 32:
			if (cmdWaitAck == 32 && dataStream.receiveCmdDataSize == dataStream.saveCalibMg_GetSize())
			{
				cmdWaitAck = 0;
				saveCalibMg = (saveCalibMg_struct)dataStream.structUpdate(saveCalibMg.GetType(), dataStream.receiveCmdDataSize);
				timer1.Enabled = false;
				Invoke((EventHandler)delegate
				{
					buttonReducerEncoderAlign.Enabled = true;
					formCalibRefresh();
				});
			}
			break;
		case 146:
			if (cmdWaitAck == 146 && dataStream.receiveCmdDataSize == 8)
			{
				cmdWaitAck = 0;
				timer1.Enabled = false;
				formMotorAngleRefresh();
			}
			break;
		case 147:
			if (cmdWaitAck == 147 && dataStream.receiveCmdDataSize == 0)
			{
				cmdWaitAck = 0;
				timer1.Enabled = false;
			}
			break;
		case 148:
			if (cmdWaitAck == 148 && dataStream.receiveCmdDataSize == 4)
			{
				cmdWaitAck = 0;
				timer1.Enabled = false;
				formMotorSingleAngleRefresh();
			}
			break;
		case 149:
			if (cmdWaitAck == 149 && dataStream.receiveCmdDataSize == 0)
			{
				cmdWaitAck = 0;
				timer1.Enabled = false;
			}
			break;
		case 154:
			if (cmdWaitAck == 154 && dataStream.receiveCmdDataSize == 7)
			{
				cmdWaitAck = 0;
				timer1.Enabled = false;
				formMotorState1Refresh();
			}
			break;
		case 155:
			if (cmdWaitAck == 155 && dataStream.receiveCmdDataSize == 7)
			{
				cmdWaitAck = 0;
				timer1.Enabled = false;
				formMotorState1Refresh();
			}
			break;
		case 156:
			if (cmdWaitAck == 156 && dataStream.receiveCmdDataSize == 7)
			{
				cmdWaitAck = 0;
				timer1.Enabled = false;
				formMotorState2Refresh();
			}
			break;
		case 157:
			if (cmdWaitAck == 157 && dataStream.receiveCmdDataSize == 7)
			{
				cmdWaitAck = 0;
				timer1.Enabled = false;
				formMotorState3Refresh();
			}
			break;
		case 128:
			if (cmdWaitAck == 128 && dataStream.receiveCmdDataSize == 0)
			{
				cmdWaitAck = 0;
				timer1.Enabled = false;
			}
			break;
		case 66:
			if (cmdWaitAck == 66 && dataStream.receiveCmdDataSize == 2)
			{
				cmdWaitAck = 0;
				timer1.Enabled = false;
			}
			break;
		case 136:
			if (cmdWaitAck == 136 && dataStream.receiveCmdDataSize == 0)
			{
				cmdWaitAck = 0;
				timer1.Enabled = false;
			}
			break;
		case 129:
			if (cmdWaitAck == 129 && dataStream.receiveCmdDataSize == 0)
			{
				cmdWaitAck = 0;
				timer1.Enabled = false;
			}
			break;
		case 137:
			if (cmdWaitAck == 137 && dataStream.receiveCmdDataSize == 0)
			{
				cmdWaitAck = 0;
				timer1.Enabled = false;
			}
			break;
		case 140:
			if (cmdWaitAck == 140 && dataStream.receiveCmdDataSize == 1)
			{
				cmdWaitAck = 0;
				timer1.Enabled = false;
			}
			break;
		case 160:
			if (cmdWaitAck == 160 && dataStream.receiveCmdDataSize == 7)
			{
				cmdWaitAck = 0;
				timer1.Enabled = false;
				formMotorState2Refresh();
			}
			break;
		case 161:
			if (cmdWaitAck == 161 && dataStream.receiveCmdDataSize == 7)
			{
				cmdWaitAck = 0;
				timer1.Enabled = false;
				formMotorState2Refresh();
			}
			break;
		case 162:
			if (cmdWaitAck == 162 && dataStream.receiveCmdDataSize == 7)
			{
				cmdWaitAck = 0;
				timer1.Enabled = false;
				formMotorState2Refresh();
			}
			break;
		case 163:
			if (cmdWaitAck == 163 && dataStream.receiveCmdDataSize == 7)
			{
				cmdWaitAck = 0;
				timer1.Enabled = false;
				formMotorState2Refresh();
			}
			break;
		case 164:
			if (cmdWaitAck == 164 && dataStream.receiveCmdDataSize == 7)
			{
				cmdWaitAck = 0;
				timer1.Enabled = false;
				formMotorState2Refresh();
			}
			break;
		case 165:
			if (cmdWaitAck == 165 && dataStream.receiveCmdDataSize == 7)
			{
				cmdWaitAck = 0;
				timer1.Enabled = false;
				formMotorState2Refresh();
			}
			break;
		case 166:
			if (cmdWaitAck == 166 && dataStream.receiveCmdDataSize == 7)
			{
				cmdWaitAck = 0;
				timer1.Enabled = false;
				formMotorState2Refresh();
			}
			break;
		case 167:
			if (cmdWaitAck == 167 && dataStream.receiveCmdDataSize == 7)
			{
				cmdWaitAck = 0;
				timer1.Enabled = false;
				formMotorState2Refresh();
			}
			break;
		case 168:
			if (cmdWaitAck == 168 && dataStream.receiveCmdDataSize == 7)
			{
				cmdWaitAck = 0;
				timer1.Enabled = false;
				formMotorState2Refresh();
			}
			break;
		}
	}

	private void timer1_Elapsed(object sender, ElapsedEventArgs e)
	{
		string error_string = "";
		switch (cmdWaitAck)
		{
		case 31:
			if (deviceConnectStep == 0)
			{
				Invoke((EventHandler)delegate
				{
					buttonConnect.Enabled = true;
				});
			}
			else if (deviceConnectStep == 1)
			{
				deviceConnectStep = 10;
			}
			error_string = "Read device type error, No response!\n";
			break;
		case 16:
			if (deviceConnectStep == 0)
			{
				Invoke((EventHandler)delegate
				{
					buttonConnect.Enabled = true;
				});
			}
			else if (deviceConnectStep == 5)
			{
				deviceConnectStep = 16;
			}
			error_string = "Connect failed, No response!\n";
			break;
		case 17:
			deviceConnected = false;
			if (General.userSerialPort.IsOpen)
			{
				comPortClosing = true;
				while (Listening)
				{
					Application.DoEvents();
				}
				General.userSerialPort.Close();
				comPortClosing = false;
			}
			Invoke((EventHandler)delegate
			{
				buttonConnect.Enabled = true;
				buttonConnect.Text = "CONNECT";
				buttonConnect.BackColor = SystemColors.ActiveCaption;
			});
			error_string = "Disconnect failed, No response!\n";
			break;
		case 18:
			if (deviceConnectStep == 2)
			{
				deviceConnectStep = 13;
			}
			error_string = "Read product information failed!\n";
			break;
		case 22:
			if (deviceConnectStep == 3)
			{
				deviceConnectStep = 14;
			}
			error_string = "Read calibration failed!\n";
			break;
		case 23:
			error_string = "Write calibration failed!\n";
			break;
		case 24:
			error_string = "Calibrate failed!\n";
			break;
		case 25:
			error_string = "Set offset failed!\n";
			break;
		case 32:
			Invoke((EventHandler)delegate
			{
				buttonReducerEncoderAlign.Enabled = true;
				MessageBox.Show("Reducer encoder align failed!", "Error");
			});
			break;
		case 20:
			if (deviceConnectStep == 4)
			{
				deviceConnectStep = 15;
			}
			error_string = "Read setting failed!\n";
			break;
		case 21:
			error_string = "Write setting failed!\n";
			break;
		case 72:
			error_string = "Reset setting failed!\n";
			break;
		case 146:
			error_string = "Read motor multi angle error!\n";
			break;
		case 147:
			error_string = "Clear motor loops error!\n";
			break;
		case 148:
			error_string = "Read motor single angle error!\n";
			break;
		case 149:
			error_string = "Set motor zero to ram error!\n";
			break;
		case 154:
			error_string = "Read state1 error!\n";
			break;
		case 155:
			error_string = "Clear state1 error!\n";
			break;
		case 156:
			error_string = "Read state2 error!\n";
			break;
		case 157:
			error_string = "Read state3 error!\n";
			break;
		case 66:
			error_string = "Write parameter to ram error!\n";
			break;
		case 128:
			error_string = "Motor off error!\n";
			break;
		case 136:
			error_string = "Motor on error!\n";
			break;
		case 129:
			error_string = "Motor stop error!\n";
			break;
		case 137:
			error_string = "Motor restore error!\n";
			break;
		case 140:
			error_string = "Motor brake control error!\n";
			break;
		case 160:
			error_string = "Open control error!\n";
			break;
		case 161:
			error_string = "Torque control error!\n";
			break;
		case 162:
			error_string = "Speed control error!\n";
			break;
		case 163:
			error_string = "Multi loop angle control 1 error!\n";
			break;
		case 164:
			error_string = "Multi loop angle control 2 error!\n";
			break;
		case 165:
			error_string = "Single loop angle control 1 error!\n";
			break;
		case 166:
			error_string = "Single loop angle control 2 error!\n";
			break;
		case 167:
			error_string = "Increment angle control 1 error!\n";
			break;
		case 168:
			error_string = "Increment angle control 2 error!\n";
			break;
		default:
			error_string = "";
			break;
		}
		cmdWaitAck = 0;
		commandErrorCount++;
		Invoke((EventHandler)delegate
		{
			richTextBoxLog.Text += error_string;
			richTextBoxLog.Select(richTextBoxLog.TextLength, 0);
			richTextBoxLog.ScrollToCaret();
			labelErrorCount.Text = commandErrorCount.ToString();
		});
	}

	private void timer2_Elapsed(object sender, ElapsedEventArgs e)
	{
		if (deviceConnectStep == 1)
		{
			cmdWaitAck = 18;
			deviceConnectStep = 2;
			timer2.Interval = 130.0;
			timer2.Enabled = true;
			dataStream.cmdPack(18, rs485Id, 0, null);
			comPortSend(80);
		}
		else if (deviceConnectStep == 2)
		{
			cmdWaitAck = 22;
			deviceConnectStep = 3;
			timer2.Interval = 130.0;
			timer2.Enabled = true;
			dataStream.cmdPack(22, rs485Id, 0, null);
			comPortSend(80);
		}
		else if (deviceConnectStep == 3)
		{
			cmdWaitAck = 20;
			deviceConnectStep = 4;
			timer2.Interval = 300.0;
			timer2.Enabled = true;
			dataStream.cmdPack(20, rs485Id, 0, null);
			comPortSend(250);
		}
		else if (deviceConnectStep == 4)
		{
			cmdWaitAck = 16;
			deviceConnectStep = 5;
			timer2.Interval = 130.0;
			timer2.Enabled = true;
			dataStream.cmdPack(16, rs485Id, 0, null);
			comPortSend(80);
		}
		else if (deviceConnectStep == 5)
		{
			deviceConnected = true;
			deviceConnectStep = 0;
			Invoke((EventHandler)delegate
			{
				buttonConnect.Enabled = true;
				buttonConnect.BackColor = SystemColors.ActiveCaption;
				buttonConnect.Text = "DISCONNECT";
			});
			formSettingRefresh();
			formCalibRefresh();
			formInfoRefresh();
		}
		else
		{
			MessageBox.Show(dataStream.cmdConnectErrorText[deviceConnectStep - 10], "Error");
			Invoke((EventHandler)delegate
			{
				buttonConnect.Enabled = true;
			});
		}
	}

	private void formUi_UpdateFromDeviceType()
	{
		BeginInvoke((EventHandler)delegate
		{
			if (deviceType == 8209 && deviceType != deviceTypePrevious)
			{
				deviceTypePrevious = deviceType;
				label33.Enabled = false;
				numericUpDownCurrentKp.Enabled = false;
				numericUpDownCurrentKi.Enabled = false;
				labelMaxTorqueCurrent.Text = "Max Power";
				numericUpDownMaxTorque.Value = 850m;
				numericUpDownMaxTorque.Maximum = 850m;
				comboBoxSelectControlMode.Items.RemoveAt(0);
				comboBoxSelectControlMode.Items.Insert(0, "Open Control");
				comboBoxSelectControlMode.SelectedIndex = 0;
				label27.Text = "Power";
				label30.Text = "Power";
				numericUpDownControlTorque.Maximum = 850m;
				numericUpDownControlTorque.Minimum = -850m;
				numericUpDownControlTorque.Value = 200m;
				label54.Enabled = false;
				numericUpDownReductionRatio.Enabled = false;
				label55.Enabled = false;
				textBoxReducerAlignValue.Enabled = false;
				buttonReducerEncoderAlign.Enabled = false;
				label56.Enabled = false;
				textBoxReducerZeroPosition.Enabled = false;
				buttonSetReducerEncoderZeroPosition.Enabled = false;
				label5.Enabled = true;
				textBoxMotorZeroPosition.Enabled = true;
				buttonSetMotorEncoderZeroPosition.Enabled = true;
			}
			else if (deviceType == 8225 && deviceType != deviceTypePrevious)
			{
				deviceTypePrevious = deviceType;
				label33.Enabled = true;
				numericUpDownCurrentKp.Enabled = true;
				numericUpDownCurrentKi.Enabled = true;
				labelMaxTorqueCurrent.Text = "Max Torque Current";
				numericUpDownMaxTorque.Value = 1000m;
				numericUpDownMaxTorque.Maximum = 2000m;
				comboBoxSelectControlMode.Items.RemoveAt(0);
				comboBoxSelectControlMode.Items.Insert(0, "Torque Control");
				comboBoxSelectControlMode.SelectedIndex = 0;
				label27.Text = "Torque Current";
				label30.Text = "Torque Current";
				numericUpDownControlTorque.Maximum = 2000m;
				numericUpDownControlTorque.Minimum = -2000m;
				numericUpDownControlTorque.Value = 100m;
				label54.Enabled = false;
				numericUpDownReductionRatio.Enabled = false;
				label55.Enabled = false;
				textBoxReducerAlignValue.Enabled = false;
				buttonReducerEncoderAlign.Enabled = false;
				label56.Enabled = false;
				textBoxReducerZeroPosition.Enabled = false;
				buttonSetReducerEncoderZeroPosition.Enabled = false;
				label5.Enabled = true;
				textBoxMotorZeroPosition.Enabled = true;
				buttonSetMotorEncoderZeroPosition.Enabled = true;
			}
			else if ((deviceType == 8241 || deviceType == 8242) && deviceType != deviceTypePrevious)
			{
				label33.Enabled = true;
				numericUpDownCurrentKp.Enabled = true;
				numericUpDownCurrentKi.Enabled = true;
				labelMaxTorqueCurrent.Text = "Max Torque Current";
				numericUpDownMaxTorque.Value = 1000m;
				numericUpDownMaxTorque.Maximum = 2000m;
				comboBoxSelectControlMode.Items.RemoveAt(0);
				comboBoxSelectControlMode.Items.Insert(0, "Torque Control");
				comboBoxSelectControlMode.SelectedIndex = 0;
				label27.Text = "Torque Current";
				label30.Text = "Torque Current";
				numericUpDownControlTorque.Maximum = 2000m;
				numericUpDownControlTorque.Minimum = -2000m;
				numericUpDownControlTorque.Value = 100m;
				label54.Enabled = true;
				numericUpDownReductionRatio.Enabled = true;
				if (deviceType == 8241)
				{
					deviceTypePrevious = deviceType;
					label55.Enabled = false;
					textBoxReducerAlignValue.Enabled = false;
					buttonReducerEncoderAlign.Enabled = false;
					label56.Enabled = false;
					textBoxReducerZeroPosition.Enabled = false;
					buttonSetReducerEncoderZeroPosition.Enabled = false;
					label5.Enabled = true;
					textBoxMotorZeroPosition.Enabled = true;
					buttonSetMotorEncoderZeroPosition.Enabled = true;
				}
				else if (deviceType == 8242)
				{
					deviceTypePrevious = deviceType;
					label55.Enabled = true;
					textBoxReducerAlignValue.Enabled = true;
					buttonReducerEncoderAlign.Enabled = true;
					label56.Enabled = true;
					textBoxReducerZeroPosition.Enabled = true;
					buttonSetReducerEncoderZeroPosition.Enabled = true;
					label5.Enabled = false;
					textBoxMotorZeroPosition.Enabled = false;
					buttonSetMotorEncoderZeroPosition.Enabled = false;
				}
			}
			else if (deviceType == 8257 && deviceType != deviceTypePrevious)
			{
				deviceTypePrevious = deviceType;
			}
		});
	}

	private void formSettingRefresh()
	{
		BeginInvoke((EventHandler)delegate
		{
			try
			{
				numericUpDownDriverID.Value = saveSetting.driverId;
				comboBoxBusType.SelectedIndex = saveSetting.busType;
				if (comboBoxBusType.SelectedIndex == 1)
				{
					comboBoxRs485Baudrate.Enabled = true;
					comboBoxCanBaudrate.Enabled = false;
				}
				else if (comboBoxBusType.SelectedIndex == 2)
				{
					comboBoxRs485Baudrate.Enabled = false;
					comboBoxCanBaudrate.Enabled = true;
				}
				else
				{
					comboBoxRs485Baudrate.Enabled = false;
					comboBoxCanBaudrate.Enabled = false;
				}
				comboBoxRs485Baudrate.SelectedIndex = saveSetting.rs485BaudRate;
				comboBoxCanBaudrate.SelectedIndex = saveSetting.canBaudRate;
				comboBoxBroadcastMode.SelectedIndex = saveSetting.broadcastMode;
				comboBoxSpinDirection.SelectedIndex = saveSetting.spinDirection;
				comboBoxProtectMotorTempEnable.Enabled = saveSetting.protectMotorTempEnable != 0;
				if (saveSetting.protectMotorTempEnable <= 1)
				{
					comboBoxProtectMotorTempEnable.SelectedIndex = 0;
				}
				else if (saveSetting.protectMotorTempEnable <= 3)
				{
					comboBoxProtectMotorTempEnable.SelectedIndex = saveSetting.protectMotorTempEnable - 1;
				}
				comboBoxProtectDriverTempEnable.Enabled = saveSetting.protectDriverTempEnable != 0;
				if (saveSetting.protectDriverTempEnable <= 1)
				{
					comboBoxProtectDriverTempEnable.SelectedIndex = 0;
				}
				else if (saveSetting.protectDriverTempEnable <= 3)
				{
					comboBoxProtectDriverTempEnable.SelectedIndex = saveSetting.protectDriverTempEnable - 1;
				}
				comboBoxProtectUnderVoltageEnable.Enabled = saveSetting.protectUnderVoltageEnable != 0;
				if (saveSetting.protectUnderVoltageEnable <= 1)
				{
					comboBoxProtectUnderVoltageEnable.SelectedIndex = 0;
				}
				else if (saveSetting.protectUnderVoltageEnable <= 3)
				{
					comboBoxProtectUnderVoltageEnable.SelectedIndex = saveSetting.protectUnderVoltageEnable - 1;
				}
				comboBoxProtectOverVoltageEnable.Enabled = saveSetting.protectOverVoltageEnable != 0;
				if (saveSetting.protectOverVoltageEnable <= 1)
				{
					comboBoxProtectOverVoltageEnable.SelectedIndex = 0;
				}
				else if (saveSetting.protectOverVoltageEnable <= 3)
				{
					comboBoxProtectOverVoltageEnable.SelectedIndex = saveSetting.protectOverVoltageEnable - 1;
				}
				comboBoxProtectOverCurrentEnable.Enabled = saveSetting.protectOverCurrentEnable != 0;
				if (saveSetting.protectOverCurrentEnable <= 1)
				{
					comboBoxProtectOverCurrentEnable.SelectedIndex = 0;
				}
				else if (saveSetting.protectOverCurrentEnable <= 3)
				{
					comboBoxProtectOverCurrentEnable.SelectedIndex = saveSetting.protectOverCurrentEnable - 1;
				}
				comboBoxProtectShortCircuitEnable.Enabled = saveSetting.protectShortCircuitEnable != 0;
				if (saveSetting.protectShortCircuitEnable <= 1)
				{
					comboBoxProtectShortCircuitEnable.SelectedIndex = 0;
				}
				else if (saveSetting.protectShortCircuitEnable <= 3)
				{
					comboBoxProtectShortCircuitEnable.SelectedIndex = saveSetting.protectShortCircuitEnable - 1;
				}
				comboBoxProtectStallEnable.Enabled = saveSetting.protectStallEnable != 0;
				if (saveSetting.protectStallEnable <= 1)
				{
					comboBoxProtectStallEnable.SelectedIndex = 0;
				}
				else if (saveSetting.protectStallEnable <= 3)
				{
					comboBoxProtectStallEnable.SelectedIndex = saveSetting.protectStallEnable - 1;
				}
				comboBoxProtectLostInputEnable.Enabled = saveSetting.protectLostInputEnable != 0;
				if (saveSetting.protectLostInputEnable <= 1)
				{
					comboBoxProtectLostInputEnable.SelectedIndex = 0;
				}
				else if (saveSetting.protectLostInputEnable <= 3)
				{
					comboBoxProtectLostInputEnable.SelectedIndex = saveSetting.protectLostInputEnable - 1;
				}
				numericUpDownProtectMotorTemp.Value = saveSetting.protectMotorTemp;
				numericUpDownProtectMotorTemp.Enabled = comboBoxProtectMotorTempEnable.Enabled;
				numericUpDownProtectDriverTemp.Value = saveSetting.protectDriverTemp;
				numericUpDownProtectDriverTemp.Enabled = comboBoxProtectDriverTempEnable.Enabled;
				numericUpDownProtectUnderVoltage.Value = (decimal)saveSetting.protectUnderVoltage / 100m;
				numericUpDownProtectUnderVoltage.Enabled = comboBoxProtectUnderVoltageEnable.Enabled;
				numericUpDownProtectOverVoltage.Value = (decimal)saveSetting.protectOverVoltage / 100m;
				numericUpDownProtectOverVoltage.Enabled = comboBoxProtectOverVoltageEnable.Enabled;
				numericUpDownProtectOverCurrent.Value = (decimal)saveSetting.protectOverCurrent / 100m;
				numericUpDownProtectOverCurrent.Enabled = comboBoxProtectOverCurrentEnable.Enabled;
				numericUpDownProtectOverCurrentTime.Value = saveSetting.protectOverCurrentTime;
				numericUpDownProtectOverCurrentTime.Enabled = comboBoxProtectOverCurrentEnable.Enabled;
				numericUpDownProtectStallTime.Value = saveSetting.protectStallTime;
				numericUpDownProtectStallTime.Enabled = comboBoxProtectStallEnable.Enabled;
				numericUpDownProtectLostInputTime.Value = saveSetting.protectLostInputTime;
				numericUpDownProtectLostInputTime.Enabled = comboBoxProtectLostInputEnable.Enabled;
				comboBoxBrakeResEnable.Enabled = saveSetting.brakeResEnable != 0;
				if (saveSetting.brakeResEnable <= 1)
				{
					comboBoxBrakeResEnable.SelectedIndex = 0;
				}
				else if (saveSetting.brakeResEnable == 2)
				{
					comboBoxBrakeResEnable.SelectedIndex = 1;
				}
				numericUpDownBrakeResOnVoltage.Enabled = comboBoxBrakeResEnable.Enabled;
				numericUpDownBrakeResOnVoltage.Value = (decimal)saveSetting.brakeResOnVoltage / 100m;
				numericUpDownAngleKp.Value = saveSetting.anglePidKp;
				numericUpDownAngleKi.Value = saveSetting.anglePidKi;
				numericUpDownSpeedKp.Value = saveSetting.speedPidKp;
				numericUpDownSpeedKi.Value = saveSetting.speedPidKi;
				numericUpDownCurrentKp.Value = saveSetting.currentPidKp;
				numericUpDownCurrentKi.Value = saveSetting.currentPidKi;
				numericUpDownCurrentRamp.Value = saveSetting.currentRamp;
				numericUpDownMaxTorque.Value = saveSetting.maxTorque;
				numericUpDownSpeedRamp.Value = saveSetting.speedRamp;
				numericUpDownMaxSpeed.Value = (decimal)saveSetting.maxSpeed / 100m;
				numericUpDownMaxAngle.Value = (decimal)saveSetting.maxAngle / 100m;
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.ToString());
			}
		});
	}

	private void formCalibRefresh()
	{
		BeginInvoke((EventHandler)delegate
		{
			try
			{
				if (deviceType == 8241 || deviceType == 8242)
				{
					numericUpDownMotorPoles.Value = saveCalibMg.motorPoles;
					if (saveCalibMg.encoderType <= 9)
					{
						textBoxEncoderType.Text = dataStream.encoderType[saveCalibMg.encoderType];
					}
					else
					{
						textBoxEncoderType.Text = "Unknown";
					}
					textBoxEncoderPosition.Text = dataStream.encoderPosition[saveCalibMg.encoderPos];
					textBoxMotorEncoderAlignRatio.Text = saveCalibMg.motorEncoder_alignRatio.ToString();
					textBoxMotorEncoderOffset.Text = saveCalibMg.motorEncoder_alignBias.ToString();
					textBoxMotorPhaseSequence.Text = dataStream.motorPhaseSequence[saveCalibMg.motorPhaseSequence];
					numericUpDownAlignVoltage.Value = (decimal)((float)(int)saveCalibMg.motorEncoder_alignVoltage / 100f);
					numericUpDownReductionRatio.Value = saveCalibMg.reductionRatio;
					textBoxReducerAlignValue.Text = saveCalibMg.encoderRelateValue.ToString();
					textBoxReducerZeroPosition.Text = saveCalibMg.encoder2_offset.ToString();
					textBoxMotorZeroPosition.Text = saveCalibMg.encoder_offset.ToString();
				}
				else if (deviceType == 8209 || deviceType == 8225 || deviceType == 8257)
				{
					numericUpDownMotorPoles.Value = saveCalibMsMfMh.motorPoles;
					if (saveCalibMsMfMh.encoderType <= 9)
					{
						textBoxEncoderType.Text = dataStream.encoderType[saveCalibMsMfMh.encoderType];
					}
					else
					{
						textBoxEncoderType.Text = "Unknown";
					}
					textBoxEncoderPosition.Text = dataStream.encoderPosition[saveCalibMsMfMh.encoderPos];
					textBoxMotorEncoderAlignRatio.Text = saveCalibMsMfMh.motorEncoder_alignRatio.ToString();
					textBoxMotorEncoderOffset.Text = saveCalibMsMfMh.motorEncoder_alignBias.ToString();
					textBoxMotorPhaseSequence.Text = dataStream.motorPhaseSequence[saveCalibMsMfMh.motorPhaseSequence];
					numericUpDownAlignVoltage.Value = (decimal)((float)(int)saveCalibMsMfMh.motorEncoder_alignVoltage / 100f);
					textBoxMotorZeroPosition.Text = saveCalibMsMfMh.encoder_offset.ToString();
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.ToString());
			}
		});
	}

	private void formCalibAlignRatioRefresh()
	{
		byte[] array = new byte[2];
		ushort tempValue = 0;
		array[0] = dataStream.receiveCmdData[4];
		array[1] = dataStream.receiveCmdData[5];
		try
		{
			tempValue = dataStream.bytesToU16(array);
			Invoke((EventHandler)delegate
			{
				if (dataStream.receiveCmdData[3] == 0)
				{
					textBoxMotorPhaseSequence.Text = dataStream.motorPhaseSequence[0];
				}
				else if (dataStream.receiveCmdData[3] == 1)
				{
					textBoxMotorPhaseSequence.Text = dataStream.motorPhaseSequence[1];
				}
				textBoxMotorEncoderAlignRatio.Text = tempValue.ToString();
			});
		}
		catch (Exception ex)
		{
			MessageBox.Show(ex.ToString());
		}
	}

	private void formCalibEncoderOffsetRefresh()
	{
		byte[] array = new byte[4];
		for (int i = 0; i < 4; i++)
		{
			array[i] = dataStream.receiveCmdData[i + 3];
		}
		try
		{
			if (dataStream.receiveCmdData[2] == 0)
			{
				saveCalibMg.encoder_offset = dataStream.bytesToU32(array);
				Invoke((EventHandler)delegate
				{
					textBoxMotorZeroPosition.Text = saveCalibMg.encoder_offset.ToString();
				});
			}
			else if (dataStream.receiveCmdData[2] == 1)
			{
				saveCalibMg.encoder2_offset = dataStream.bytesToU32(array);
				Invoke((EventHandler)delegate
				{
					textBoxReducerZeroPosition.Text = saveCalibMg.encoder2_offset.ToString();
				});
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show(ex.ToString());
		}
	}

	private void formInfoRefresh()
	{
		int i = 0;
		BeginInvoke((EventHandler)delegate
		{
			try
			{
				labelDriverName.Text = "Driver :  " + Encoding.ASCII.GetString(dataStream.driverName);
				labelMotorName.Text = "Motor :  " + Encoding.ASCII.GetString(dataStream.motorName);
				labelHardwareVersion.Text = "Hardware version :  V" + ((decimal)dataStream.hardwareVersion / 100m).ToString("F1");
				labelMotorVersion.Text = "Motor version :  V" + ((decimal)dataStream.motorVersion / 100m).ToString("F1");
				labelFirmwareVersoin.Text = "Firmware version :  V" + ((decimal)dataStream.firmwareVersion / 100m).ToString("F2");
				labelChipId.Text = "Chip ID: ";
				for (i = 0; i < 12; i++)
				{
					labelChipId.Text += dataStream.uniqueId[i].ToString("X2");
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.ToString());
			}
		});
	}

	private void formMotorAngleRefresh()
	{
		byte[] array = new byte[8];
		for (int i = 0; i < 8; i++)
		{
			array[i] = dataStream.receiveCmdData[i];
		}
		try
		{
			angleValue = dataStream.bytesToU64(array);
			Invoke((EventHandler)delegate
			{
				textBoxMultiAngle.Text = (decimal)angleValue / 100m + "°";
			});
		}
		catch (Exception ex)
		{
			MessageBox.Show(ex.ToString());
		}
	}

	private void formMotorSingleAngleRefresh()
	{
		byte[] array = new byte[4];
		for (int i = 0; i < 4; i++)
		{
			array[i] = dataStream.receiveCmdData[i];
		}
		try
		{
			singleAngleValue = dataStream.bytesToU32(array);
			Invoke((EventHandler)delegate
			{
				textBoxSingleAngle.Text = (decimal)singleAngleValue / 100m + "°";
			});
		}
		catch (Exception ex)
		{
			MessageBox.Show(ex.ToString());
		}
	}

	private void formMotorState1Refresh()
	{
		try
		{
			motorTemperatureValue = (sbyte)dataStream.receiveCmdData[0];
			busVoltageValue = (short)(dataStream.receiveCmdData[1] + (dataStream.receiveCmdData[2] << 8));
			busCurrentValue = (short)(dataStream.receiveCmdData[3] + (dataStream.receiveCmdData[4] << 8));
			MotorErrorFlagValue = dataStream.receiveCmdData[6];
			Invoke((EventHandler)delegate
			{
				labelMotorTemperature.Text = motorTemperatureValue + "℃";
				labelBusVoltage.Text = ((float)busVoltageValue / 100f).ToString("F2") + " V";
				labelBusCurrent.Text = ((float)busCurrentValue / 100f).ToString("F2") + " A";
				checkBoxUnderVoltageProtection.Checked = (MotorErrorFlagValue & 1) != 0;
				checkBoxOverVoltageProtection.Checked = (MotorErrorFlagValue & 2) != 0;
				checkBoxDriverTemperatureProtection.Checked = (MotorErrorFlagValue & 4) != 0;
				checkBoxMotorTemperatureProtection.Checked = (MotorErrorFlagValue & 8) != 0;
				checkBoxOverCurrentProtection.Checked = (MotorErrorFlagValue & 0x10) != 0;
				checkBoxShortCircuitProtection.Checked = (MotorErrorFlagValue & 0x20) != 0;
				checkBoxMotorStallProtection.Checked = (MotorErrorFlagValue & 0x40) != 0;
				checkBoxLostInputProtection.Checked = (MotorErrorFlagValue & 0x80) != 0;
			});
		}
		catch (Exception ex)
		{
			MessageBox.Show(ex.ToString());
		}
	}

	private void formMotorState2Refresh()
	{
		try
		{
			motorTemperatureValue = (sbyte)dataStream.receiveCmdData[0];
			torqueCurrentValue = (short)(dataStream.receiveCmdData[1] + (dataStream.receiveCmdData[2] << 8));
			speedValue = (short)(dataStream.receiveCmdData[3] + (dataStream.receiveCmdData[4] << 8));
			encoderValue = (ushort)(dataStream.receiveCmdData[5] + (dataStream.receiveCmdData[6] << 8));
			Invoke((EventHandler)delegate
			{
				labelMotorTemperature.Text = motorTemperatureValue + "℃";
				labelIq.Text = torqueCurrentValue.ToString();
				labelSpeed.Text = speedValue + " dps";
				labelEncoder.Text = encoderValue.ToString();
			});
		}
		catch (Exception ex)
		{
			MessageBox.Show(ex.ToString());
		}
	}

	private void formMotorState3Refresh()
	{
		try
		{
			motorTemperatureValue = (sbyte)dataStream.receiveCmdData[0];
			iaValue = (short)(dataStream.receiveCmdData[1] + (dataStream.receiveCmdData[2] << 8));
			ibValue = (short)(dataStream.receiveCmdData[3] + (dataStream.receiveCmdData[4] << 8));
			icValue = (short)(dataStream.receiveCmdData[5] + (dataStream.receiveCmdData[6] << 8));
			Invoke((EventHandler)delegate
			{
				labelMotorTemperature.Text = motorTemperatureValue + "℃";
				labelIa.Text = iaValue.ToString();
				labelIb.Text = ibValue.ToString();
				labelIc.Text = icValue.ToString();
			});
		}
		catch (Exception ex)
		{
			MessageBox.Show(ex.ToString());
		}
	}

	private void saveSetting_Update()
	{
		try
		{
			saveSetting.driverId = (byte)numericUpDownDriverID.Value;
			saveSetting.busType = (byte)comboBoxBusType.SelectedIndex;
			saveSetting.rs485BaudRate = (byte)comboBoxRs485Baudrate.SelectedIndex;
			saveSetting.canBaudRate = (byte)comboBoxCanBaudrate.SelectedIndex;
			saveSetting.broadcastMode = (byte)comboBoxBroadcastMode.SelectedIndex;
			saveSetting.spinDirection = (byte)comboBoxSpinDirection.SelectedIndex;
			if (!comboBoxProtectMotorTempEnable.Enabled)
			{
				saveSetting.protectMotorTempEnable = 0;
			}
			else
			{
				saveSetting.protectMotorTempEnable = (byte)(comboBoxProtectMotorTempEnable.SelectedIndex + 1);
			}
			if (!comboBoxProtectDriverTempEnable.Enabled)
			{
				saveSetting.protectDriverTempEnable = 0;
			}
			else
			{
				saveSetting.protectDriverTempEnable = (byte)(comboBoxProtectDriverTempEnable.SelectedIndex + 1);
			}
			if (!comboBoxProtectUnderVoltageEnable.Enabled)
			{
				saveSetting.protectUnderVoltageEnable = 0;
			}
			else
			{
				saveSetting.protectUnderVoltageEnable = (byte)(comboBoxProtectUnderVoltageEnable.SelectedIndex + 1);
			}
			if (!comboBoxProtectOverVoltageEnable.Enabled)
			{
				saveSetting.protectOverVoltageEnable = 0;
			}
			else
			{
				saveSetting.protectOverVoltageEnable = (byte)(comboBoxProtectOverVoltageEnable.SelectedIndex + 1);
			}
			if (!comboBoxProtectOverCurrentEnable.Enabled)
			{
				saveSetting.protectOverCurrentEnable = 0;
			}
			else
			{
				saveSetting.protectOverCurrentEnable = (byte)(comboBoxProtectOverCurrentEnable.SelectedIndex + 1);
			}
			if (!comboBoxProtectShortCircuitEnable.Enabled)
			{
				saveSetting.protectShortCircuitEnable = 0;
			}
			else
			{
				saveSetting.protectShortCircuitEnable = (byte)(comboBoxProtectShortCircuitEnable.SelectedIndex + 1);
			}
			if (!comboBoxProtectStallEnable.Enabled)
			{
				saveSetting.protectStallEnable = 0;
			}
			else
			{
				saveSetting.protectStallEnable = (byte)(comboBoxProtectStallEnable.SelectedIndex + 1);
			}
			if (!comboBoxProtectLostInputEnable.Enabled)
			{
				saveSetting.protectLostInputEnable = 0;
			}
			else
			{
				saveSetting.protectLostInputEnable = (byte)(comboBoxProtectLostInputEnable.SelectedIndex + 1);
			}
			saveSetting.protectMotorTemp = (byte)numericUpDownProtectMotorTemp.Value;
			saveSetting.protectDriverTemp = (byte)numericUpDownProtectDriverTemp.Value;
			saveSetting.protectUnderVoltage = (ushort)(numericUpDownProtectUnderVoltage.Value * 100m);
			saveSetting.protectOverVoltage = (ushort)(numericUpDownProtectOverVoltage.Value * 100m);
			saveSetting.protectOverCurrent = (ushort)(numericUpDownProtectOverCurrent.Value * 100m);
			saveSetting.protectOverCurrentTime = (ushort)numericUpDownProtectOverCurrentTime.Value;
			saveSetting.protectStallTime = (ushort)numericUpDownProtectStallTime.Value;
			saveSetting.protectLostInputTime = (ushort)numericUpDownProtectLostInputTime.Value;
			if (!comboBoxBrakeResEnable.Enabled)
			{
				saveSetting.brakeResEnable = 0;
			}
			else
			{
				saveSetting.brakeResEnable = (byte)(comboBoxBrakeResEnable.SelectedIndex + 1);
			}
			saveSetting.brakeResOnVoltage = (ushort)(numericUpDownBrakeResOnVoltage.Value * 100m);
			saveSetting.anglePidKp = (ushort)numericUpDownAngleKp.Value;
			saveSetting.anglePidKi = (ushort)numericUpDownAngleKi.Value;
			saveSetting.speedPidKp = (ushort)numericUpDownSpeedKp.Value;
			saveSetting.speedPidKi = (ushort)numericUpDownSpeedKi.Value;
			saveSetting.currentPidKp = (ushort)numericUpDownCurrentKp.Value;
			saveSetting.currentPidKi = (ushort)numericUpDownCurrentKi.Value;
			saveSetting.maxTorque = (short)numericUpDownMaxTorque.Value;
			saveSetting.maxSpeed = (int)numericUpDownMaxSpeed.Value * 100;
			saveSetting.maxAngle = (long)numericUpDownMaxAngle.Value * 100;
			saveSetting.currentRamp = (short)numericUpDownCurrentRamp.Value;
			saveSetting.speedRamp = (int)numericUpDownSpeedRamp.Value;
		}
		catch (Exception ex)
		{
			MessageBox.Show(ex.ToString());
		}
	}

	private void saveCalib_Update()
	{
		try
		{
			if (deviceType == 8241 || deviceType == 8242)
			{
				saveCalibMg.motorPoles = (byte)numericUpDownMotorPoles.Value;
				saveCalibMg.motorEncoder_alignVoltage = (ushort)((float)numericUpDownAlignVoltage.Value * 100f);
			}
			else if (deviceType == 8209 || deviceType == 8225 || deviceType == 8257)
			{
				saveCalibMsMfMh.motorPoles = (byte)numericUpDownMotorPoles.Value;
				saveCalibMsMfMh.motorEncoder_alignVoltage = (ushort)((float)numericUpDownAlignVoltage.Value * 100f);
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show(ex.ToString());
		}
	}

	public bool comPortSend(int timeout)
	{
		string logString = "";
		bool flag = true;
		try
		{
			General.userSerialPort.Write(dataStream.sendBuffer.ToArray(), 0, dataStream.sendBuffer.Count);
			while (General.userSerialPort.BytesToWrite > 0)
			{
			}
		}
		catch
		{
			MessageBox.Show("COM port error!");
			flag = false;
		}
		finally
		{
			if (flag)
			{
				logString = "";
				for (byte b = 0; b < dataStream.sendBuffer.Count; b++)
				{
					logString = logString + dataStream.sendBuffer[b].ToString("X2") + " ";
				}
				Invoke((EventHandler)delegate
				{
					RichTextBox richTextBox = richTextBoxLog;
					richTextBox.Text = richTextBox.Text + "TX:  " + logString + "\n";
					richTextBoxLog.Select(richTextBoxLog.TextLength, 0);
					richTextBoxLog.ScrollToCaret();
				});
				if (timeout != 0)
				{
					timer1.Interval = timeout;
					timer1.AutoReset = false;
					timer1.Enabled = true;
				}
			}
		}
		return flag;
	}

	private void comboBoxSelectCom_Click(object sender, EventArgs e)
	{
		string[] portNames = SerialPort.GetPortNames();
		Array.Sort(portNames);
		comboBoxSelectCom.Items.Clear();
		ComboBox.ObjectCollection items = comboBoxSelectCom.Items;
		object[] items2 = portNames;
		items.AddRange(items2);
	}

	private void comboBoxSelectCom_SelectedIndexChanged(object sender, EventArgs e)
	{
		General.userSerialPort.Close();
		if (comboBoxSelectCom.SelectedIndex != -1)
		{
			General.userSerialPort.PortName = comboBoxSelectCom.Text;
		}
	}

	private void comboBoxSelectBaudRate_SelectedIndexChanged(object sender, EventArgs e)
	{
		if (comboBoxSelectBaudRate.SelectedIndex == 0)
		{
			General.userSerialPort.BaudRate = 9600;
		}
		else if (comboBoxSelectBaudRate.SelectedIndex == 1)
		{
			General.userSerialPort.BaudRate = 19200;
		}
		else if (comboBoxSelectBaudRate.SelectedIndex == 2)
		{
			General.userSerialPort.BaudRate = 38400;
		}
		else if (comboBoxSelectBaudRate.SelectedIndex == 3)
		{
			General.userSerialPort.BaudRate = 57600;
		}
		else if (comboBoxSelectBaudRate.SelectedIndex == 4)
		{
			General.userSerialPort.BaudRate = 115200;
		}
		else if (comboBoxSelectBaudRate.SelectedIndex == 5)
		{
			General.userSerialPort.BaudRate = 230400;
		}
		else if (comboBoxSelectBaudRate.SelectedIndex == 6)
		{
			General.userSerialPort.BaudRate = 460800;
		}
		else if (comboBoxSelectBaudRate.SelectedIndex == 7)
		{
			General.userSerialPort.BaudRate = 1000000;
		}
		else if (comboBoxSelectBaudRate.SelectedIndex == 8)
		{
			General.userSerialPort.BaudRate = 2000000;
		}
		else if (comboBoxSelectBaudRate.SelectedIndex == 9)
		{
			General.userSerialPort.BaudRate = 4000000;
		}
	}

	private void numericUpDownSelectId_ValueChanged(object sender, EventArgs e)
	{
		rs485Id = (byte)numericUpDownSelectId.Value;
	}

	private void buttonConnect_Click(object sender, EventArgs e)
	{
		if (!deviceConnected)
		{
			if (!General.userSerialPort.IsOpen)
			{
				try
				{
					General.userSerialPort.Open();
				}
				catch (Exception)
				{
					MessageBox.Show("串口不存在或被占用!");
					return;
				}
			}
			buttonConnect.Enabled = false;
			deviceConnectStep = 1;
			cmdWaitAck = 31;
			timer2.Interval = 50.0;
			timer2.Enabled = true;
			dataStream.cmdPack(31, rs485Id, 0, null);
			comPortSend(100);
		}
		else
		{
			buttonConnect.Enabled = false;
			deviceType = 0;
			cmdWaitAck = 17;
			dataStream.cmdPack(17, rs485Id, 0, null);
			comPortSend(100);
		}
	}

	private unsafe void buttonSetMaxTorqueCurrentRam_Click(object sender, EventArgs e)
	{
		byte[] array = new byte[7];
		if (deviceConnected && cmdWaitAck == 0)
		{
			short num = (short)numericUpDownMaxTorque.Value;
			array[0] = 153;
			array[1] = *(byte*)(&num);
			array[2] = ((byte*)(&num))[1];
			array[3] = 0;
			array[4] = 0;
			array[5] = 0;
			array[6] = 0;
			cmdWaitAck = 66;
			dataStream.cmdPack(66, rs485Id, 7, array);
			comPortSend(100);
		}
	}

	private unsafe void buttonSetMaxSpeedRam_Click(object sender, EventArgs e)
	{
		byte[] array = new byte[7];
		if (deviceConnected && cmdWaitAck == 0)
		{
			int num = (int)(numericUpDownMaxSpeed.Value * 100m);
			array[0] = 154;
			array[1] = *(byte*)(&num);
			array[2] = ((byte*)(&num))[1];
			array[3] = ((byte*)(&num))[2];
			array[4] = ((byte*)(&num))[3];
			array[5] = 0;
			array[6] = 0;
			cmdWaitAck = 66;
			dataStream.cmdPack(66, rs485Id, 7, array);
			comPortSend(100);
		}
	}

	private unsafe void buttonSetSpeedRampRam_Click(object sender, EventArgs e)
	{
		byte[] array = new byte[7];
		if (deviceConnected && cmdWaitAck == 0)
		{
			int num = (int)numericUpDownSpeedRamp.Value;
			array[0] = 158;
			array[1] = *(byte*)(&num);
			array[2] = ((byte*)(&num))[1];
			array[3] = ((byte*)(&num))[2];
			array[4] = ((byte*)(&num))[3];
			array[5] = 0;
			array[6] = 0;
			cmdWaitAck = 66;
			dataStream.cmdPack(66, rs485Id, 7, array);
			comPortSend(100);
		}
	}

	private unsafe void buttonSetAnglePidRam_Click(object sender, EventArgs e)
	{
		byte[] array = new byte[7];
		if (deviceConnected && cmdWaitAck == 0)
		{
			ushort num = (ushort)numericUpDownAngleKp.Value;
			ushort num2 = (ushort)numericUpDownAngleKi.Value;
			ushort num3 = 0;
			array[0] = 150;
			array[1] = *(byte*)(&num);
			array[2] = ((byte*)(&num))[1];
			array[3] = *(byte*)(&num2);
			array[4] = ((byte*)(&num2))[1];
			array[5] = *(byte*)(&num3);
			array[6] = ((byte*)(&num3))[1];
			cmdWaitAck = 66;
			dataStream.cmdPack(66, rs485Id, 7, array);
			comPortSend(100);
		}
	}

	private unsafe void buttonSetSpeedPidRam_Click(object sender, EventArgs e)
	{
		byte[] array = new byte[7];
		if (deviceConnected && cmdWaitAck == 0)
		{
			ushort num = (ushort)numericUpDownSpeedKp.Value;
			ushort num2 = (ushort)numericUpDownSpeedKi.Value;
			ushort num3 = 0;
			array[0] = 151;
			array[1] = *(byte*)(&num);
			array[2] = ((byte*)(&num))[1];
			array[3] = *(byte*)(&num2);
			array[4] = ((byte*)(&num2))[1];
			array[5] = *(byte*)(&num3);
			array[6] = ((byte*)(&num3))[1];
			cmdWaitAck = 66;
			dataStream.cmdPack(66, rs485Id, 7, array);
			comPortSend(100);
		}
	}

	private unsafe void buttonSetCurrentPidRam_Click(object sender, EventArgs e)
	{
		byte[] array = new byte[7];
		if (deviceConnected && cmdWaitAck == 0)
		{
			ushort num = (ushort)numericUpDownCurrentKp.Value;
			ushort num2 = (ushort)numericUpDownCurrentKi.Value;
			ushort num3 = 0;
			array[0] = 152;
			array[1] = *(byte*)(&num);
			array[2] = ((byte*)(&num))[1];
			array[3] = *(byte*)(&num2);
			array[4] = ((byte*)(&num2))[1];
			array[5] = *(byte*)(&num3);
			array[6] = ((byte*)(&num3))[1];
			cmdWaitAck = 66;
			dataStream.cmdPack(66, rs485Id, 7, array);
			comPortSend(100);
		}
	}

	private void buttonReadSaveSetting_Click(object sender, EventArgs e)
	{
		if (deviceConnected && cmdWaitAck == 0)
		{
			cmdWaitAck = 20;
			dataStream.cmdPack(20, rs485Id, 0, null);
			comPortSend(400);
		}
	}

	private void buttonWriteSaveSetting_Click(object sender, EventArgs e)
	{
		if (deviceConnected && cmdWaitAck == 0)
		{
			cmdWaitAck = 21;
			saveSetting_Update();
			dataStream.cmdPack(21, rs485Id, dataStream.saveSetting_GetSize(), dataStreamClass.StructToBytes(saveSetting));
			comPortSend(2000);
		}
	}

	private void buttonResetSaveSetting_Click(object sender, EventArgs e)
	{
		if (deviceConnected && cmdWaitAck == 0)
		{
			cmdWaitAck = 72;
			dataStream.cmdPack(72, rs485Id, 0, null);
			comPortSend(2000);
		}
	}

	private void buttonRebootDevice_Click(object sender, EventArgs e)
	{
		if (deviceConnected && cmdWaitAck == 0)
		{
			dataStream.cmdPack(7, rs485Id, 0, null);
			comPortSend(0);
		}
	}

	private void buttonReadProductInfo_Click(object sender, EventArgs e)
	{
		if (deviceConnected && cmdWaitAck == 0)
		{
			cmdWaitAck = 18;
			dataStream.cmdPack(18, rs485Id, 0, null);
			comPortSend(100);
		}
	}

	private void buttonReadSaveCalib_Click(object sender, EventArgs e)
	{
		if (deviceConnected && cmdWaitAck == 0)
		{
			cmdWaitAck = 22;
			dataStream.cmdPack(22, rs485Id, 0, null);
			comPortSend(100);
		}
	}

	private void buttonWriteSaveCalib_Click(object sender, EventArgs e)
	{
		if (deviceConnected && cmdWaitAck == 0)
		{
			cmdWaitAck = 23;
			saveCalib_Update();
			if (deviceType == 8241 || deviceType == 8242)
			{
				dataStream.cmdPack(23, rs485Id, dataStream.saveCalibMg_GetSize(), dataStreamClass.StructToBytes(saveCalibMg));
			}
			else if (deviceType == 8209 || deviceType == 8225 || deviceType == 8257)
			{
				dataStream.cmdPack(23, rs485Id, dataStream.saveCalibMsMfMh_GetSize(), dataStreamClass.StructToBytes(saveCalibMsMfMh));
			}
			comPortSend(2000);
		}
	}

	private void buttonMotorEncoderAlign_Click(object sender, EventArgs e)
	{
		if (deviceConnected && cmdWaitAck == 0)
		{
			cmdWaitAck = 24;
			dataStream.cmdPack(24, rs485Id, 0, null);
			comPortSend(0);
		}
	}

	private void buttonbuttonSetMotorEncoderZeroPosition_Click(object sender, EventArgs e)
	{
		if (deviceConnected && cmdWaitAck == 0)
		{
			cmdWaitAck = 25;
			dataStream.cmdPack(25, rs485Id, 0, null);
			comPortSend(2000);
		}
	}

	private void buttonReducerEncoderAlign_Click(object sender, EventArgs e)
	{
		if (deviceConnected)
		{
			buttonReducerEncoderAlign.Enabled = false;
			cmdWaitAck = 32;
			dataStream.cmdPack(32, rs485Id, 0, null);
			comPortSend(2000);
		}
	}

	private void buttonSetReducerEncoderZeroPosition_Click(object sender, EventArgs e)
	{
		if (deviceConnected && cmdWaitAck == 0)
		{
			cmdWaitAck = 25;
			dataStream.cmdPack(25, rs485Id, 0, null);
			comPortSend(2000);
		}
	}

	private void comboBoxSelectControlMode_SelectedIndexChanged(object sender, EventArgs e)
	{
		if (comboBoxSelectControlMode.SelectedIndex == 0)
		{
			numericUpDownControlTorque.Enabled = true;
			numericUpDownControlSpeed.Enabled = false;
			numericUpDownControlAngle.Enabled = false;
			checkBoxControlReverse.Enabled = false;
		}
		else if (comboBoxSelectControlMode.SelectedIndex == 1)
		{
			numericUpDownControlTorque.Enabled = false;
			numericUpDownControlSpeed.Enabled = true;
			numericUpDownControlAngle.Enabled = false;
			checkBoxControlReverse.Enabled = false;
		}
		else if (comboBoxSelectControlMode.SelectedIndex == 2)
		{
			numericUpDownControlTorque.Enabled = false;
			numericUpDownControlSpeed.Enabled = false;
			numericUpDownControlAngle.Enabled = true;
			checkBoxControlReverse.Enabled = false;
		}
		else if (comboBoxSelectControlMode.SelectedIndex == 3)
		{
			numericUpDownControlTorque.Enabled = false;
			numericUpDownControlSpeed.Enabled = true;
			numericUpDownControlAngle.Enabled = true;
			checkBoxControlReverse.Enabled = false;
		}
		else if (comboBoxSelectControlMode.SelectedIndex == 4)
		{
			numericUpDownControlTorque.Enabled = false;
			numericUpDownControlSpeed.Enabled = false;
			numericUpDownControlAngle.Enabled = true;
			checkBoxControlReverse.Enabled = true;
		}
		else if (comboBoxSelectControlMode.SelectedIndex == 5)
		{
			numericUpDownControlTorque.Enabled = false;
			numericUpDownControlSpeed.Enabled = true;
			numericUpDownControlAngle.Enabled = true;
			checkBoxControlReverse.Enabled = true;
		}
		else if (comboBoxSelectControlMode.SelectedIndex == 6)
		{
			numericUpDownControlTorque.Enabled = false;
			numericUpDownControlSpeed.Enabled = false;
			numericUpDownControlAngle.Enabled = true;
			checkBoxControlReverse.Enabled = false;
		}
		else if (comboBoxSelectControlMode.SelectedIndex == 7)
		{
			numericUpDownControlTorque.Enabled = false;
			numericUpDownControlSpeed.Enabled = true;
			numericUpDownControlAngle.Enabled = true;
			checkBoxControlReverse.Enabled = false;
		}
	}

	private void buttonMotorStop_Click(object sender, EventArgs e)
	{
		if (deviceConnected && cmdWaitAck == 0)
		{
			cmdWaitAck = 129;
			dataStream.cmdPack(129, rs485Id, 0, null);
			comPortSend(100);
		}
	}

	private void buttonMotorRestore_Click(object sender, EventArgs e)
	{
		if (deviceConnected && cmdWaitAck == 0)
		{
			cmdWaitAck = 137;
			dataStream.cmdPack(137, rs485Id, 0, null);
			comPortSend(100);
		}
	}

	private void ButtonMotorOff_Click(object sender, EventArgs e)
	{
		if (deviceConnected && cmdWaitAck == 0)
		{
			cmdWaitAck = 128;
			dataStream.cmdPack(128, rs485Id, 0, null);
			comPortSend(100);
		}
	}

	private void buttonMotorOn_Click(object sender, EventArgs e)
	{
		if (deviceConnected && cmdWaitAck == 0)
		{
			cmdWaitAck = 136;
			dataStream.cmdPack(136, rs485Id, 0, null);
			comPortSend(100);
		}
	}

	private void buttonSendControlCommand_Click(object sender, EventArgs e)
	{
		long num = 0L;
		uint num2 = 0u;
		int num3 = 0;
		byte[] array = new byte[2];
		byte[] array2 = new byte[4];
		byte[] array3 = new byte[8];
		byte[] array4 = new byte[20];
		byte dataLength = 0;
		if (!deviceConnected || cmdWaitAck != 0)
		{
			return;
		}
		if (comboBoxSelectControlMode.SelectedIndex == 0)
		{
			if (deviceType == 8209)
			{
				cmdWaitAck = 160;
			}
			else
			{
				cmdWaitAck = 161;
			}
			array = dataStreamClass.StructToBytes((short)numericUpDownControlTorque.Value);
			for (byte b = 0; b < 2; b++)
			{
				array4[b] = array[b];
			}
			dataLength = 2;
		}
		else if (comboBoxSelectControlMode.SelectedIndex == 1)
		{
			cmdWaitAck = 162;
			array2 = dataStreamClass.StructToBytes((int)(numericUpDownControlSpeed.Value * 100m));
			for (byte b2 = 0; b2 < 4; b2++)
			{
				array4[b2] = array2[b2];
			}
			dataLength = 4;
		}
		else if (comboBoxSelectControlMode.SelectedIndex == 2)
		{
			cmdWaitAck = 163;
			num = (long)(numericUpDownControlAngle.Value * 100m);
			array3 = dataStreamClass.StructToBytes(num);
			for (byte b3 = 0; b3 < 8; b3++)
			{
				array4[b3] = array3[b3];
			}
			dataLength = 8;
		}
		else if (comboBoxSelectControlMode.SelectedIndex == 3)
		{
			cmdWaitAck = 164;
			num = (long)(numericUpDownControlAngle.Value * 100m);
			uint num4 = (uint)(numericUpDownControlSpeed.Value * 100m);
			array3 = dataStreamClass.StructToBytes(num);
			array2 = dataStreamClass.StructToBytes(num4);
			for (byte b4 = 0; b4 < 8; b4++)
			{
				array4[b4] = array3[b4];
			}
			for (byte b5 = 8; b5 < 12; b5++)
			{
				array4[b5] = array2[b5 - 8];
			}
			dataLength = 12;
		}
		else if (comboBoxSelectControlMode.SelectedIndex == 4)
		{
			cmdWaitAck = 165;
			num2 = (uint)(numericUpDownControlAngle.Value * 100m);
			array3 = dataStreamClass.StructToBytes(num2);
			array4[0] = (checkBoxControlReverse.Checked ? ((byte)1) : ((byte)0));
			for (byte b6 = 1; b6 < 4; b6++)
			{
				array4[b6] = array3[b6 - 1];
			}
			dataLength = 4;
		}
		else if (comboBoxSelectControlMode.SelectedIndex == 5)
		{
			cmdWaitAck = 166;
			num2 = (uint)(numericUpDownControlAngle.Value * 100m);
			uint num5 = (uint)(numericUpDownControlSpeed.Value * 100m);
			array3 = dataStreamClass.StructToBytes(num2);
			array2 = dataStreamClass.StructToBytes(num5);
			array4[0] = (checkBoxControlReverse.Checked ? ((byte)1) : ((byte)0));
			for (byte b7 = 1; b7 < 4; b7++)
			{
				array4[b7] = array3[b7 - 1];
			}
			for (byte b8 = 4; b8 < 8; b8++)
			{
				array4[b8] = array2[b8 - 4];
			}
			dataLength = 8;
		}
		else if (comboBoxSelectControlMode.SelectedIndex == 6)
		{
			cmdWaitAck = 167;
			num3 = (int)(numericUpDownControlAngle.Value * 100m);
			array3 = dataStreamClass.StructToBytes(num3);
			for (byte b9 = 0; b9 < 4; b9++)
			{
				array4[b9] = array3[b9];
			}
			dataLength = 4;
		}
		else if (comboBoxSelectControlMode.SelectedIndex == 7)
		{
			cmdWaitAck = 168;
			num3 = (int)(numericUpDownControlAngle.Value * 100m);
			uint num6 = (uint)(numericUpDownControlSpeed.Value * 100m);
			array3 = dataStreamClass.StructToBytes(num3);
			array2 = dataStreamClass.StructToBytes(num6);
			for (byte b10 = 0; b10 < 4; b10++)
			{
				array4[b10] = array3[b10];
			}
			for (byte b11 = 4; b11 < 8; b11++)
			{
				array4[b11] = array2[b11 - 4];
			}
			dataLength = 8;
		}
		if (comboBoxSelectControlMode.SelectedIndex <= 7 && comboBoxSelectControlMode.SelectedIndex >= 0)
		{
			dataStream.cmdPack(cmdWaitAck, rs485Id, dataLength, array4);
			comPortSend(100);
		}
		else
		{
			MessageBox.Show("Please select control mode first !");
		}
	}

	private void buttonReadMultiAngle_Click(object sender, EventArgs e)
	{
		if (deviceConnected && cmdWaitAck == 0)
		{
			cmdWaitAck = 146;
			dataStream.cmdPack(146, rs485Id, 0, null);
			comPortSend(100);
		}
	}

	private void buttonClearMotorLoops_Click(object sender, EventArgs e)
	{
		if (deviceConnected && cmdWaitAck == 0)
		{
			cmdWaitAck = 147;
			dataStream.cmdPack(147, rs485Id, 0, null);
			comPortSend(100);
		}
	}

	private void buttonReadSingleAngle_Click(object sender, EventArgs e)
	{
		if (deviceConnected && cmdWaitAck == 0)
		{
			cmdWaitAck = 148;
			dataStream.cmdPack(148, rs485Id, 0, null);
			comPortSend(100);
		}
	}

	private void buttonSetMotorZeroRam_Click(object sender, EventArgs e)
	{
		if (deviceConnected && cmdWaitAck == 0)
		{
			cmdWaitAck = 149;
			dataStream.cmdPack(149, rs485Id, 0, null);
			comPortSend(100);
		}
	}

	private void buttonReadState1_Click(object sender, EventArgs e)
	{
		if (deviceConnected && cmdWaitAck == 0)
		{
			cmdWaitAck = 154;
			dataStream.cmdPack(154, rs485Id, 0, null);
			comPortSend(100);
		}
	}

	private void buttonClearError_Click(object sender, EventArgs e)
	{
		if (deviceConnected && cmdWaitAck == 0)
		{
			cmdWaitAck = 155;
			dataStream.cmdPack(155, rs485Id, 0, null);
			comPortSend(100);
		}
	}

	private void buttonReadState2_Click(object sender, EventArgs e)
	{
		if (deviceConnected && cmdWaitAck == 0)
		{
			cmdWaitAck = 156;
			dataStream.cmdPack(156, rs485Id, 0, null);
			comPortSend(100);
		}
	}

	private void buttonReadState3_Click(object sender, EventArgs e)
	{
		if (deviceConnected && cmdWaitAck == 0)
		{
			cmdWaitAck = 157;
			dataStream.cmdPack(157, rs485Id, 0, null);
			comPortSend(100);
		}
	}

	private void buttonBrake_Click(object sender, EventArgs e)
	{
		byte[] array = new byte[1];
		if (deviceConnected && cmdWaitAck == 0)
		{
			cmdWaitAck = 140;
			array[0] = 0;
			dataStream.cmdPack(cmdWaitAck, rs485Id, 1, array);
			comPortSend(100);
		}
	}

	private void buttonBrakeRelease_Click(object sender, EventArgs e)
	{
		byte[] array = new byte[1];
		if (deviceConnected && cmdWaitAck == 0)
		{
			cmdWaitAck = 140;
			array[0] = 1;
			dataStream.cmdPack(cmdWaitAck, rs485Id, 1, array);
			comPortSend(100);
		}
	}

	private void buttonClearLog_Click(object sender, EventArgs e)
	{
		richTextBoxLog.Clear();
	}

	private void buttonOpenFirmware_Click(object sender, EventArgs e)
	{
		_ = (Button)sender;
		OpenFileDialog openFileDialog = new OpenFileDialog();
		if (openFileDialog.ShowDialog() == DialogResult.OK)
		{
			firmwareName = openFileDialog.FileName;
			textBoxFilePath.Text = openFileDialog.SafeFileName;
		}
	}

	private void buttonWriteFirmware_Click(object sender, EventArgs e)
	{
		MessageBoxButtons buttons = MessageBoxButtons.OKCancel;
		if (MessageBox.Show("Clicking 'OK' will erase the firmware and start downloading", "Download ", buttons) == DialogResult.OK)
		{
			dataStream.cmdPack(187, rs485Id, 0, null);
			comPortSend(0);
			((Button)sender).Text = "Writing";
			General.userSerialPort.DataReceived -= comPort_DataReceived;
			panelWriteProgress.Width = 0;
			ymodem.Path = firmwareName;
			downloadThread = new Thread(ymodem.YmodemDownloadFile);
			ymodem.NowDownloadProgressEvent += NowDownloadProgressEvent;
			ymodem.DownloadResultEvent += DownloadFinishEvent;
			ymodem.SerialSettingResetEvent += SerialSettingRestoreEvent;
			downloadThread.Start();
		}
	}

	private void NowDownloadProgressEvent(object sender, EventArgs e)
	{
		int num = Convert.ToInt32(sender);
		NowDownloadProgress method = UploadFileProgress;
		Invoke(method, num);
	}

	private void UploadFileProgress(int count)
	{
		panelWriteProgress.Width = count * 6;
	}

	private void DownloadFinishEvent(object sender, EventArgs e)
	{
		bool flag = (bool)sender;
		DownloadFinish method = UploadFileResult;
		Invoke(method, flag);
	}

	private void UploadFileResult(bool result)
	{
		if (result)
		{
			MessageBox.Show("Write finished");
			buttonWriteFirmware.Text = "Write";
		}
		else
		{
			MessageBox.Show("Wirte failed");
			buttonWriteFirmware.Text = "Write";
		}
	}

	private void SerialSettingRestoreEvent(object sender, EventArgs e)
	{
		SerialSettingReset method = serialSettingResetCall;
		Invoke(method);
	}

	private void serialSettingResetCall()
	{
		General.userSerialPort.DataReceived += comPort_DataReceived;
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && components != null)
		{
			components.Dispose();
		}
		base.Dispose(disposing);
	}

	private void InitializeComponent()
	{
		this.components = new System.ComponentModel.Container();
		System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(serovMotor.FormMainSetting));
		this.comboBoxSelectCom = new System.Windows.Forms.ComboBox();
		this.label1 = new System.Windows.Forms.Label();
		this.buttonConnect = new System.Windows.Forms.Button();
		this.label6 = new System.Windows.Forms.Label();
		this.comboBoxSelectBaudRate = new System.Windows.Forms.ComboBox();
		this.numericUpDownSelectId = new System.Windows.Forms.NumericUpDown();
		this.label23 = new System.Windows.Forms.Label();
		this.tabPage1 = new System.Windows.Forms.TabPage();
		this.labelChipId = new System.Windows.Forms.Label();
		this.labelMotorVersion = new System.Windows.Forms.Label();
		this.textBoxFilePath = new System.Windows.Forms.TextBox();
		this.panelWriteProgressFull = new System.Windows.Forms.Panel();
		this.panelWriteProgress = new System.Windows.Forms.Panel();
		this.buttonOpenFirmware = new System.Windows.Forms.Button();
		this.buttonWriteFirmware = new System.Windows.Forms.Button();
		this.labelMotorName = new System.Windows.Forms.Label();
		this.buttonReadProductInfo = new System.Windows.Forms.Button();
		this.labelFirmwareVersoin = new System.Windows.Forms.Label();
		this.labelDriverName = new System.Windows.Forms.Label();
		this.labelHardwareVersion = new System.Windows.Forms.Label();
		this.tabPage2 = new System.Windows.Forms.TabPage();
		this.panel12 = new System.Windows.Forms.Panel();
		this.panel15 = new System.Windows.Forms.Panel();
		this.panel14 = new System.Windows.Forms.Panel();
		this.label58 = new System.Windows.Forms.Label();
		this.label56 = new System.Windows.Forms.Label();
		this.buttonReducerEncoderAlign = new System.Windows.Forms.Button();
		this.textBoxReducerZeroPosition = new System.Windows.Forms.TextBox();
		this.label55 = new System.Windows.Forms.Label();
		this.textBoxReducerAlignValue = new System.Windows.Forms.TextBox();
		this.buttonSetReducerEncoderZeroPosition = new System.Windows.Forms.Button();
		this.label54 = new System.Windows.Forms.Label();
		this.numericUpDownReductionRatio = new System.Windows.Forms.NumericUpDown();
		this.panel16 = new System.Windows.Forms.Panel();
		this.panel13 = new System.Windows.Forms.Panel();
		this.label57 = new System.Windows.Forms.Label();
		this.buttonSetMotorEncoderZeroPosition = new System.Windows.Forms.Button();
		this.label2 = new System.Windows.Forms.Label();
		this.textBoxMotorZeroPosition = new System.Windows.Forms.TextBox();
		this.textBoxMotorEncoderAlignRatio = new System.Windows.Forms.TextBox();
		this.numericUpDownAlignVoltage = new System.Windows.Forms.NumericUpDown();
		this.numericUpDownMotorPoles = new System.Windows.Forms.NumericUpDown();
		this.textBoxMotorEncoderOffset = new System.Windows.Forms.TextBox();
		this.label5 = new System.Windows.Forms.Label();
		this.label3 = new System.Windows.Forms.Label();
		this.textBoxMotorPhaseSequence = new System.Windows.Forms.TextBox();
		this.label4 = new System.Windows.Forms.Label();
		this.buttonMotorEncoderAlign = new System.Windows.Forms.Button();
		this.label28 = new System.Windows.Forms.Label();
		this.textBoxEncoderType = new System.Windows.Forms.TextBox();
		this.textBoxEncoderPosition = new System.Windows.Forms.TextBox();
		this.label7 = new System.Windows.Forms.Label();
		this.label9 = new System.Windows.Forms.Label();
		this.label8 = new System.Windows.Forms.Label();
		this.buttonReadSaveCalib = new System.Windows.Forms.Button();
		this.buttonWriteSaveCalib = new System.Windows.Forms.Button();
		this.tabPage3 = new System.Windows.Forms.TabPage();
		this.panel3 = new System.Windows.Forms.Panel();
		this.label53 = new System.Windows.Forms.Label();
		this.comboBoxProtectStallEnable = new System.Windows.Forms.ComboBox();
		this.numericUpDownProtectStallTime = new System.Windows.Forms.NumericUpDown();
		this.label52 = new System.Windows.Forms.Label();
		this.comboBoxProtectShortCircuitEnable = new System.Windows.Forms.ComboBox();
		this.label43 = new System.Windows.Forms.Label();
		this.numericUpDownProtectOverCurrentTime = new System.Windows.Forms.NumericUpDown();
		this.label42 = new System.Windows.Forms.Label();
		this.comboBoxProtectOverCurrentEnable = new System.Windows.Forms.ComboBox();
		this.numericUpDownProtectOverCurrent = new System.Windows.Forms.NumericUpDown();
		this.label21 = new System.Windows.Forms.Label();
		this.comboBoxProtectDriverTempEnable = new System.Windows.Forms.ComboBox();
		this.numericUpDownProtectDriverTemp = new System.Windows.Forms.NumericUpDown();
		this.label31 = new System.Windows.Forms.Label();
		this.comboBoxProtectLostInputEnable = new System.Windows.Forms.ComboBox();
		this.comboBoxProtectOverVoltageEnable = new System.Windows.Forms.ComboBox();
		this.comboBoxProtectUnderVoltageEnable = new System.Windows.Forms.ComboBox();
		this.comboBoxProtectMotorTempEnable = new System.Windows.Forms.ComboBox();
		this.numericUpDownProtectOverVoltage = new System.Windows.Forms.NumericUpDown();
		this.label51 = new System.Windows.Forms.Label();
		this.label39 = new System.Windows.Forms.Label();
		this.numericUpDownProtectMotorTemp = new System.Windows.Forms.NumericUpDown();
		this.label40 = new System.Windows.Forms.Label();
		this.numericUpDownProtectUnderVoltage = new System.Windows.Forms.NumericUpDown();
		this.numericUpDownProtectLostInputTime = new System.Windows.Forms.NumericUpDown();
		this.label14 = new System.Windows.Forms.Label();
		this.panel10 = new System.Windows.Forms.Panel();
		this.panel2 = new System.Windows.Forms.Panel();
		this.label44 = new System.Windows.Forms.Label();
		this.label20 = new System.Windows.Forms.Label();
		this.label15 = new System.Windows.Forms.Label();
		this.label50 = new System.Windows.Forms.Label();
		this.label13 = new System.Windows.Forms.Label();
		this.comboBoxSpinDirection = new System.Windows.Forms.ComboBox();
		this.comboBoxRs485Baudrate = new System.Windows.Forms.ComboBox();
		this.label49 = new System.Windows.Forms.Label();
		this.numericUpDownDriverID = new System.Windows.Forms.NumericUpDown();
		this.comboBoxBroadcastMode = new System.Windows.Forms.ComboBox();
		this.label45 = new System.Windows.Forms.Label();
		this.comboBoxBrakeResEnable = new System.Windows.Forms.ComboBox();
		this.comboBoxBusType = new System.Windows.Forms.ComboBox();
		this.comboBoxCanBaudrate = new System.Windows.Forms.ComboBox();
		this.label48 = new System.Windows.Forms.Label();
		this.label41 = new System.Windows.Forms.Label();
		this.numericUpDownBrakeResOnVoltage = new System.Windows.Forms.NumericUpDown();
		this.panel4 = new System.Windows.Forms.Panel();
		this.panel7 = new System.Windows.Forms.Panel();
		this.buttonResetSaveSetting = new System.Windows.Forms.Button();
		this.panel22 = new System.Windows.Forms.Panel();
		this.panel5 = new System.Windows.Forms.Panel();
		this.label10 = new System.Windows.Forms.Label();
		this.label33 = new System.Windows.Forms.Label();
		this.label34 = new System.Windows.Forms.Label();
		this.numericUpDownSpeedKp = new System.Windows.Forms.NumericUpDown();
		this.buttonSetCurrentPidRam = new System.Windows.Forms.Button();
		this.label16 = new System.Windows.Forms.Label();
		this.buttonSetSpeedPidRam = new System.Windows.Forms.Button();
		this.numericUpDownAngleKp = new System.Windows.Forms.NumericUpDown();
		this.buttonSetAnglePidRam = new System.Windows.Forms.Button();
		this.numericUpDownCurrentKp = new System.Windows.Forms.NumericUpDown();
		this.label37 = new System.Windows.Forms.Label();
		this.numericUpDownSpeedKi = new System.Windows.Forms.NumericUpDown();
		this.numericUpDownCurrentKi = new System.Windows.Forms.NumericUpDown();
		this.label17 = new System.Windows.Forms.Label();
		this.numericUpDownAngleKi = new System.Windows.Forms.NumericUpDown();
		this.panel11 = new System.Windows.Forms.Panel();
		this.panel9 = new System.Windows.Forms.Panel();
		this.buttonSetSpeedRampRam = new System.Windows.Forms.Button();
		this.buttonSetMaxSpeedRam = new System.Windows.Forms.Button();
		this.buttonSetMaxTorqueCurrentRam = new System.Windows.Forms.Button();
		this.numericUpDownCurrentRamp = new System.Windows.Forms.NumericUpDown();
		this.labelCurrentRamp = new System.Windows.Forms.Label();
		this.label11 = new System.Windows.Forms.Label();
		this.numericUpDownMaxTorque = new System.Windows.Forms.NumericUpDown();
		this.numericUpDownMaxSpeed = new System.Windows.Forms.NumericUpDown();
		this.labelMaxTorqueCurrent = new System.Windows.Forms.Label();
		this.labelMaxSpeed = new System.Windows.Forms.Label();
		this.numericUpDownMaxAngle = new System.Windows.Forms.NumericUpDown();
		this.labelSpeedRamp = new System.Windows.Forms.Label();
		this.labelMaxAngle = new System.Windows.Forms.Label();
		this.numericUpDownSpeedRamp = new System.Windows.Forms.NumericUpDown();
		this.buttonWriteSaveSetting = new System.Windows.Forms.Button();
		this.buttonReadSaveSetting = new System.Windows.Forms.Button();
		this.buttonRebootDevice = new System.Windows.Forms.Button();
		this.TabControl = new System.Windows.Forms.TabControl();
		this.tabPage4 = new System.Windows.Forms.TabPage();
		this.richTextBoxLog = new System.Windows.Forms.RichTextBox();
		this.panel17 = new System.Windows.Forms.Panel();
		this.buttonClearLog = new System.Windows.Forms.Button();
		this.panel20 = new System.Windows.Forms.Panel();
		this.buttonReadMultiAngle = new System.Windows.Forms.Button();
		this.buttonSetMotorZeroRam = new System.Windows.Forms.Button();
		this.buttonReadSingleAngle = new System.Windows.Forms.Button();
		this.buttonClearMotorLoops = new System.Windows.Forms.Button();
		this.textBoxMultiAngle = new System.Windows.Forms.TextBox();
		this.textBoxSingleAngle = new System.Windows.Forms.TextBox();
		this.panel1 = new System.Windows.Forms.Panel();
		this.panel19 = new System.Windows.Forms.Panel();
		this.label18 = new System.Windows.Forms.Label();
		this.labelBusCurrent = new System.Windows.Forms.Label();
		this.buttonBrakeRelease = new System.Windows.Forms.Button();
		this.buttonBrake = new System.Windows.Forms.Button();
		this.buttonClearError = new System.Windows.Forms.Button();
		this.checkBoxLostInputProtection = new System.Windows.Forms.CheckBox();
		this.label32 = new System.Windows.Forms.Label();
		this.labelIc = new System.Windows.Forms.Label();
		this.checkBoxUnderVoltageProtection = new System.Windows.Forms.CheckBox();
		this.checkBoxMotorStallProtection = new System.Windows.Forms.CheckBox();
		this.label26 = new System.Windows.Forms.Label();
		this.label66 = new System.Windows.Forms.Label();
		this.checkBoxShortCircuitProtection = new System.Windows.Forms.CheckBox();
		this.buttonReadState3 = new System.Windows.Forms.Button();
		this.checkBoxOverCurrentProtection = new System.Windows.Forms.CheckBox();
		this.checkBoxOverVoltageProtection = new System.Windows.Forms.CheckBox();
		this.checkBoxMotorTemperatureProtection = new System.Windows.Forms.CheckBox();
		this.checkBoxDriverTemperatureProtection = new System.Windows.Forms.CheckBox();
		this.buttonReadState2 = new System.Windows.Forms.Button();
		this.label25 = new System.Windows.Forms.Label();
		this.buttonReadState1 = new System.Windows.Forms.Button();
		this.labelIb = new System.Windows.Forms.Label();
		this.label38 = new System.Windows.Forms.Label();
		this.label64 = new System.Windows.Forms.Label();
		this.label27 = new System.Windows.Forms.Label();
		this.labelIa = new System.Windows.Forms.Label();
		this.labelBusVoltage = new System.Windows.Forms.Label();
		this.label61 = new System.Windows.Forms.Label();
		this.labelMotorTemperature = new System.Windows.Forms.Label();
		this.labelIq = new System.Windows.Forms.Label();
		this.labelSpeed = new System.Windows.Forms.Label();
		this.labelEncoder = new System.Windows.Forms.Label();
		this.panel21 = new System.Windows.Forms.Panel();
		this.panel18 = new System.Windows.Forms.Panel();
		this.label29 = new System.Windows.Forms.Label();
		this.buttonSendControlCommand = new System.Windows.Forms.Button();
		this.buttonMotorRestore = new System.Windows.Forms.Button();
		this.numericUpDownControlAngle = new System.Windows.Forms.NumericUpDown();
		this.label35 = new System.Windows.Forms.Label();
		this.numericUpDownControlSpeed = new System.Windows.Forms.NumericUpDown();
		this.label36 = new System.Windows.Forms.Label();
		this.checkBoxControlReverse = new System.Windows.Forms.CheckBox();
		this.comboBoxSelectControlMode = new System.Windows.Forms.ComboBox();
		this.numericUpDownControlTorque = new System.Windows.Forms.NumericUpDown();
		this.label30 = new System.Windows.Forms.Label();
		this.buttonMotorStop = new System.Windows.Forms.Button();
		this.tabPage5 = new System.Windows.Forms.TabPage();
		this.label46 = new System.Windows.Forms.Label();
		this.pictureBox3 = new System.Windows.Forms.PictureBox();
		this.label47 = new System.Windows.Forms.Label();
		this.pictureBox2 = new System.Windows.Forms.PictureBox();
		this.pictureBox4 = new System.Windows.Forms.PictureBox();
		this.buttonMotorOn = new System.Windows.Forms.Button();
		this.buttonMotorOff = new System.Windows.Forms.Button();
		this.label12 = new System.Windows.Forms.Label();
		this.labelErrorCount = new System.Windows.Forms.Label();
		this.pictureBox1 = new System.Windows.Forms.PictureBox();
		this.panel6 = new System.Windows.Forms.Panel();
		this.panel8 = new System.Windows.Forms.Panel();
		this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
		((System.ComponentModel.ISupportInitialize)this.numericUpDownSelectId).BeginInit();
		this.tabPage1.SuspendLayout();
		this.panelWriteProgressFull.SuspendLayout();
		this.tabPage2.SuspendLayout();
		this.panel12.SuspendLayout();
		this.panel15.SuspendLayout();
		this.panel14.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownReductionRatio).BeginInit();
		this.panel13.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownAlignVoltage).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownMotorPoles).BeginInit();
		this.tabPage3.SuspendLayout();
		this.panel3.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownProtectStallTime).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownProtectOverCurrentTime).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownProtectOverCurrent).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownProtectDriverTemp).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownProtectOverVoltage).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownProtectMotorTemp).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownProtectUnderVoltage).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownProtectLostInputTime).BeginInit();
		this.panel2.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownDriverID).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownBrakeResOnVoltage).BeginInit();
		this.panel7.SuspendLayout();
		this.panel5.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownSpeedKp).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownAngleKp).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownCurrentKp).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownSpeedKi).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownCurrentKi).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownAngleKi).BeginInit();
		this.panel9.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownCurrentRamp).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownMaxTorque).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownMaxSpeed).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownMaxAngle).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownSpeedRamp).BeginInit();
		this.TabControl.SuspendLayout();
		this.tabPage4.SuspendLayout();
		this.panel17.SuspendLayout();
		this.panel20.SuspendLayout();
		this.panel1.SuspendLayout();
		this.panel19.SuspendLayout();
		this.panel18.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownControlAngle).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownControlSpeed).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownControlTorque).BeginInit();
		this.tabPage5.SuspendLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox3).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.pictureBox2).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.pictureBox4).BeginInit();
		((System.ComponentModel.ISupportInitialize)this.pictureBox1).BeginInit();
		this.panel6.SuspendLayout();
		this.panel8.SuspendLayout();
		base.SuspendLayout();
		this.comboBoxSelectCom.BackColor = System.Drawing.SystemColors.Window;
		this.comboBoxSelectCom.FormattingEnabled = true;
		this.comboBoxSelectCom.Location = new System.Drawing.Point(123, 10);
		this.comboBoxSelectCom.Margin = new System.Windows.Forms.Padding(6);
		this.comboBoxSelectCom.Name = "comboBoxSelectCom";
		this.comboBoxSelectCom.Size = new System.Drawing.Size(105, 30);
		this.comboBoxSelectCom.TabIndex = 0;
		this.comboBoxSelectCom.SelectedIndexChanged += new System.EventHandler(comboBoxSelectCom_SelectedIndexChanged);
		this.comboBoxSelectCom.Click += new System.EventHandler(comboBoxSelectCom_Click);
		this.label1.AutoSize = true;
		this.label1.Location = new System.Drawing.Point(2, 14);
		this.label1.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
		this.label1.Name = "label1";
		this.label1.Size = new System.Drawing.Size(113, 22);
		this.label1.TabIndex = 1000;
		this.label1.Text = "Select COM";
		this.buttonConnect.BackColor = System.Drawing.SystemColors.Window;
		this.buttonConnect.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonConnect.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonConnect.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonConnect.Location = new System.Drawing.Point(617, 8);
		this.buttonConnect.Margin = new System.Windows.Forms.Padding(6);
		this.buttonConnect.Name = "buttonConnect";
		this.buttonConnect.Size = new System.Drawing.Size(150, 35);
		this.buttonConnect.TabIndex = 3;
		this.buttonConnect.Text = "CONNECT";
		this.buttonConnect.UseVisualStyleBackColor = false;
		this.buttonConnect.Click += new System.EventHandler(buttonConnect_Click);
		this.label6.AutoSize = true;
		this.label6.Location = new System.Drawing.Point(243, 14);
		this.label6.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
		this.label6.Name = "label6";
		this.label6.Size = new System.Drawing.Size(86, 22);
		this.label6.TabIndex = 1000;
		this.label6.Text = "Baudrate";
		this.comboBoxSelectBaudRate.BackColor = System.Drawing.SystemColors.Window;
		this.comboBoxSelectBaudRate.FormattingEnabled = true;
		this.comboBoxSelectBaudRate.Items.AddRange(new object[10] { "9600", "19200", "38400", "57600", "115200", "230400", "460800", "1000000", "2000000", "4000000" });
		this.comboBoxSelectBaudRate.Location = new System.Drawing.Point(335, 10);
		this.comboBoxSelectBaudRate.Margin = new System.Windows.Forms.Padding(6);
		this.comboBoxSelectBaudRate.Name = "comboBoxSelectBaudRate";
		this.comboBoxSelectBaudRate.Size = new System.Drawing.Size(105, 30);
		this.comboBoxSelectBaudRate.TabIndex = 1;
		this.comboBoxSelectBaudRate.SelectedIndexChanged += new System.EventHandler(comboBoxSelectBaudRate_SelectedIndexChanged);
		this.numericUpDownSelectId.Location = new System.Drawing.Point(487, 11);
		this.numericUpDownSelectId.Margin = new System.Windows.Forms.Padding(2);
		this.numericUpDownSelectId.Maximum = new decimal(new int[4] { 32, 0, 0, 0 });
		this.numericUpDownSelectId.Minimum = new decimal(new int[4] { 1, 0, 0, 0 });
		this.numericUpDownSelectId.Name = "numericUpDownSelectId";
		this.numericUpDownSelectId.Size = new System.Drawing.Size(105, 29);
		this.numericUpDownSelectId.TabIndex = 2;
		this.numericUpDownSelectId.Value = new decimal(new int[4] { 1, 0, 0, 0 });
		this.numericUpDownSelectId.ValueChanged += new System.EventHandler(numericUpDownSelectId_ValueChanged);
		this.label23.AutoSize = true;
		this.label23.Location = new System.Drawing.Point(454, 14);
		this.label23.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
		this.label23.Name = "label23";
		this.label23.Size = new System.Drawing.Size(30, 22);
		this.label23.TabIndex = 1000;
		this.label23.Text = "ID";
		this.tabPage1.Controls.Add(this.labelChipId);
		this.tabPage1.Controls.Add(this.labelMotorVersion);
		this.tabPage1.Controls.Add(this.textBoxFilePath);
		this.tabPage1.Controls.Add(this.panelWriteProgressFull);
		this.tabPage1.Controls.Add(this.buttonOpenFirmware);
		this.tabPage1.Controls.Add(this.buttonWriteFirmware);
		this.tabPage1.Controls.Add(this.labelMotorName);
		this.tabPage1.Controls.Add(this.buttonReadProductInfo);
		this.tabPage1.Controls.Add(this.labelFirmwareVersoin);
		this.tabPage1.Controls.Add(this.labelDriverName);
		this.tabPage1.Controls.Add(this.labelHardwareVersion);
		this.tabPage1.Location = new System.Drawing.Point(4, 31);
		this.tabPage1.Margin = new System.Windows.Forms.Padding(2);
		this.tabPage1.Name = "tabPage1";
		this.tabPage1.Padding = new System.Windows.Forms.Padding(2);
		this.tabPage1.Size = new System.Drawing.Size(936, 601);
		this.tabPage1.TabIndex = 4;
		this.tabPage1.Text = "Product";
		this.tabPage1.UseVisualStyleBackColor = true;
		this.labelChipId.AutoSize = true;
		this.labelChipId.Font = new System.Drawing.Font("Arial", 12f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.labelChipId.Location = new System.Drawing.Point(109, 462);
		this.labelChipId.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.labelChipId.Name = "labelChipId";
		this.labelChipId.Size = new System.Drawing.Size(60, 18);
		this.labelChipId.TabIndex = 46;
		this.labelChipId.Text = "Chip ID";
		this.labelMotorVersion.AutoSize = true;
		this.labelMotorVersion.Font = new System.Drawing.Font("Arial", 15f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.labelMotorVersion.Location = new System.Drawing.Point(109, 100);
		this.labelMotorVersion.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
		this.labelMotorVersion.Name = "labelMotorVersion";
		this.labelMotorVersion.Size = new System.Drawing.Size(130, 23);
		this.labelMotorVersion.TabIndex = 60;
		this.labelMotorVersion.Text = "Motor version";
		this.textBoxFilePath.Location = new System.Drawing.Point(110, 330);
		this.textBoxFilePath.Margin = new System.Windows.Forms.Padding(2);
		this.textBoxFilePath.Name = "textBoxFilePath";
		this.textBoxFilePath.Size = new System.Drawing.Size(599, 29);
		this.textBoxFilePath.TabIndex = 0;
		this.panelWriteProgressFull.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
		this.panelWriteProgressFull.Controls.Add(this.panelWriteProgress);
		this.panelWriteProgressFull.Location = new System.Drawing.Point(109, 383);
		this.panelWriteProgressFull.Margin = new System.Windows.Forms.Padding(2);
		this.panelWriteProgressFull.Name = "panelWriteProgressFull";
		this.panelWriteProgressFull.Size = new System.Drawing.Size(600, 29);
		this.panelWriteProgressFull.TabIndex = 2;
		this.panelWriteProgress.BackColor = System.Drawing.SystemColors.Highlight;
		this.panelWriteProgress.Dock = System.Windows.Forms.DockStyle.Left;
		this.panelWriteProgress.Location = new System.Drawing.Point(0, 0);
		this.panelWriteProgress.Margin = new System.Windows.Forms.Padding(2);
		this.panelWriteProgress.Name = "panelWriteProgress";
		this.panelWriteProgress.Size = new System.Drawing.Size(0, 27);
		this.panelWriteProgress.TabIndex = 55;
		this.buttonOpenFirmware.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonOpenFirmware.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonOpenFirmware.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonOpenFirmware.Font = new System.Drawing.Font("Arial", 12f);
		this.buttonOpenFirmware.Location = new System.Drawing.Point(729, 330);
		this.buttonOpenFirmware.Margin = new System.Windows.Forms.Padding(2);
		this.buttonOpenFirmware.Name = "buttonOpenFirmware";
		this.buttonOpenFirmware.Size = new System.Drawing.Size(100, 29);
		this.buttonOpenFirmware.TabIndex = 1;
		this.buttonOpenFirmware.Text = "Open File";
		this.buttonOpenFirmware.UseVisualStyleBackColor = false;
		this.buttonOpenFirmware.Click += new System.EventHandler(buttonOpenFirmware_Click);
		this.buttonWriteFirmware.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonWriteFirmware.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonWriteFirmware.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonWriteFirmware.Font = new System.Drawing.Font("Arial", 12f);
		this.buttonWriteFirmware.Location = new System.Drawing.Point(729, 383);
		this.buttonWriteFirmware.Margin = new System.Windows.Forms.Padding(2);
		this.buttonWriteFirmware.Name = "buttonWriteFirmware";
		this.buttonWriteFirmware.Size = new System.Drawing.Size(100, 29);
		this.buttonWriteFirmware.TabIndex = 3;
		this.buttonWriteFirmware.Text = "Download";
		this.buttonWriteFirmware.UseVisualStyleBackColor = false;
		this.buttonWriteFirmware.Click += new System.EventHandler(buttonWriteFirmware_Click);
		this.labelMotorName.AutoSize = true;
		this.labelMotorName.Font = new System.Drawing.Font("Arial", 15f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.labelMotorName.Location = new System.Drawing.Point(109, 55);
		this.labelMotorName.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
		this.labelMotorName.Name = "labelMotorName";
		this.labelMotorName.Size = new System.Drawing.Size(62, 23);
		this.labelMotorName.TabIndex = 50;
		this.labelMotorName.Text = "Motor";
		this.buttonReadProductInfo.BackColor = System.Drawing.SystemColors.Window;
		this.buttonReadProductInfo.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonReadProductInfo.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonReadProductInfo.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonReadProductInfo.Location = new System.Drawing.Point(800, 518);
		this.buttonReadProductInfo.Margin = new System.Windows.Forms.Padding(6);
		this.buttonReadProductInfo.Name = "buttonReadProductInfo";
		this.buttonReadProductInfo.Size = new System.Drawing.Size(128, 35);
		this.buttonReadProductInfo.TabIndex = 4;
		this.buttonReadProductInfo.Text = "Read Info";
		this.buttonReadProductInfo.UseVisualStyleBackColor = false;
		this.buttonReadProductInfo.Click += new System.EventHandler(buttonReadProductInfo_Click);
		this.labelFirmwareVersoin.AutoSize = true;
		this.labelFirmwareVersoin.Font = new System.Drawing.Font("Arial", 15f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.labelFirmwareVersoin.Location = new System.Drawing.Point(109, 235);
		this.labelFirmwareVersoin.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
		this.labelFirmwareVersoin.Name = "labelFirmwareVersoin";
		this.labelFirmwareVersoin.Size = new System.Drawing.Size(161, 23);
		this.labelFirmwareVersoin.TabIndex = 48;
		this.labelFirmwareVersoin.Text = "Firmware version";
		this.labelDriverName.AutoSize = true;
		this.labelDriverName.Font = new System.Drawing.Font("Arial", 15f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.labelDriverName.Location = new System.Drawing.Point(109, 145);
		this.labelDriverName.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
		this.labelDriverName.Name = "labelDriverName";
		this.labelDriverName.Size = new System.Drawing.Size(62, 23);
		this.labelDriverName.TabIndex = 46;
		this.labelDriverName.Text = "Driver";
		this.labelHardwareVersion.AutoSize = true;
		this.labelHardwareVersion.Font = new System.Drawing.Font("Arial", 15f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.labelHardwareVersion.Location = new System.Drawing.Point(109, 189);
		this.labelHardwareVersion.Margin = new System.Windows.Forms.Padding(6, 0, 6, 0);
		this.labelHardwareVersion.Name = "labelHardwareVersion";
		this.labelHardwareVersion.Size = new System.Drawing.Size(164, 23);
		this.labelHardwareVersion.TabIndex = 47;
		this.labelHardwareVersion.Text = "Hardware version";
		this.tabPage2.Controls.Add(this.panel12);
		this.tabPage2.Location = new System.Drawing.Point(4, 31);
		this.tabPage2.Margin = new System.Windows.Forms.Padding(2);
		this.tabPage2.Name = "tabPage2";
		this.tabPage2.Padding = new System.Windows.Forms.Padding(2);
		this.tabPage2.Size = new System.Drawing.Size(936, 601);
		this.tabPage2.TabIndex = 1;
		this.tabPage2.Text = "Encoder";
		this.tabPage2.UseVisualStyleBackColor = true;
		this.panel12.Controls.Add(this.panel15);
		this.panel12.Controls.Add(this.buttonReadSaveCalib);
		this.panel12.Controls.Add(this.buttonWriteSaveCalib);
		this.panel12.Dock = System.Windows.Forms.DockStyle.Fill;
		this.panel12.Location = new System.Drawing.Point(2, 2);
		this.panel12.Name = "panel12";
		this.panel12.Size = new System.Drawing.Size(932, 597);
		this.panel12.TabIndex = 38;
		this.panel15.Controls.Add(this.panel14);
		this.panel15.Controls.Add(this.panel16);
		this.panel15.Controls.Add(this.panel13);
		this.panel15.Dock = System.Windows.Forms.DockStyle.Top;
		this.panel15.Location = new System.Drawing.Point(0, 0);
		this.panel15.Name = "panel15";
		this.panel15.Size = new System.Drawing.Size(932, 389);
		this.panel15.TabIndex = 55;
		this.panel14.Controls.Add(this.label58);
		this.panel14.Controls.Add(this.label56);
		this.panel14.Controls.Add(this.buttonReducerEncoderAlign);
		this.panel14.Controls.Add(this.textBoxReducerZeroPosition);
		this.panel14.Controls.Add(this.label55);
		this.panel14.Controls.Add(this.textBoxReducerAlignValue);
		this.panel14.Controls.Add(this.buttonSetReducerEncoderZeroPosition);
		this.panel14.Controls.Add(this.label54);
		this.panel14.Controls.Add(this.numericUpDownReductionRatio);
		this.panel14.Dock = System.Windows.Forms.DockStyle.Fill;
		this.panel14.Location = new System.Drawing.Point(469, 0);
		this.panel14.Name = "panel14";
		this.panel14.Size = new System.Drawing.Size(463, 389);
		this.panel14.TabIndex = 54;
		this.label58.AutoSize = true;
		this.label58.Font = new System.Drawing.Font("Arial", 15.75f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.label58.ForeColor = System.Drawing.SystemColors.ActiveCaption;
		this.label58.Location = new System.Drawing.Point(3, 3);
		this.label58.Name = "label58";
		this.label58.Size = new System.Drawing.Size(256, 24);
		this.label58.TabIndex = 68;
		this.label58.Text = "Reducer / Encoder Setting";
		this.label56.AutoSize = true;
		this.label56.Location = new System.Drawing.Point(20, 130);
		this.label56.Name = "label56";
		this.label56.Size = new System.Drawing.Size(201, 22);
		this.label56.TabIndex = 52;
		this.label56.Text = "Reducer Zero Position";
		this.buttonReducerEncoderAlign.BackColor = System.Drawing.SystemColors.Window;
		this.buttonReducerEncoderAlign.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonReducerEncoderAlign.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonReducerEncoderAlign.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonReducerEncoderAlign.Font = new System.Drawing.Font("Arial", 12f);
		this.buttonReducerEncoderAlign.Location = new System.Drawing.Point(368, 82);
		this.buttonReducerEncoderAlign.Margin = new System.Windows.Forms.Padding(6);
		this.buttonReducerEncoderAlign.Name = "buttonReducerEncoderAlign";
		this.buttonReducerEncoderAlign.Size = new System.Drawing.Size(70, 29);
		this.buttonReducerEncoderAlign.TabIndex = 12;
		this.buttonReducerEncoderAlign.Text = "Clear";
		this.buttonReducerEncoderAlign.UseVisualStyleBackColor = false;
		this.buttonReducerEncoderAlign.Click += new System.EventHandler(buttonReducerEncoderAlign_Click);
		this.textBoxReducerZeroPosition.Location = new System.Drawing.Point(283, 127);
		this.textBoxReducerZeroPosition.Name = "textBoxReducerZeroPosition";
		this.textBoxReducerZeroPosition.ReadOnly = true;
		this.textBoxReducerZeroPosition.Size = new System.Drawing.Size(80, 29);
		this.textBoxReducerZeroPosition.TabIndex = 13;
		this.textBoxReducerZeroPosition.Text = "0";
		this.label55.AutoSize = true;
		this.label55.Location = new System.Drawing.Point(20, 85);
		this.label55.Name = "label55";
		this.label55.Size = new System.Drawing.Size(258, 22);
		this.label55.TabIndex = 50;
		this.label55.Text = "Reducer/Encoder Align Value";
		this.textBoxReducerAlignValue.Location = new System.Drawing.Point(283, 82);
		this.textBoxReducerAlignValue.Name = "textBoxReducerAlignValue";
		this.textBoxReducerAlignValue.ReadOnly = true;
		this.textBoxReducerAlignValue.Size = new System.Drawing.Size(80, 29);
		this.textBoxReducerAlignValue.TabIndex = 11;
		this.textBoxReducerAlignValue.Text = "0";
		this.buttonSetReducerEncoderZeroPosition.BackColor = System.Drawing.SystemColors.Window;
		this.buttonSetReducerEncoderZeroPosition.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonSetReducerEncoderZeroPosition.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonSetReducerEncoderZeroPosition.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonSetReducerEncoderZeroPosition.Font = new System.Drawing.Font("Arial", 12f);
		this.buttonSetReducerEncoderZeroPosition.Location = new System.Drawing.Point(368, 127);
		this.buttonSetReducerEncoderZeroPosition.Margin = new System.Windows.Forms.Padding(6);
		this.buttonSetReducerEncoderZeroPosition.Name = "buttonSetReducerEncoderZeroPosition";
		this.buttonSetReducerEncoderZeroPosition.Size = new System.Drawing.Size(70, 29);
		this.buttonSetReducerEncoderZeroPosition.TabIndex = 14;
		this.buttonSetReducerEncoderZeroPosition.Text = "Set";
		this.buttonSetReducerEncoderZeroPosition.UseVisualStyleBackColor = false;
		this.buttonSetReducerEncoderZeroPosition.Click += new System.EventHandler(buttonSetReducerEncoderZeroPosition_Click);
		this.label54.AutoSize = true;
		this.label54.Location = new System.Drawing.Point(20, 40);
		this.label54.Name = "label54";
		this.label54.Size = new System.Drawing.Size(145, 22);
		this.label54.TabIndex = 47;
		this.label54.Text = "Reduction Ratio";
		this.numericUpDownReductionRatio.Enabled = false;
		this.numericUpDownReductionRatio.Location = new System.Drawing.Point(283, 37);
		this.numericUpDownReductionRatio.Maximum = new decimal(new int[4] { 1000, 0, 0, 0 });
		this.numericUpDownReductionRatio.Name = "numericUpDownReductionRatio";
		this.numericUpDownReductionRatio.ReadOnly = true;
		this.numericUpDownReductionRatio.Size = new System.Drawing.Size(155, 29);
		this.numericUpDownReductionRatio.TabIndex = 10;
		this.numericUpDownReductionRatio.Value = new decimal(new int[4] { 8, 0, 0, 0 });
		this.panel16.BackColor = System.Drawing.Color.WhiteSmoke;
		this.panel16.Dock = System.Windows.Forms.DockStyle.Left;
		this.panel16.Location = new System.Drawing.Point(464, 0);
		this.panel16.Name = "panel16";
		this.panel16.Size = new System.Drawing.Size(5, 389);
		this.panel16.TabIndex = 77;
		this.panel13.Controls.Add(this.label57);
		this.panel13.Controls.Add(this.buttonSetMotorEncoderZeroPosition);
		this.panel13.Controls.Add(this.label2);
		this.panel13.Controls.Add(this.textBoxMotorZeroPosition);
		this.panel13.Controls.Add(this.textBoxMotorEncoderAlignRatio);
		this.panel13.Controls.Add(this.numericUpDownAlignVoltage);
		this.panel13.Controls.Add(this.numericUpDownMotorPoles);
		this.panel13.Controls.Add(this.textBoxMotorEncoderOffset);
		this.panel13.Controls.Add(this.label5);
		this.panel13.Controls.Add(this.label3);
		this.panel13.Controls.Add(this.textBoxMotorPhaseSequence);
		this.panel13.Controls.Add(this.label4);
		this.panel13.Controls.Add(this.buttonMotorEncoderAlign);
		this.panel13.Controls.Add(this.label28);
		this.panel13.Controls.Add(this.textBoxEncoderType);
		this.panel13.Controls.Add(this.textBoxEncoderPosition);
		this.panel13.Controls.Add(this.label7);
		this.panel13.Controls.Add(this.label9);
		this.panel13.Controls.Add(this.label8);
		this.panel13.Dock = System.Windows.Forms.DockStyle.Left;
		this.panel13.Location = new System.Drawing.Point(0, 0);
		this.panel13.Name = "panel13";
		this.panel13.Size = new System.Drawing.Size(464, 389);
		this.panel13.TabIndex = 53;
		this.label57.AutoSize = true;
		this.label57.Font = new System.Drawing.Font("Arial", 15.75f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.label57.ForeColor = System.Drawing.SystemColors.ActiveCaption;
		this.label57.Location = new System.Drawing.Point(3, 3);
		this.label57.Name = "label57";
		this.label57.Size = new System.Drawing.Size(231, 24);
		this.label57.TabIndex = 68;
		this.label57.Text = "Motor / Encoder Setting";
		this.buttonSetMotorEncoderZeroPosition.BackColor = System.Drawing.SystemColors.Window;
		this.buttonSetMotorEncoderZeroPosition.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonSetMotorEncoderZeroPosition.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonSetMotorEncoderZeroPosition.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonSetMotorEncoderZeroPosition.Font = new System.Drawing.Font("Arial", 12f);
		this.buttonSetMotorEncoderZeroPosition.Location = new System.Drawing.Point(368, 352);
		this.buttonSetMotorEncoderZeroPosition.Margin = new System.Windows.Forms.Padding(6);
		this.buttonSetMotorEncoderZeroPosition.Name = "buttonSetMotorEncoderZeroPosition";
		this.buttonSetMotorEncoderZeroPosition.Size = new System.Drawing.Size(70, 29);
		this.buttonSetMotorEncoderZeroPosition.TabIndex = 9;
		this.buttonSetMotorEncoderZeroPosition.Text = "Set";
		this.buttonSetMotorEncoderZeroPosition.UseVisualStyleBackColor = false;
		this.buttonSetMotorEncoderZeroPosition.Click += new System.EventHandler(buttonbuttonSetMotorEncoderZeroPosition_Click);
		this.label2.AutoSize = true;
		this.label2.Location = new System.Drawing.Point(20, 40);
		this.label2.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label2.Name = "label2";
		this.label2.Size = new System.Drawing.Size(112, 22);
		this.label2.TabIndex = 1;
		this.label2.Text = "Motor Poles";
		this.textBoxMotorZeroPosition.Location = new System.Drawing.Point(283, 352);
		this.textBoxMotorZeroPosition.Margin = new System.Windows.Forms.Padding(2);
		this.textBoxMotorZeroPosition.Name = "textBoxMotorZeroPosition";
		this.textBoxMotorZeroPosition.ReadOnly = true;
		this.textBoxMotorZeroPosition.Size = new System.Drawing.Size(80, 29);
		this.textBoxMotorZeroPosition.TabIndex = 8;
		this.textBoxMotorZeroPosition.Text = "0";
		this.textBoxMotorEncoderAlignRatio.Location = new System.Drawing.Point(283, 262);
		this.textBoxMotorEncoderAlignRatio.Margin = new System.Windows.Forms.Padding(2);
		this.textBoxMotorEncoderAlignRatio.Name = "textBoxMotorEncoderAlignRatio";
		this.textBoxMotorEncoderAlignRatio.ReadOnly = true;
		this.textBoxMotorEncoderAlignRatio.Size = new System.Drawing.Size(155, 29);
		this.textBoxMotorEncoderAlignRatio.TabIndex = 3;
		this.textBoxMotorEncoderAlignRatio.Text = "0";
		this.numericUpDownAlignVoltage.DecimalPlaces = 2;
		this.numericUpDownAlignVoltage.Increment = new decimal(new int[4] { 1, 0, 0, 65536 });
		this.numericUpDownAlignVoltage.Location = new System.Drawing.Point(283, 307);
		this.numericUpDownAlignVoltage.Margin = new System.Windows.Forms.Padding(2);
		this.numericUpDownAlignVoltage.Maximum = new decimal(new int[4] { 24, 0, 0, 0 });
		this.numericUpDownAlignVoltage.Name = "numericUpDownAlignVoltage";
		this.numericUpDownAlignVoltage.Size = new System.Drawing.Size(80, 29);
		this.numericUpDownAlignVoltage.TabIndex = 6;
		this.numericUpDownAlignVoltage.Value = new decimal(new int[4] { 25, 0, 0, 65536 });
		this.numericUpDownMotorPoles.Increment = new decimal(new int[4] { 2, 0, 0, 0 });
		this.numericUpDownMotorPoles.Location = new System.Drawing.Point(283, 37);
		this.numericUpDownMotorPoles.Margin = new System.Windows.Forms.Padding(2);
		this.numericUpDownMotorPoles.Maximum = new decimal(new int[4] { 128, 0, 0, 0 });
		this.numericUpDownMotorPoles.Name = "numericUpDownMotorPoles";
		this.numericUpDownMotorPoles.Size = new System.Drawing.Size(155, 29);
		this.numericUpDownMotorPoles.TabIndex = 0;
		this.numericUpDownMotorPoles.Value = new decimal(new int[4] { 28, 0, 0, 0 });
		this.textBoxMotorEncoderOffset.Location = new System.Drawing.Point(283, 217);
		this.textBoxMotorEncoderOffset.Margin = new System.Windows.Forms.Padding(2);
		this.textBoxMotorEncoderOffset.Name = "textBoxMotorEncoderOffset";
		this.textBoxMotorEncoderOffset.ReadOnly = true;
		this.textBoxMotorEncoderOffset.Size = new System.Drawing.Size(155, 29);
		this.textBoxMotorEncoderOffset.TabIndex = 4;
		this.textBoxMotorEncoderOffset.Text = "0";
		this.label5.AutoSize = true;
		this.label5.Location = new System.Drawing.Point(20, 355);
		this.label5.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label5.Name = "label5";
		this.label5.Size = new System.Drawing.Size(234, 22);
		this.label5.TabIndex = 10;
		this.label5.Text = "Motor Zero Position (Rom)";
		this.label3.AutoSize = true;
		this.label3.Location = new System.Drawing.Point(20, 265);
		this.label3.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label3.Name = "label3";
		this.label3.Size = new System.Drawing.Size(230, 22);
		this.label3.TabIndex = 3;
		this.label3.Text = "Motor/Encoder Align Ratio";
		this.textBoxMotorPhaseSequence.Location = new System.Drawing.Point(283, 172);
		this.textBoxMotorPhaseSequence.Margin = new System.Windows.Forms.Padding(2);
		this.textBoxMotorPhaseSequence.Name = "textBoxMotorPhaseSequence";
		this.textBoxMotorPhaseSequence.ReadOnly = true;
		this.textBoxMotorPhaseSequence.Size = new System.Drawing.Size(155, 29);
		this.textBoxMotorPhaseSequence.TabIndex = 5;
		this.label4.AutoSize = true;
		this.label4.Location = new System.Drawing.Point(20, 85);
		this.label4.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label4.Name = "label4";
		this.label4.Size = new System.Drawing.Size(129, 22);
		this.label4.TabIndex = 4;
		this.label4.Text = "Encoder Type";
		this.buttonMotorEncoderAlign.BackColor = System.Drawing.SystemColors.Window;
		this.buttonMotorEncoderAlign.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonMotorEncoderAlign.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonMotorEncoderAlign.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonMotorEncoderAlign.Font = new System.Drawing.Font("Arial", 12f);
		this.buttonMotorEncoderAlign.Location = new System.Drawing.Point(368, 307);
		this.buttonMotorEncoderAlign.Margin = new System.Windows.Forms.Padding(6);
		this.buttonMotorEncoderAlign.Name = "buttonMotorEncoderAlign";
		this.buttonMotorEncoderAlign.Size = new System.Drawing.Size(70, 29);
		this.buttonMotorEncoderAlign.TabIndex = 7;
		this.buttonMotorEncoderAlign.Text = "Align";
		this.buttonMotorEncoderAlign.UseVisualStyleBackColor = false;
		this.buttonMotorEncoderAlign.Click += new System.EventHandler(buttonMotorEncoderAlign_Click);
		this.label28.AutoSize = true;
		this.label28.Location = new System.Drawing.Point(20, 130);
		this.label28.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label28.Name = "label28";
		this.label28.Size = new System.Drawing.Size(155, 22);
		this.label28.TabIndex = 37;
		this.label28.Text = "Encoder Position";
		this.textBoxEncoderType.Location = new System.Drawing.Point(283, 82);
		this.textBoxEncoderType.Margin = new System.Windows.Forms.Padding(2);
		this.textBoxEncoderType.Name = "textBoxEncoderType";
		this.textBoxEncoderType.ReadOnly = true;
		this.textBoxEncoderType.Size = new System.Drawing.Size(155, 29);
		this.textBoxEncoderType.TabIndex = 1;
		this.textBoxEncoderPosition.Location = new System.Drawing.Point(283, 127);
		this.textBoxEncoderPosition.Margin = new System.Windows.Forms.Padding(2);
		this.textBoxEncoderPosition.Name = "textBoxEncoderPosition";
		this.textBoxEncoderPosition.ReadOnly = true;
		this.textBoxEncoderPosition.Size = new System.Drawing.Size(155, 29);
		this.textBoxEncoderPosition.TabIndex = 2;
		this.label7.AutoSize = true;
		this.label7.Location = new System.Drawing.Point(20, 220);
		this.label7.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label7.Name = "label7";
		this.label7.Size = new System.Drawing.Size(193, 22);
		this.label7.TabIndex = 31;
		this.label7.Text = "Motor/Encoder Offset";
		this.label9.AutoSize = true;
		this.label9.Location = new System.Drawing.Point(20, 310);
		this.label9.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label9.Name = "label9";
		this.label9.Size = new System.Drawing.Size(250, 22);
		this.label9.TabIndex = 35;
		this.label9.Text = "Motor/Encoder Align Voltage";
		this.label8.AutoSize = true;
		this.label8.Location = new System.Drawing.Point(20, 175);
		this.label8.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label8.Name = "label8";
		this.label8.Size = new System.Drawing.Size(209, 22);
		this.label8.TabIndex = 33;
		this.label8.Text = "Motor Phase Sequence";
		this.buttonReadSaveCalib.BackColor = System.Drawing.SystemColors.Window;
		this.buttonReadSaveCalib.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonReadSaveCalib.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonReadSaveCalib.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonReadSaveCalib.Location = new System.Drawing.Point(796, 517);
		this.buttonReadSaveCalib.Margin = new System.Windows.Forms.Padding(6);
		this.buttonReadSaveCalib.Name = "buttonReadSaveCalib";
		this.buttonReadSaveCalib.Size = new System.Drawing.Size(128, 35);
		this.buttonReadSaveCalib.TabIndex = 16;
		this.buttonReadSaveCalib.Text = "Read";
		this.buttonReadSaveCalib.UseVisualStyleBackColor = false;
		this.buttonReadSaveCalib.Click += new System.EventHandler(buttonReadSaveCalib_Click);
		this.buttonWriteSaveCalib.BackColor = System.Drawing.SystemColors.Window;
		this.buttonWriteSaveCalib.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonWriteSaveCalib.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonWriteSaveCalib.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonWriteSaveCalib.Location = new System.Drawing.Point(796, 459);
		this.buttonWriteSaveCalib.Margin = new System.Windows.Forms.Padding(6);
		this.buttonWriteSaveCalib.Name = "buttonWriteSaveCalib";
		this.buttonWriteSaveCalib.Size = new System.Drawing.Size(128, 35);
		this.buttonWriteSaveCalib.TabIndex = 15;
		this.buttonWriteSaveCalib.Text = "Save";
		this.buttonWriteSaveCalib.UseVisualStyleBackColor = false;
		this.buttonWriteSaveCalib.Click += new System.EventHandler(buttonWriteSaveCalib_Click);
		this.tabPage3.Controls.Add(this.panel3);
		this.tabPage3.Controls.Add(this.panel10);
		this.tabPage3.Controls.Add(this.panel2);
		this.tabPage3.Controls.Add(this.panel4);
		this.tabPage3.Controls.Add(this.panel7);
		this.tabPage3.Font = new System.Drawing.Font("Arial", 12f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.tabPage3.Location = new System.Drawing.Point(4, 31);
		this.tabPage3.Margin = new System.Windows.Forms.Padding(2);
		this.tabPage3.Name = "tabPage3";
		this.tabPage3.Padding = new System.Windows.Forms.Padding(2);
		this.tabPage3.Size = new System.Drawing.Size(936, 601);
		this.tabPage3.TabIndex = 3;
		this.tabPage3.Text = "Setting";
		this.tabPage3.UseVisualStyleBackColor = true;
		this.panel3.Controls.Add(this.label53);
		this.panel3.Controls.Add(this.comboBoxProtectStallEnable);
		this.panel3.Controls.Add(this.numericUpDownProtectStallTime);
		this.panel3.Controls.Add(this.label52);
		this.panel3.Controls.Add(this.comboBoxProtectShortCircuitEnable);
		this.panel3.Controls.Add(this.label43);
		this.panel3.Controls.Add(this.numericUpDownProtectOverCurrentTime);
		this.panel3.Controls.Add(this.label42);
		this.panel3.Controls.Add(this.comboBoxProtectOverCurrentEnable);
		this.panel3.Controls.Add(this.numericUpDownProtectOverCurrent);
		this.panel3.Controls.Add(this.label21);
		this.panel3.Controls.Add(this.comboBoxProtectDriverTempEnable);
		this.panel3.Controls.Add(this.numericUpDownProtectDriverTemp);
		this.panel3.Controls.Add(this.label31);
		this.panel3.Controls.Add(this.comboBoxProtectLostInputEnable);
		this.panel3.Controls.Add(this.comboBoxProtectOverVoltageEnable);
		this.panel3.Controls.Add(this.comboBoxProtectUnderVoltageEnable);
		this.panel3.Controls.Add(this.comboBoxProtectMotorTempEnable);
		this.panel3.Controls.Add(this.numericUpDownProtectOverVoltage);
		this.panel3.Controls.Add(this.label51);
		this.panel3.Controls.Add(this.label39);
		this.panel3.Controls.Add(this.numericUpDownProtectMotorTemp);
		this.panel3.Controls.Add(this.label40);
		this.panel3.Controls.Add(this.numericUpDownProtectUnderVoltage);
		this.panel3.Controls.Add(this.numericUpDownProtectLostInputTime);
		this.panel3.Controls.Add(this.label14);
		this.panel3.Dock = System.Windows.Forms.DockStyle.Fill;
		this.panel3.Location = new System.Drawing.Point(345, 2);
		this.panel3.Margin = new System.Windows.Forms.Padding(2);
		this.panel3.Name = "panel3";
		this.panel3.Size = new System.Drawing.Size(589, 377);
		this.panel3.TabIndex = 68;
		this.label53.AutoSize = true;
		this.label53.Font = new System.Drawing.Font("Arial", 15.75f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.label53.ForeColor = System.Drawing.SystemColors.ActiveCaption;
		this.label53.Location = new System.Drawing.Point(3, 3);
		this.label53.Name = "label53";
		this.label53.Size = new System.Drawing.Size(177, 24);
		this.label53.TabIndex = 69;
		this.label53.Text = "Protection Setting";
		this.comboBoxProtectStallEnable.BackColor = System.Drawing.SystemColors.Window;
		this.comboBoxProtectStallEnable.FormattingEnabled = true;
		this.comboBoxProtectStallEnable.Items.AddRange(new object[3] { "Disable", "Enable (recoverable)", "Enable (not recoverable)" });
		this.comboBoxProtectStallEnable.Location = new System.Drawing.Point(343, 301);
		this.comboBoxProtectStallEnable.Margin = new System.Windows.Forms.Padding(6);
		this.comboBoxProtectStallEnable.Name = "comboBoxProtectStallEnable";
		this.comboBoxProtectStallEnable.Size = new System.Drawing.Size(233, 26);
		this.comboBoxProtectStallEnable.TabIndex = 21;
		this.comboBoxProtectStallEnable.Tag = "";
		this.numericUpDownProtectStallTime.Location = new System.Drawing.Point(229, 301);
		this.numericUpDownProtectStallTime.Margin = new System.Windows.Forms.Padding(2);
		this.numericUpDownProtectStallTime.Maximum = new decimal(new int[4] { 65535, 0, 0, 0 });
		this.numericUpDownProtectStallTime.Name = "numericUpDownProtectStallTime";
		this.numericUpDownProtectStallTime.Size = new System.Drawing.Size(105, 26);
		this.numericUpDownProtectStallTime.TabIndex = 20;
		this.label52.AutoSize = true;
		this.label52.Location = new System.Drawing.Point(7, 305);
		this.label52.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label52.Name = "label52";
		this.label52.Size = new System.Drawing.Size(92, 18);
		this.label52.TabIndex = 78;
		this.label52.Text = "Protect Stall";
		this.comboBoxProtectShortCircuitEnable.BackColor = System.Drawing.SystemColors.Window;
		this.comboBoxProtectShortCircuitEnable.FormattingEnabled = true;
		this.comboBoxProtectShortCircuitEnable.Items.AddRange(new object[3] { "Disable", "Enable (recoverable)", "Enable (not recoverable)" });
		this.comboBoxProtectShortCircuitEnable.Location = new System.Drawing.Point(343, 262);
		this.comboBoxProtectShortCircuitEnable.Margin = new System.Windows.Forms.Padding(6);
		this.comboBoxProtectShortCircuitEnable.Name = "comboBoxProtectShortCircuitEnable";
		this.comboBoxProtectShortCircuitEnable.Size = new System.Drawing.Size(233, 26);
		this.comboBoxProtectShortCircuitEnable.TabIndex = 19;
		this.comboBoxProtectShortCircuitEnable.Tag = "";
		this.label43.AutoSize = true;
		this.label43.Location = new System.Drawing.Point(7, 266);
		this.label43.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label43.Name = "label43";
		this.label43.Size = new System.Drawing.Size(148, 18);
		this.label43.TabIndex = 75;
		this.label43.Text = "Protect Short Circuit";
		this.numericUpDownProtectOverCurrentTime.Location = new System.Drawing.Point(229, 223);
		this.numericUpDownProtectOverCurrentTime.Margin = new System.Windows.Forms.Padding(2);
		this.numericUpDownProtectOverCurrentTime.Maximum = new decimal(new int[4] { 65535, 0, 0, 0 });
		this.numericUpDownProtectOverCurrentTime.Name = "numericUpDownProtectOverCurrentTime";
		this.numericUpDownProtectOverCurrentTime.Size = new System.Drawing.Size(105, 26);
		this.numericUpDownProtectOverCurrentTime.TabIndex = 18;
		this.label42.AutoSize = true;
		this.label42.Location = new System.Drawing.Point(7, 227);
		this.label42.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label42.Name = "label42";
		this.label42.Size = new System.Drawing.Size(188, 18);
		this.label42.TabIndex = 73;
		this.label42.Text = "Protect Over Current Time";
		this.comboBoxProtectOverCurrentEnable.BackColor = System.Drawing.SystemColors.Window;
		this.comboBoxProtectOverCurrentEnable.FormattingEnabled = true;
		this.comboBoxProtectOverCurrentEnable.Items.AddRange(new object[3] { "Disable", "Enable (recoverable)", "Enable (not recoverable)" });
		this.comboBoxProtectOverCurrentEnable.Location = new System.Drawing.Point(343, 184);
		this.comboBoxProtectOverCurrentEnable.Margin = new System.Windows.Forms.Padding(6);
		this.comboBoxProtectOverCurrentEnable.Name = "comboBoxProtectOverCurrentEnable";
		this.comboBoxProtectOverCurrentEnable.Size = new System.Drawing.Size(233, 26);
		this.comboBoxProtectOverCurrentEnable.TabIndex = 17;
		this.comboBoxProtectOverCurrentEnable.Tag = "";
		this.numericUpDownProtectOverCurrent.DecimalPlaces = 2;
		this.numericUpDownProtectOverCurrent.Increment = new decimal(new int[4] { 1, 0, 0, 65536 });
		this.numericUpDownProtectOverCurrent.Location = new System.Drawing.Point(229, 184);
		this.numericUpDownProtectOverCurrent.Margin = new System.Windows.Forms.Padding(2);
		this.numericUpDownProtectOverCurrent.Maximum = new decimal(new int[4] { 15, 0, 0, 0 });
		this.numericUpDownProtectOverCurrent.Minimum = new decimal(new int[4] { 1, 0, 0, 65536 });
		this.numericUpDownProtectOverCurrent.Name = "numericUpDownProtectOverCurrent";
		this.numericUpDownProtectOverCurrent.Size = new System.Drawing.Size(105, 26);
		this.numericUpDownProtectOverCurrent.TabIndex = 16;
		this.numericUpDownProtectOverCurrent.Value = new decimal(new int[4] { 5, 0, 0, 0 });
		this.label21.AutoSize = true;
		this.label21.Location = new System.Drawing.Point(7, 188);
		this.label21.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label21.Name = "label21";
		this.label21.Size = new System.Drawing.Size(150, 18);
		this.label21.TabIndex = 70;
		this.label21.Text = "Protect Over Current";
		this.comboBoxProtectDriverTempEnable.BackColor = System.Drawing.SystemColors.Window;
		this.comboBoxProtectDriverTempEnable.FormattingEnabled = true;
		this.comboBoxProtectDriverTempEnable.Items.AddRange(new object[3] { "Disable", "Enable (recoverable)", "Enable (not recoverable)" });
		this.comboBoxProtectDriverTempEnable.Location = new System.Drawing.Point(343, 67);
		this.comboBoxProtectDriverTempEnable.Margin = new System.Windows.Forms.Padding(6);
		this.comboBoxProtectDriverTempEnable.Name = "comboBoxProtectDriverTempEnable";
		this.comboBoxProtectDriverTempEnable.Size = new System.Drawing.Size(233, 26);
		this.comboBoxProtectDriverTempEnable.TabIndex = 11;
		this.comboBoxProtectDriverTempEnable.Tag = "";
		this.numericUpDownProtectDriverTemp.Location = new System.Drawing.Point(229, 67);
		this.numericUpDownProtectDriverTemp.Margin = new System.Windows.Forms.Padding(2);
		this.numericUpDownProtectDriverTemp.Maximum = new decimal(new int[4] { 180, 0, 0, 0 });
		this.numericUpDownProtectDriverTemp.Minimum = new decimal(new int[4] { 20, 0, 0, 0 });
		this.numericUpDownProtectDriverTemp.Name = "numericUpDownProtectDriverTemp";
		this.numericUpDownProtectDriverTemp.Size = new System.Drawing.Size(105, 26);
		this.numericUpDownProtectDriverTemp.TabIndex = 10;
		this.numericUpDownProtectDriverTemp.Value = new decimal(new int[4] { 100, 0, 0, 0 });
		this.label31.AutoSize = true;
		this.label31.Location = new System.Drawing.Point(7, 71);
		this.label31.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label31.Name = "label31";
		this.label31.Size = new System.Drawing.Size(191, 18);
		this.label31.TabIndex = 68;
		this.label31.Text = "Protect DriverTemperature";
		this.comboBoxProtectLostInputEnable.BackColor = System.Drawing.SystemColors.Window;
		this.comboBoxProtectLostInputEnable.FormattingEnabled = true;
		this.comboBoxProtectLostInputEnable.Items.AddRange(new object[3] { "Disable", "Enable (recoverable)", "Enable (not recoverable)" });
		this.comboBoxProtectLostInputEnable.Location = new System.Drawing.Point(343, 340);
		this.comboBoxProtectLostInputEnable.Margin = new System.Windows.Forms.Padding(6);
		this.comboBoxProtectLostInputEnable.Name = "comboBoxProtectLostInputEnable";
		this.comboBoxProtectLostInputEnable.Size = new System.Drawing.Size(233, 26);
		this.comboBoxProtectLostInputEnable.TabIndex = 23;
		this.comboBoxProtectLostInputEnable.Tag = "";
		this.comboBoxProtectOverVoltageEnable.BackColor = System.Drawing.SystemColors.Window;
		this.comboBoxProtectOverVoltageEnable.FormattingEnabled = true;
		this.comboBoxProtectOverVoltageEnable.Items.AddRange(new object[3] { "Disable", "Enable (recoverable)", "Enable (not recoverable)" });
		this.comboBoxProtectOverVoltageEnable.Location = new System.Drawing.Point(343, 145);
		this.comboBoxProtectOverVoltageEnable.Margin = new System.Windows.Forms.Padding(6);
		this.comboBoxProtectOverVoltageEnable.Name = "comboBoxProtectOverVoltageEnable";
		this.comboBoxProtectOverVoltageEnable.Size = new System.Drawing.Size(233, 26);
		this.comboBoxProtectOverVoltageEnable.TabIndex = 15;
		this.comboBoxProtectOverVoltageEnable.Tag = "";
		this.comboBoxProtectUnderVoltageEnable.BackColor = System.Drawing.SystemColors.Window;
		this.comboBoxProtectUnderVoltageEnable.FormattingEnabled = true;
		this.comboBoxProtectUnderVoltageEnable.Items.AddRange(new object[3] { "Disable", "Enable (recoverable)", "Enable (not recoverable)" });
		this.comboBoxProtectUnderVoltageEnable.Location = new System.Drawing.Point(343, 106);
		this.comboBoxProtectUnderVoltageEnable.Margin = new System.Windows.Forms.Padding(6);
		this.comboBoxProtectUnderVoltageEnable.Name = "comboBoxProtectUnderVoltageEnable";
		this.comboBoxProtectUnderVoltageEnable.Size = new System.Drawing.Size(233, 26);
		this.comboBoxProtectUnderVoltageEnable.TabIndex = 13;
		this.comboBoxProtectUnderVoltageEnable.Tag = "";
		this.comboBoxProtectMotorTempEnable.BackColor = System.Drawing.SystemColors.Window;
		this.comboBoxProtectMotorTempEnable.FormattingEnabled = true;
		this.comboBoxProtectMotorTempEnable.Items.AddRange(new object[3] { "Disable", "Enable (recoverable)", "Enable (not recoverable)" });
		this.comboBoxProtectMotorTempEnable.Location = new System.Drawing.Point(343, 28);
		this.comboBoxProtectMotorTempEnable.Margin = new System.Windows.Forms.Padding(6);
		this.comboBoxProtectMotorTempEnable.Name = "comboBoxProtectMotorTempEnable";
		this.comboBoxProtectMotorTempEnable.Size = new System.Drawing.Size(233, 26);
		this.comboBoxProtectMotorTempEnable.TabIndex = 9;
		this.comboBoxProtectMotorTempEnable.Tag = "";
		this.numericUpDownProtectOverVoltage.DecimalPlaces = 2;
		this.numericUpDownProtectOverVoltage.Increment = new decimal(new int[4] { 1, 0, 0, 65536 });
		this.numericUpDownProtectOverVoltage.Location = new System.Drawing.Point(229, 145);
		this.numericUpDownProtectOverVoltage.Margin = new System.Windows.Forms.Padding(2);
		this.numericUpDownProtectOverVoltage.Maximum = new decimal(new int[4] { 70, 0, 0, 0 });
		this.numericUpDownProtectOverVoltage.Minimum = new decimal(new int[4] { 2, 0, 0, 0 });
		this.numericUpDownProtectOverVoltage.Name = "numericUpDownProtectOverVoltage";
		this.numericUpDownProtectOverVoltage.Size = new System.Drawing.Size(105, 26);
		this.numericUpDownProtectOverVoltage.TabIndex = 14;
		this.numericUpDownProtectOverVoltage.Value = new decimal(new int[4] { 34, 0, 0, 0 });
		this.label51.AutoSize = true;
		this.label51.Location = new System.Drawing.Point(7, 149);
		this.label51.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label51.Name = "label51";
		this.label51.Size = new System.Drawing.Size(152, 18);
		this.label51.TabIndex = 56;
		this.label51.Text = "Protect Over Voltage";
		this.label39.AutoSize = true;
		this.label39.Font = new System.Drawing.Font("Arial", 12f);
		this.label39.Location = new System.Drawing.Point(7, 32);
		this.label39.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label39.Name = "label39";
		this.label39.Size = new System.Drawing.Size(193, 18);
		this.label39.TabIndex = 49;
		this.label39.Text = "Protect Motor Temperature";
		this.numericUpDownProtectMotorTemp.Location = new System.Drawing.Point(229, 28);
		this.numericUpDownProtectMotorTemp.Margin = new System.Windows.Forms.Padding(2);
		this.numericUpDownProtectMotorTemp.Maximum = new decimal(new int[4] { 180, 0, 0, 0 });
		this.numericUpDownProtectMotorTemp.Minimum = new decimal(new int[4] { 20, 0, 0, 0 });
		this.numericUpDownProtectMotorTemp.Name = "numericUpDownProtectMotorTemp";
		this.numericUpDownProtectMotorTemp.Size = new System.Drawing.Size(105, 26);
		this.numericUpDownProtectMotorTemp.TabIndex = 8;
		this.numericUpDownProtectMotorTemp.Value = new decimal(new int[4] { 100, 0, 0, 0 });
		this.label40.AutoSize = true;
		this.label40.Location = new System.Drawing.Point(7, 110);
		this.label40.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label40.Name = "label40";
		this.label40.Size = new System.Drawing.Size(161, 18);
		this.label40.TabIndex = 51;
		this.label40.Text = "Protect Under Voltage";
		this.numericUpDownProtectUnderVoltage.DecimalPlaces = 2;
		this.numericUpDownProtectUnderVoltage.Increment = new decimal(new int[4] { 1, 0, 0, 65536 });
		this.numericUpDownProtectUnderVoltage.Location = new System.Drawing.Point(229, 106);
		this.numericUpDownProtectUnderVoltage.Margin = new System.Windows.Forms.Padding(2);
		this.numericUpDownProtectUnderVoltage.Maximum = new decimal(new int[4] { 70, 0, 0, 0 });
		this.numericUpDownProtectUnderVoltage.Minimum = new decimal(new int[4] { 2, 0, 0, 0 });
		this.numericUpDownProtectUnderVoltage.Name = "numericUpDownProtectUnderVoltage";
		this.numericUpDownProtectUnderVoltage.Size = new System.Drawing.Size(105, 26);
		this.numericUpDownProtectUnderVoltage.TabIndex = 12;
		this.numericUpDownProtectUnderVoltage.Value = new decimal(new int[4] { 8, 0, 0, 0 });
		this.numericUpDownProtectLostInputTime.Location = new System.Drawing.Point(229, 340);
		this.numericUpDownProtectLostInputTime.Margin = new System.Windows.Forms.Padding(2);
		this.numericUpDownProtectLostInputTime.Maximum = new decimal(new int[4] { 65535, 0, 0, 0 });
		this.numericUpDownProtectLostInputTime.Name = "numericUpDownProtectLostInputTime";
		this.numericUpDownProtectLostInputTime.Size = new System.Drawing.Size(105, 26);
		this.numericUpDownProtectLostInputTime.TabIndex = 22;
		this.label14.AutoSize = true;
		this.label14.Location = new System.Drawing.Point(7, 344);
		this.label14.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label14.Name = "label14";
		this.label14.Size = new System.Drawing.Size(166, 18);
		this.label14.TabIndex = 4;
		this.label14.Text = "Protect Lost Input Time";
		this.panel10.BackColor = System.Drawing.Color.WhiteSmoke;
		this.panel10.Dock = System.Windows.Forms.DockStyle.Left;
		this.panel10.Location = new System.Drawing.Point(340, 2);
		this.panel10.Name = "panel10";
		this.panel10.Size = new System.Drawing.Size(5, 377);
		this.panel10.TabIndex = 74;
		this.panel2.Controls.Add(this.label44);
		this.panel2.Controls.Add(this.label20);
		this.panel2.Controls.Add(this.label15);
		this.panel2.Controls.Add(this.label50);
		this.panel2.Controls.Add(this.label13);
		this.panel2.Controls.Add(this.comboBoxSpinDirection);
		this.panel2.Controls.Add(this.comboBoxRs485Baudrate);
		this.panel2.Controls.Add(this.label49);
		this.panel2.Controls.Add(this.numericUpDownDriverID);
		this.panel2.Controls.Add(this.comboBoxBroadcastMode);
		this.panel2.Controls.Add(this.label45);
		this.panel2.Controls.Add(this.comboBoxBrakeResEnable);
		this.panel2.Controls.Add(this.comboBoxBusType);
		this.panel2.Controls.Add(this.comboBoxCanBaudrate);
		this.panel2.Controls.Add(this.label48);
		this.panel2.Controls.Add(this.label41);
		this.panel2.Controls.Add(this.numericUpDownBrakeResOnVoltage);
		this.panel2.Dock = System.Windows.Forms.DockStyle.Left;
		this.panel2.Location = new System.Drawing.Point(2, 2);
		this.panel2.Margin = new System.Windows.Forms.Padding(2);
		this.panel2.Name = "panel2";
		this.panel2.Size = new System.Drawing.Size(338, 377);
		this.panel2.TabIndex = 67;
		this.label44.AutoSize = true;
		this.label44.Location = new System.Drawing.Point(8, 296);
		this.label44.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label44.Name = "label44";
		this.label44.Size = new System.Drawing.Size(166, 18);
		this.label44.TabIndex = 68;
		this.label44.Text = "Brake Resistor Control";
		this.label20.AutoSize = true;
		this.label20.Font = new System.Drawing.Font("Arial", 15.75f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.label20.ForeColor = System.Drawing.SystemColors.ActiveCaption;
		this.label20.Location = new System.Drawing.Point(3, 3);
		this.label20.Name = "label20";
		this.label20.Size = new System.Drawing.Size(134, 24);
		this.label20.TabIndex = 67;
		this.label20.Text = "Basic Setting";
		this.label15.AutoSize = true;
		this.label15.Location = new System.Drawing.Point(8, 32);
		this.label15.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label15.Name = "label15";
		this.label15.Size = new System.Drawing.Size(69, 18);
		this.label15.TabIndex = 38;
		this.label15.Text = "Driver ID";
		this.label50.AutoSize = true;
		this.label50.Location = new System.Drawing.Point(8, 252);
		this.label50.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label50.Name = "label50";
		this.label50.Size = new System.Drawing.Size(107, 18);
		this.label50.TabIndex = 66;
		this.label50.Text = "Spin Direction";
		this.label13.AutoSize = true;
		this.label13.Location = new System.Drawing.Point(8, 120);
		this.label13.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label13.Name = "label13";
		this.label13.Size = new System.Drawing.Size(125, 18);
		this.label13.TabIndex = 3;
		this.label13.Text = "RS485 Baudrate";
		this.comboBoxSpinDirection.BackColor = System.Drawing.SystemColors.Window;
		this.comboBoxSpinDirection.FormattingEnabled = true;
		this.comboBoxSpinDirection.Items.AddRange(new object[2] { "Normal", "Reverse" });
		this.comboBoxSpinDirection.Location = new System.Drawing.Point(192, 248);
		this.comboBoxSpinDirection.Margin = new System.Windows.Forms.Padding(6);
		this.comboBoxSpinDirection.Name = "comboBoxSpinDirection";
		this.comboBoxSpinDirection.Size = new System.Drawing.Size(134, 26);
		this.comboBoxSpinDirection.TabIndex = 5;
		this.comboBoxRs485Baudrate.BackColor = System.Drawing.SystemColors.Window;
		this.comboBoxRs485Baudrate.FormattingEnabled = true;
		this.comboBoxRs485Baudrate.Items.AddRange(new object[10] { "9600", "19200", "38400", "57600", "115200", "230400", "460800", "1000000", "2000000", "4000000" });
		this.comboBoxRs485Baudrate.Location = new System.Drawing.Point(192, 116);
		this.comboBoxRs485Baudrate.Margin = new System.Windows.Forms.Padding(6);
		this.comboBoxRs485Baudrate.Name = "comboBoxRs485Baudrate";
		this.comboBoxRs485Baudrate.Size = new System.Drawing.Size(133, 26);
		this.comboBoxRs485Baudrate.TabIndex = 2;
		this.label49.AutoSize = true;
		this.label49.Location = new System.Drawing.Point(8, 208);
		this.label49.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label49.Name = "label49";
		this.label49.Size = new System.Drawing.Size(124, 18);
		this.label49.TabIndex = 64;
		this.label49.Text = "Broadcast Mode";
		this.numericUpDownDriverID.Location = new System.Drawing.Point(192, 28);
		this.numericUpDownDriverID.Margin = new System.Windows.Forms.Padding(2);
		this.numericUpDownDriverID.Maximum = new decimal(new int[4] { 32, 0, 0, 0 });
		this.numericUpDownDriverID.Name = "numericUpDownDriverID";
		this.numericUpDownDriverID.Size = new System.Drawing.Size(133, 26);
		this.numericUpDownDriverID.TabIndex = 0;
		this.comboBoxBroadcastMode.BackColor = System.Drawing.SystemColors.Window;
		this.comboBoxBroadcastMode.FormattingEnabled = true;
		this.comboBoxBroadcastMode.Items.AddRange(new object[2] { "OFF", "ON" });
		this.comboBoxBroadcastMode.Location = new System.Drawing.Point(192, 204);
		this.comboBoxBroadcastMode.Margin = new System.Windows.Forms.Padding(6);
		this.comboBoxBroadcastMode.Name = "comboBoxBroadcastMode";
		this.comboBoxBroadcastMode.Size = new System.Drawing.Size(133, 26);
		this.comboBoxBroadcastMode.TabIndex = 4;
		this.label45.AutoSize = true;
		this.label45.Location = new System.Drawing.Point(8, 164);
		this.label45.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label45.Name = "label45";
		this.label45.Size = new System.Drawing.Size(110, 18);
		this.label45.TabIndex = 60;
		this.label45.Text = "CAN Baudrate";
		this.comboBoxBrakeResEnable.BackColor = System.Drawing.SystemColors.Window;
		this.comboBoxBrakeResEnable.FormattingEnabled = true;
		this.comboBoxBrakeResEnable.Items.AddRange(new object[2] { "Disable", "Enable" });
		this.comboBoxBrakeResEnable.Location = new System.Drawing.Point(192, 292);
		this.comboBoxBrakeResEnable.Margin = new System.Windows.Forms.Padding(6);
		this.comboBoxBrakeResEnable.Name = "comboBoxBrakeResEnable";
		this.comboBoxBrakeResEnable.Size = new System.Drawing.Size(134, 26);
		this.comboBoxBrakeResEnable.TabIndex = 6;
		this.comboBoxBrakeResEnable.Tag = "";
		this.comboBoxBusType.BackColor = System.Drawing.SystemColors.Window;
		this.comboBoxBusType.Enabled = false;
		this.comboBoxBusType.FormattingEnabled = true;
		this.comboBoxBusType.Items.AddRange(new object[4] { "NONE", "RS485", "CAN", "EtherCAT" });
		this.comboBoxBusType.Location = new System.Drawing.Point(192, 72);
		this.comboBoxBusType.Margin = new System.Windows.Forms.Padding(6);
		this.comboBoxBusType.Name = "comboBoxBusType";
		this.comboBoxBusType.Size = new System.Drawing.Size(133, 26);
		this.comboBoxBusType.TabIndex = 1;
		this.comboBoxBusType.Tag = "";
		this.comboBoxCanBaudrate.BackColor = System.Drawing.SystemColors.Window;
		this.comboBoxCanBaudrate.FormattingEnabled = true;
		this.comboBoxCanBaudrate.Items.AddRange(new object[5] { "100000", "125000", "250000", "500000", "1000000" });
		this.comboBoxCanBaudrate.Location = new System.Drawing.Point(192, 160);
		this.comboBoxCanBaudrate.Margin = new System.Windows.Forms.Padding(6);
		this.comboBoxCanBaudrate.Name = "comboBoxCanBaudrate";
		this.comboBoxCanBaudrate.Size = new System.Drawing.Size(133, 26);
		this.comboBoxCanBaudrate.TabIndex = 3;
		this.comboBoxCanBaudrate.Tag = "";
		this.label48.AutoSize = true;
		this.label48.Location = new System.Drawing.Point(8, 76);
		this.label48.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label48.Name = "label48";
		this.label48.Size = new System.Drawing.Size(72, 18);
		this.label48.TabIndex = 62;
		this.label48.Text = "Bus Type";
		this.label41.AutoSize = true;
		this.label41.Location = new System.Drawing.Point(8, 340);
		this.label41.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label41.Name = "label41";
		this.label41.Size = new System.Drawing.Size(169, 18);
		this.label41.TabIndex = 59;
		this.label41.Text = "Brake Resistor Voltage";
		this.numericUpDownBrakeResOnVoltage.DecimalPlaces = 2;
		this.numericUpDownBrakeResOnVoltage.Increment = new decimal(new int[4] { 1, 0, 0, 65536 });
		this.numericUpDownBrakeResOnVoltage.Location = new System.Drawing.Point(192, 336);
		this.numericUpDownBrakeResOnVoltage.Margin = new System.Windows.Forms.Padding(2);
		this.numericUpDownBrakeResOnVoltage.Maximum = new decimal(new int[4] { 70, 0, 0, 0 });
		this.numericUpDownBrakeResOnVoltage.Name = "numericUpDownBrakeResOnVoltage";
		this.numericUpDownBrakeResOnVoltage.Size = new System.Drawing.Size(134, 26);
		this.numericUpDownBrakeResOnVoltage.TabIndex = 7;
		this.numericUpDownBrakeResOnVoltage.Value = new decimal(new int[4] { 26, 0, 0, 0 });
		this.panel4.BackColor = System.Drawing.Color.WhiteSmoke;
		this.panel4.Dock = System.Windows.Forms.DockStyle.Bottom;
		this.panel4.Location = new System.Drawing.Point(2, 379);
		this.panel4.Name = "panel4";
		this.panel4.Size = new System.Drawing.Size(932, 5);
		this.panel4.TabIndex = 73;
		this.panel7.Controls.Add(this.buttonResetSaveSetting);
		this.panel7.Controls.Add(this.panel22);
		this.panel7.Controls.Add(this.panel5);
		this.panel7.Controls.Add(this.panel11);
		this.panel7.Controls.Add(this.panel9);
		this.panel7.Controls.Add(this.buttonWriteSaveSetting);
		this.panel7.Controls.Add(this.buttonReadSaveSetting);
		this.panel7.Dock = System.Windows.Forms.DockStyle.Bottom;
		this.panel7.Location = new System.Drawing.Point(2, 384);
		this.panel7.Name = "panel7";
		this.panel7.Size = new System.Drawing.Size(932, 215);
		this.panel7.TabIndex = 71;
		this.buttonResetSaveSetting.BackColor = System.Drawing.SystemColors.Window;
		this.buttonResetSaveSetting.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonResetSaveSetting.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonResetSaveSetting.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonResetSaveSetting.Location = new System.Drawing.Point(797, 171);
		this.buttonResetSaveSetting.Margin = new System.Windows.Forms.Padding(6);
		this.buttonResetSaveSetting.Name = "buttonResetSaveSetting";
		this.buttonResetSaveSetting.Size = new System.Drawing.Size(128, 35);
		this.buttonResetSaveSetting.TabIndex = 78;
		this.buttonResetSaveSetting.Text = "Reset Setting";
		this.buttonResetSaveSetting.UseVisualStyleBackColor = false;
		this.buttonResetSaveSetting.Click += new System.EventHandler(buttonResetSaveSetting_Click);
		this.panel22.BackColor = System.Drawing.Color.WhiteSmoke;
		this.panel22.Dock = System.Windows.Forms.DockStyle.Left;
		this.panel22.Location = new System.Drawing.Point(787, 0);
		this.panel22.Name = "panel22";
		this.panel22.Size = new System.Drawing.Size(5, 215);
		this.panel22.TabIndex = 77;
		this.panel5.Controls.Add(this.label10);
		this.panel5.Controls.Add(this.label33);
		this.panel5.Controls.Add(this.label34);
		this.panel5.Controls.Add(this.numericUpDownSpeedKp);
		this.panel5.Controls.Add(this.buttonSetCurrentPidRam);
		this.panel5.Controls.Add(this.label16);
		this.panel5.Controls.Add(this.buttonSetSpeedPidRam);
		this.panel5.Controls.Add(this.numericUpDownAngleKp);
		this.panel5.Controls.Add(this.buttonSetAnglePidRam);
		this.panel5.Controls.Add(this.numericUpDownCurrentKp);
		this.panel5.Controls.Add(this.label37);
		this.panel5.Controls.Add(this.numericUpDownSpeedKi);
		this.panel5.Controls.Add(this.numericUpDownCurrentKi);
		this.panel5.Controls.Add(this.label17);
		this.panel5.Controls.Add(this.numericUpDownAngleKi);
		this.panel5.Dock = System.Windows.Forms.DockStyle.Left;
		this.panel5.Location = new System.Drawing.Point(439, 0);
		this.panel5.Name = "panel5";
		this.panel5.Size = new System.Drawing.Size(348, 215);
		this.panel5.TabIndex = 74;
		this.label10.AutoSize = true;
		this.label10.Font = new System.Drawing.Font("Arial", 15.75f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.label10.ForeColor = System.Drawing.SystemColors.ActiveCaption;
		this.label10.Location = new System.Drawing.Point(3, 3);
		this.label10.Name = "label10";
		this.label10.Size = new System.Drawing.Size(116, 24);
		this.label10.TabIndex = 58;
		this.label10.Text = "PID Setting";
		this.label33.AutoSize = true;
		this.label33.Location = new System.Drawing.Point(10, 144);
		this.label33.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label33.Name = "label33";
		this.label33.Size = new System.Drawing.Size(59, 18);
		this.label33.TabIndex = 53;
		this.label33.Text = "Current";
		this.label34.AutoSize = true;
		this.label34.Font = new System.Drawing.Font("Arial", 12f);
		this.label34.Location = new System.Drawing.Point(10, 68);
		this.label34.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label34.Name = "label34";
		this.label34.Size = new System.Drawing.Size(48, 18);
		this.label34.TabIndex = 51;
		this.label34.Text = "Angle";
		this.numericUpDownSpeedKp.Location = new System.Drawing.Point(89, 102);
		this.numericUpDownSpeedKp.Margin = new System.Windows.Forms.Padding(2);
		this.numericUpDownSpeedKp.Maximum = new decimal(new int[4] { 2000, 0, 0, 0 });
		this.numericUpDownSpeedKp.Name = "numericUpDownSpeedKp";
		this.numericUpDownSpeedKp.Size = new System.Drawing.Size(75, 26);
		this.numericUpDownSpeedKp.TabIndex = 31;
		this.buttonSetCurrentPidRam.BackColor = System.Drawing.SystemColors.Window;
		this.buttonSetCurrentPidRam.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonSetCurrentPidRam.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonSetCurrentPidRam.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonSetCurrentPidRam.Font = new System.Drawing.Font("Arial", 10.5f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.buttonSetCurrentPidRam.Location = new System.Drawing.Point(257, 139);
		this.buttonSetCurrentPidRam.Margin = new System.Windows.Forms.Padding(6);
		this.buttonSetCurrentPidRam.Name = "buttonSetCurrentPidRam";
		this.buttonSetCurrentPidRam.Size = new System.Drawing.Size(79, 29);
		this.buttonSetCurrentPidRam.TabIndex = 36;
		this.buttonSetCurrentPidRam.Text = "SET RAM";
		this.buttonSetCurrentPidRam.UseVisualStyleBackColor = false;
		this.buttonSetCurrentPidRam.Click += new System.EventHandler(buttonSetCurrentPidRam_Click);
		this.label16.AutoSize = true;
		this.label16.Font = new System.Drawing.Font("Arial", 12f);
		this.label16.Location = new System.Drawing.Point(109, 40);
		this.label16.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label16.Name = "label16";
		this.label16.Size = new System.Drawing.Size(28, 18);
		this.label16.TabIndex = 41;
		this.label16.Text = "Kp";
		this.buttonSetSpeedPidRam.BackColor = System.Drawing.SystemColors.Window;
		this.buttonSetSpeedPidRam.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonSetSpeedPidRam.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonSetSpeedPidRam.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonSetSpeedPidRam.Font = new System.Drawing.Font("Arial", 10.5f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.buttonSetSpeedPidRam.Location = new System.Drawing.Point(257, 101);
		this.buttonSetSpeedPidRam.Margin = new System.Windows.Forms.Padding(6);
		this.buttonSetSpeedPidRam.Name = "buttonSetSpeedPidRam";
		this.buttonSetSpeedPidRam.Size = new System.Drawing.Size(79, 29);
		this.buttonSetSpeedPidRam.TabIndex = 33;
		this.buttonSetSpeedPidRam.Text = "SET RAM";
		this.buttonSetSpeedPidRam.UseVisualStyleBackColor = false;
		this.buttonSetSpeedPidRam.Click += new System.EventHandler(buttonSetSpeedPidRam_Click);
		this.numericUpDownAngleKp.Location = new System.Drawing.Point(89, 64);
		this.numericUpDownAngleKp.Margin = new System.Windows.Forms.Padding(2);
		this.numericUpDownAngleKp.Maximum = new decimal(new int[4] { 2000, 0, 0, 0 });
		this.numericUpDownAngleKp.Name = "numericUpDownAngleKp";
		this.numericUpDownAngleKp.Size = new System.Drawing.Size(75, 26);
		this.numericUpDownAngleKp.TabIndex = 28;
		this.buttonSetAnglePidRam.BackColor = System.Drawing.SystemColors.Window;
		this.buttonSetAnglePidRam.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonSetAnglePidRam.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonSetAnglePidRam.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonSetAnglePidRam.Font = new System.Drawing.Font("Arial", 10.5f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.buttonSetAnglePidRam.Location = new System.Drawing.Point(257, 63);
		this.buttonSetAnglePidRam.Margin = new System.Windows.Forms.Padding(6);
		this.buttonSetAnglePidRam.Name = "buttonSetAnglePidRam";
		this.buttonSetAnglePidRam.Size = new System.Drawing.Size(79, 29);
		this.buttonSetAnglePidRam.TabIndex = 30;
		this.buttonSetAnglePidRam.Text = "SET RAM";
		this.buttonSetAnglePidRam.UseVisualStyleBackColor = false;
		this.buttonSetAnglePidRam.Click += new System.EventHandler(buttonSetAnglePidRam_Click);
		this.numericUpDownCurrentKp.Location = new System.Drawing.Point(89, 140);
		this.numericUpDownCurrentKp.Margin = new System.Windows.Forms.Padding(2);
		this.numericUpDownCurrentKp.Maximum = new decimal(new int[4] { 255, 0, 0, 0 });
		this.numericUpDownCurrentKp.Name = "numericUpDownCurrentKp";
		this.numericUpDownCurrentKp.Size = new System.Drawing.Size(75, 26);
		this.numericUpDownCurrentKp.TabIndex = 34;
		this.label37.AutoSize = true;
		this.label37.Font = new System.Drawing.Font("Arial", 12f);
		this.label37.Location = new System.Drawing.Point(10, 106);
		this.label37.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label37.Name = "label37";
		this.label37.Size = new System.Drawing.Size(55, 18);
		this.label37.TabIndex = 52;
		this.label37.Text = "Speed";
		this.numericUpDownSpeedKi.Location = new System.Drawing.Point(174, 102);
		this.numericUpDownSpeedKi.Margin = new System.Windows.Forms.Padding(2);
		this.numericUpDownSpeedKi.Maximum = new decimal(new int[4] { 2000, 0, 0, 0 });
		this.numericUpDownSpeedKi.Name = "numericUpDownSpeedKi";
		this.numericUpDownSpeedKi.Size = new System.Drawing.Size(75, 26);
		this.numericUpDownSpeedKi.TabIndex = 32;
		this.numericUpDownCurrentKi.Location = new System.Drawing.Point(174, 140);
		this.numericUpDownCurrentKi.Margin = new System.Windows.Forms.Padding(2);
		this.numericUpDownCurrentKi.Maximum = new decimal(new int[4] { 255, 0, 0, 0 });
		this.numericUpDownCurrentKi.Name = "numericUpDownCurrentKi";
		this.numericUpDownCurrentKi.Size = new System.Drawing.Size(75, 26);
		this.numericUpDownCurrentKi.TabIndex = 35;
		this.label17.AutoSize = true;
		this.label17.Font = new System.Drawing.Font("Arial", 12f);
		this.label17.Location = new System.Drawing.Point(198, 40);
		this.label17.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label17.Name = "label17";
		this.label17.Size = new System.Drawing.Size(23, 18);
		this.label17.TabIndex = 43;
		this.label17.Text = "Ki";
		this.numericUpDownAngleKi.Location = new System.Drawing.Point(174, 64);
		this.numericUpDownAngleKi.Margin = new System.Windows.Forms.Padding(2);
		this.numericUpDownAngleKi.Maximum = new decimal(new int[4] { 2000, 0, 0, 0 });
		this.numericUpDownAngleKi.Name = "numericUpDownAngleKi";
		this.numericUpDownAngleKi.Size = new System.Drawing.Size(75, 26);
		this.numericUpDownAngleKi.TabIndex = 29;
		this.panel11.BackColor = System.Drawing.Color.WhiteSmoke;
		this.panel11.Dock = System.Windows.Forms.DockStyle.Left;
		this.panel11.Location = new System.Drawing.Point(434, 0);
		this.panel11.Name = "panel11";
		this.panel11.Size = new System.Drawing.Size(5, 215);
		this.panel11.TabIndex = 76;
		this.panel9.Controls.Add(this.buttonSetSpeedRampRam);
		this.panel9.Controls.Add(this.buttonSetMaxSpeedRam);
		this.panel9.Controls.Add(this.buttonSetMaxTorqueCurrentRam);
		this.panel9.Controls.Add(this.numericUpDownCurrentRamp);
		this.panel9.Controls.Add(this.labelCurrentRamp);
		this.panel9.Controls.Add(this.label11);
		this.panel9.Controls.Add(this.numericUpDownMaxTorque);
		this.panel9.Controls.Add(this.numericUpDownMaxSpeed);
		this.panel9.Controls.Add(this.labelMaxTorqueCurrent);
		this.panel9.Controls.Add(this.labelMaxSpeed);
		this.panel9.Controls.Add(this.numericUpDownMaxAngle);
		this.panel9.Controls.Add(this.labelSpeedRamp);
		this.panel9.Controls.Add(this.labelMaxAngle);
		this.panel9.Controls.Add(this.numericUpDownSpeedRamp);
		this.panel9.Dock = System.Windows.Forms.DockStyle.Left;
		this.panel9.Location = new System.Drawing.Point(0, 0);
		this.panel9.Name = "panel9";
		this.panel9.Size = new System.Drawing.Size(434, 215);
		this.panel9.TabIndex = 75;
		this.buttonSetSpeedRampRam.BackColor = System.Drawing.SystemColors.Window;
		this.buttonSetSpeedRampRam.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonSetSpeedRampRam.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonSetSpeedRampRam.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonSetSpeedRampRam.Font = new System.Drawing.Font("Arial", 10.5f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.buttonSetSpeedRampRam.Location = new System.Drawing.Point(346, 139);
		this.buttonSetSpeedRampRam.Margin = new System.Windows.Forms.Padding(6);
		this.buttonSetSpeedRampRam.Name = "buttonSetSpeedRampRam";
		this.buttonSetSpeedRampRam.Size = new System.Drawing.Size(79, 29);
		this.buttonSetSpeedRampRam.TabIndex = 64;
		this.buttonSetSpeedRampRam.Text = "SET RAM";
		this.buttonSetSpeedRampRam.UseVisualStyleBackColor = false;
		this.buttonSetSpeedRampRam.Click += new System.EventHandler(buttonSetSpeedRampRam_Click);
		this.buttonSetMaxSpeedRam.BackColor = System.Drawing.SystemColors.Window;
		this.buttonSetMaxSpeedRam.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonSetMaxSpeedRam.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonSetMaxSpeedRam.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonSetMaxSpeedRam.Font = new System.Drawing.Font("Arial", 10.5f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.buttonSetMaxSpeedRam.Location = new System.Drawing.Point(346, 63);
		this.buttonSetMaxSpeedRam.Margin = new System.Windows.Forms.Padding(6);
		this.buttonSetMaxSpeedRam.Name = "buttonSetMaxSpeedRam";
		this.buttonSetMaxSpeedRam.Size = new System.Drawing.Size(79, 29);
		this.buttonSetMaxSpeedRam.TabIndex = 62;
		this.buttonSetMaxSpeedRam.Text = "SET RAM";
		this.buttonSetMaxSpeedRam.UseVisualStyleBackColor = false;
		this.buttonSetMaxSpeedRam.Click += new System.EventHandler(buttonSetMaxSpeedRam_Click);
		this.buttonSetMaxTorqueCurrentRam.BackColor = System.Drawing.SystemColors.Window;
		this.buttonSetMaxTorqueCurrentRam.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonSetMaxTorqueCurrentRam.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonSetMaxTorqueCurrentRam.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonSetMaxTorqueCurrentRam.Font = new System.Drawing.Font("Arial", 10.5f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.buttonSetMaxTorqueCurrentRam.Location = new System.Drawing.Point(346, 25);
		this.buttonSetMaxTorqueCurrentRam.Margin = new System.Windows.Forms.Padding(6);
		this.buttonSetMaxTorqueCurrentRam.Name = "buttonSetMaxTorqueCurrentRam";
		this.buttonSetMaxTorqueCurrentRam.Size = new System.Drawing.Size(79, 29);
		this.buttonSetMaxTorqueCurrentRam.TabIndex = 59;
		this.buttonSetMaxTorqueCurrentRam.Text = "SET RAM";
		this.buttonSetMaxTorqueCurrentRam.UseVisualStyleBackColor = false;
		this.buttonSetMaxTorqueCurrentRam.Click += new System.EventHandler(buttonSetMaxTorqueCurrentRam_Click);
		this.numericUpDownCurrentRamp.Location = new System.Drawing.Point(188, 178);
		this.numericUpDownCurrentRamp.Margin = new System.Windows.Forms.Padding(2);
		this.numericUpDownCurrentRamp.Name = "numericUpDownCurrentRamp";
		this.numericUpDownCurrentRamp.Size = new System.Drawing.Size(150, 26);
		this.numericUpDownCurrentRamp.TabIndex = 60;
		this.labelCurrentRamp.AutoSize = true;
		this.labelCurrentRamp.Location = new System.Drawing.Point(8, 182);
		this.labelCurrentRamp.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.labelCurrentRamp.Name = "labelCurrentRamp";
		this.labelCurrentRamp.Size = new System.Drawing.Size(105, 18);
		this.labelCurrentRamp.TabIndex = 61;
		this.labelCurrentRamp.Text = "Current Ramp";
		this.label11.AutoSize = true;
		this.label11.Font = new System.Drawing.Font("Arial", 15.75f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.label11.ForeColor = System.Drawing.SystemColors.ActiveCaption;
		this.label11.Location = new System.Drawing.Point(3, 3);
		this.label11.Name = "label11";
		this.label11.Size = new System.Drawing.Size(136, 24);
		this.label11.TabIndex = 59;
		this.label11.Text = "Limits Setting";
		this.numericUpDownMaxTorque.Location = new System.Drawing.Point(188, 26);
		this.numericUpDownMaxTorque.Margin = new System.Windows.Forms.Padding(2);
		this.numericUpDownMaxTorque.Maximum = new decimal(new int[4] { 2000, 0, 0, 0 });
		this.numericUpDownMaxTorque.Name = "numericUpDownMaxTorque";
		this.numericUpDownMaxTorque.Size = new System.Drawing.Size(150, 26);
		this.numericUpDownMaxTorque.TabIndex = 27;
		this.numericUpDownMaxTorque.Value = new decimal(new int[4] { 1000, 0, 0, 0 });
		this.numericUpDownMaxSpeed.DecimalPlaces = 2;
		this.numericUpDownMaxSpeed.Location = new System.Drawing.Point(188, 64);
		this.numericUpDownMaxSpeed.Margin = new System.Windows.Forms.Padding(2);
		this.numericUpDownMaxSpeed.Maximum = new decimal(new int[4] { 72000, 0, 0, 0 });
		this.numericUpDownMaxSpeed.Name = "numericUpDownMaxSpeed";
		this.numericUpDownMaxSpeed.Size = new System.Drawing.Size(150, 26);
		this.numericUpDownMaxSpeed.TabIndex = 25;
		this.labelMaxTorqueCurrent.AutoSize = true;
		this.labelMaxTorqueCurrent.Location = new System.Drawing.Point(8, 30);
		this.labelMaxTorqueCurrent.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.labelMaxTorqueCurrent.Name = "labelMaxTorqueCurrent";
		this.labelMaxTorqueCurrent.Size = new System.Drawing.Size(143, 18);
		this.labelMaxTorqueCurrent.TabIndex = 41;
		this.labelMaxTorqueCurrent.Text = "Max Torque Current";
		this.labelMaxSpeed.AutoSize = true;
		this.labelMaxSpeed.Font = new System.Drawing.Font("Arial", 12f);
		this.labelMaxSpeed.Location = new System.Drawing.Point(8, 68);
		this.labelMaxSpeed.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.labelMaxSpeed.Name = "labelMaxSpeed";
		this.labelMaxSpeed.Size = new System.Drawing.Size(88, 18);
		this.labelMaxSpeed.TabIndex = 45;
		this.labelMaxSpeed.Text = "Max Speed";
		this.numericUpDownMaxAngle.DecimalPlaces = 2;
		this.numericUpDownMaxAngle.Location = new System.Drawing.Point(188, 102);
		this.numericUpDownMaxAngle.Margin = new System.Windows.Forms.Padding(2);
		this.numericUpDownMaxAngle.Maximum = new decimal(new int[4] { 360000000, 0, 0, 0 });
		this.numericUpDownMaxAngle.Name = "numericUpDownMaxAngle";
		this.numericUpDownMaxAngle.Size = new System.Drawing.Size(150, 26);
		this.numericUpDownMaxAngle.TabIndex = 24;
		this.labelSpeedRamp.AutoSize = true;
		this.labelSpeedRamp.Font = new System.Drawing.Font("Arial", 12f);
		this.labelSpeedRamp.Location = new System.Drawing.Point(8, 144);
		this.labelSpeedRamp.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.labelSpeedRamp.Name = "labelSpeedRamp";
		this.labelSpeedRamp.Size = new System.Drawing.Size(101, 18);
		this.labelSpeedRamp.TabIndex = 45;
		this.labelSpeedRamp.Text = "Speed Ramp";
		this.labelMaxAngle.AutoSize = true;
		this.labelMaxAngle.Font = new System.Drawing.Font("Arial", 12f);
		this.labelMaxAngle.Location = new System.Drawing.Point(8, 106);
		this.labelMaxAngle.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.labelMaxAngle.Name = "labelMaxAngle";
		this.labelMaxAngle.Size = new System.Drawing.Size(80, 18);
		this.labelMaxAngle.TabIndex = 45;
		this.labelMaxAngle.Text = "Max Angle";
		this.numericUpDownSpeedRamp.Location = new System.Drawing.Point(188, 140);
		this.numericUpDownSpeedRamp.Margin = new System.Windows.Forms.Padding(2);
		this.numericUpDownSpeedRamp.Maximum = new decimal(new int[4] { 360000, 0, 0, 0 });
		this.numericUpDownSpeedRamp.Name = "numericUpDownSpeedRamp";
		this.numericUpDownSpeedRamp.Size = new System.Drawing.Size(150, 26);
		this.numericUpDownSpeedRamp.TabIndex = 26;
		this.buttonWriteSaveSetting.BackColor = System.Drawing.SystemColors.Window;
		this.buttonWriteSaveSetting.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonWriteSaveSetting.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonWriteSaveSetting.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonWriteSaveSetting.Location = new System.Drawing.Point(797, 63);
		this.buttonWriteSaveSetting.Margin = new System.Windows.Forms.Padding(6);
		this.buttonWriteSaveSetting.Name = "buttonWriteSaveSetting";
		this.buttonWriteSaveSetting.Size = new System.Drawing.Size(128, 35);
		this.buttonWriteSaveSetting.TabIndex = 37;
		this.buttonWriteSaveSetting.Text = "Save Setting";
		this.buttonWriteSaveSetting.UseVisualStyleBackColor = false;
		this.buttonWriteSaveSetting.Click += new System.EventHandler(buttonWriteSaveSetting_Click);
		this.buttonReadSaveSetting.BackColor = System.Drawing.SystemColors.Window;
		this.buttonReadSaveSetting.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonReadSaveSetting.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonReadSaveSetting.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonReadSaveSetting.Location = new System.Drawing.Point(797, 9);
		this.buttonReadSaveSetting.Margin = new System.Windows.Forms.Padding(6);
		this.buttonReadSaveSetting.Name = "buttonReadSaveSetting";
		this.buttonReadSaveSetting.Size = new System.Drawing.Size(128, 35);
		this.buttonReadSaveSetting.TabIndex = 38;
		this.buttonReadSaveSetting.Text = "Read Setting";
		this.buttonReadSaveSetting.UseVisualStyleBackColor = false;
		this.buttonReadSaveSetting.Click += new System.EventHandler(buttonReadSaveSetting_Click);
		this.buttonRebootDevice.BackColor = System.Drawing.Color.Yellow;
		this.buttonRebootDevice.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonRebootDevice.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonRebootDevice.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonRebootDevice.Font = new System.Drawing.Font("Arial", 12f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.buttonRebootDevice.Location = new System.Drawing.Point(445, 3);
		this.buttonRebootDevice.Margin = new System.Windows.Forms.Padding(6);
		this.buttonRebootDevice.Name = "buttonRebootDevice";
		this.buttonRebootDevice.Size = new System.Drawing.Size(130, 29);
		this.buttonRebootDevice.TabIndex = 79;
		this.buttonRebootDevice.Text = "Reboot Device";
		this.buttonRebootDevice.UseVisualStyleBackColor = false;
		this.buttonRebootDevice.Click += new System.EventHandler(buttonRebootDevice_Click);
		this.TabControl.Controls.Add(this.tabPage3);
		this.TabControl.Controls.Add(this.tabPage2);
		this.TabControl.Controls.Add(this.tabPage1);
		this.TabControl.Controls.Add(this.tabPage4);
		this.TabControl.Controls.Add(this.tabPage5);
		this.TabControl.Dock = System.Windows.Forms.DockStyle.Fill;
		this.TabControl.Font = new System.Drawing.Font("Arial", 14.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.TabControl.ImeMode = System.Windows.Forms.ImeMode.NoControl;
		this.TabControl.Location = new System.Drawing.Point(0, 50);
		this.TabControl.Margin = new System.Windows.Forms.Padding(2);
		this.TabControl.Multiline = true;
		this.TabControl.Name = "TabControl";
		this.TabControl.SelectedIndex = 0;
		this.TabControl.Size = new System.Drawing.Size(944, 636);
		this.TabControl.SizeMode = System.Windows.Forms.TabSizeMode.Fixed;
		this.TabControl.TabIndex = 999;
		this.tabPage4.Controls.Add(this.richTextBoxLog);
		this.tabPage4.Controls.Add(this.panel17);
		this.tabPage4.Controls.Add(this.panel20);
		this.tabPage4.Controls.Add(this.panel1);
		this.tabPage4.Location = new System.Drawing.Point(4, 31);
		this.tabPage4.Margin = new System.Windows.Forms.Padding(2);
		this.tabPage4.Name = "tabPage4";
		this.tabPage4.Padding = new System.Windows.Forms.Padding(2);
		this.tabPage4.Size = new System.Drawing.Size(936, 601);
		this.tabPage4.TabIndex = 5;
		this.tabPage4.Text = "Test";
		this.tabPage4.UseVisualStyleBackColor = true;
		this.richTextBoxLog.BackColor = System.Drawing.SystemColors.Control;
		this.richTextBoxLog.BorderStyle = System.Windows.Forms.BorderStyle.None;
		this.richTextBoxLog.Dock = System.Windows.Forms.DockStyle.Fill;
		this.richTextBoxLog.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.richTextBoxLog.Location = new System.Drawing.Point(312, 2);
		this.richTextBoxLog.Margin = new System.Windows.Forms.Padding(2);
		this.richTextBoxLog.Name = "richTextBoxLog";
		this.richTextBoxLog.Size = new System.Drawing.Size(622, 482);
		this.richTextBoxLog.TabIndex = 10;
		this.richTextBoxLog.Text = "";
		this.panel17.Controls.Add(this.buttonClearLog);
		this.panel17.Dock = System.Windows.Forms.DockStyle.Bottom;
		this.panel17.Location = new System.Drawing.Point(312, 484);
		this.panel17.Name = "panel17";
		this.panel17.Size = new System.Drawing.Size(622, 35);
		this.panel17.TabIndex = 32;
		this.buttonClearLog.BackColor = System.Drawing.SystemColors.Window;
		this.buttonClearLog.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonClearLog.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonClearLog.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonClearLog.Font = new System.Drawing.Font("Arial", 12f);
		this.buttonClearLog.Location = new System.Drawing.Point(480, 3);
		this.buttonClearLog.Margin = new System.Windows.Forms.Padding(6);
		this.buttonClearLog.Name = "buttonClearLog";
		this.buttonClearLog.Size = new System.Drawing.Size(130, 29);
		this.buttonClearLog.TabIndex = 11;
		this.buttonClearLog.Text = "Clear Text";
		this.buttonClearLog.UseVisualStyleBackColor = false;
		this.buttonClearLog.Click += new System.EventHandler(buttonClearLog_Click);
		this.panel20.Controls.Add(this.buttonReadMultiAngle);
		this.panel20.Controls.Add(this.buttonSetMotorZeroRam);
		this.panel20.Controls.Add(this.buttonReadSingleAngle);
		this.panel20.Controls.Add(this.buttonClearMotorLoops);
		this.panel20.Controls.Add(this.textBoxMultiAngle);
		this.panel20.Controls.Add(this.textBoxSingleAngle);
		this.panel20.Dock = System.Windows.Forms.DockStyle.Bottom;
		this.panel20.Location = new System.Drawing.Point(312, 519);
		this.panel20.Name = "panel20";
		this.panel20.Size = new System.Drawing.Size(622, 80);
		this.panel20.TabIndex = 35;
		this.buttonReadMultiAngle.BackColor = System.Drawing.SystemColors.Window;
		this.buttonReadMultiAngle.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonReadMultiAngle.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonReadMultiAngle.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonReadMultiAngle.Font = new System.Drawing.Font("Arial", 12f);
		this.buttonReadMultiAngle.Location = new System.Drawing.Point(4, 6);
		this.buttonReadMultiAngle.Margin = new System.Windows.Forms.Padding(6);
		this.buttonReadMultiAngle.Name = "buttonReadMultiAngle";
		this.buttonReadMultiAngle.Size = new System.Drawing.Size(200, 29);
		this.buttonReadMultiAngle.TabIndex = 7;
		this.buttonReadMultiAngle.Text = "Read Multi Loop Angle";
		this.buttonReadMultiAngle.UseVisualStyleBackColor = false;
		this.buttonReadMultiAngle.Click += new System.EventHandler(buttonReadMultiAngle_Click);
		this.buttonSetMotorZeroRam.BackColor = System.Drawing.SystemColors.Window;
		this.buttonSetMotorZeroRam.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonSetMotorZeroRam.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonSetMotorZeroRam.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonSetMotorZeroRam.Font = new System.Drawing.Font("Arial", 12f);
		this.buttonSetMotorZeroRam.Location = new System.Drawing.Point(410, 41);
		this.buttonSetMotorZeroRam.Margin = new System.Windows.Forms.Padding(6);
		this.buttonSetMotorZeroRam.Name = "buttonSetMotorZeroRam";
		this.buttonSetMotorZeroRam.Size = new System.Drawing.Size(200, 29);
		this.buttonSetMotorZeroRam.TabIndex = 34;
		this.buttonSetMotorZeroRam.Text = "Set Motor Zero (RAM)";
		this.buttonSetMotorZeroRam.UseVisualStyleBackColor = false;
		this.buttonSetMotorZeroRam.Click += new System.EventHandler(buttonSetMotorZeroRam_Click);
		this.buttonReadSingleAngle.BackColor = System.Drawing.SystemColors.Window;
		this.buttonReadSingleAngle.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonReadSingleAngle.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonReadSingleAngle.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonReadSingleAngle.Font = new System.Drawing.Font("Arial", 12f);
		this.buttonReadSingleAngle.Location = new System.Drawing.Point(4, 41);
		this.buttonReadSingleAngle.Margin = new System.Windows.Forms.Padding(6);
		this.buttonReadSingleAngle.Name = "buttonReadSingleAngle";
		this.buttonReadSingleAngle.Size = new System.Drawing.Size(200, 29);
		this.buttonReadSingleAngle.TabIndex = 9;
		this.buttonReadSingleAngle.Text = "Read Single Loop Angle";
		this.buttonReadSingleAngle.UseVisualStyleBackColor = false;
		this.buttonReadSingleAngle.Click += new System.EventHandler(buttonReadSingleAngle_Click);
		this.buttonClearMotorLoops.BackColor = System.Drawing.SystemColors.Window;
		this.buttonClearMotorLoops.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonClearMotorLoops.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonClearMotorLoops.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonClearMotorLoops.Font = new System.Drawing.Font("Arial", 12f);
		this.buttonClearMotorLoops.Location = new System.Drawing.Point(410, 6);
		this.buttonClearMotorLoops.Margin = new System.Windows.Forms.Padding(6);
		this.buttonClearMotorLoops.Name = "buttonClearMotorLoops";
		this.buttonClearMotorLoops.Size = new System.Drawing.Size(200, 29);
		this.buttonClearMotorLoops.TabIndex = 33;
		this.buttonClearMotorLoops.Text = "Clear Motor Loops";
		this.buttonClearMotorLoops.UseVisualStyleBackColor = false;
		this.buttonClearMotorLoops.Click += new System.EventHandler(buttonClearMotorLoops_Click);
		this.textBoxMultiAngle.Font = new System.Drawing.Font("Arial", 12f);
		this.textBoxMultiAngle.Location = new System.Drawing.Point(222, 7);
		this.textBoxMultiAngle.Margin = new System.Windows.Forms.Padding(2);
		this.textBoxMultiAngle.Name = "textBoxMultiAngle";
		this.textBoxMultiAngle.ReadOnly = true;
		this.textBoxMultiAngle.Size = new System.Drawing.Size(170, 26);
		this.textBoxMultiAngle.TabIndex = 6;
		this.textBoxSingleAngle.Font = new System.Drawing.Font("Arial", 12f);
		this.textBoxSingleAngle.Location = new System.Drawing.Point(222, 42);
		this.textBoxSingleAngle.Margin = new System.Windows.Forms.Padding(2);
		this.textBoxSingleAngle.Name = "textBoxSingleAngle";
		this.textBoxSingleAngle.ReadOnly = true;
		this.textBoxSingleAngle.Size = new System.Drawing.Size(170, 26);
		this.textBoxSingleAngle.TabIndex = 8;
		this.panel1.Controls.Add(this.panel19);
		this.panel1.Controls.Add(this.panel21);
		this.panel1.Controls.Add(this.panel18);
		this.panel1.Dock = System.Windows.Forms.DockStyle.Left;
		this.panel1.Location = new System.Drawing.Point(2, 2);
		this.panel1.Margin = new System.Windows.Forms.Padding(2);
		this.panel1.Name = "panel1";
		this.panel1.Size = new System.Drawing.Size(310, 597);
		this.panel1.TabIndex = 30;
		this.panel19.Controls.Add(this.label18);
		this.panel19.Controls.Add(this.labelBusCurrent);
		this.panel19.Controls.Add(this.buttonBrakeRelease);
		this.panel19.Controls.Add(this.buttonBrake);
		this.panel19.Controls.Add(this.buttonClearError);
		this.panel19.Controls.Add(this.checkBoxLostInputProtection);
		this.panel19.Controls.Add(this.label32);
		this.panel19.Controls.Add(this.labelIc);
		this.panel19.Controls.Add(this.checkBoxUnderVoltageProtection);
		this.panel19.Controls.Add(this.checkBoxMotorStallProtection);
		this.panel19.Controls.Add(this.label26);
		this.panel19.Controls.Add(this.label66);
		this.panel19.Controls.Add(this.checkBoxShortCircuitProtection);
		this.panel19.Controls.Add(this.buttonReadState3);
		this.panel19.Controls.Add(this.checkBoxOverCurrentProtection);
		this.panel19.Controls.Add(this.checkBoxOverVoltageProtection);
		this.panel19.Controls.Add(this.checkBoxMotorTemperatureProtection);
		this.panel19.Controls.Add(this.checkBoxDriverTemperatureProtection);
		this.panel19.Controls.Add(this.buttonReadState2);
		this.panel19.Controls.Add(this.label25);
		this.panel19.Controls.Add(this.buttonReadState1);
		this.panel19.Controls.Add(this.labelIb);
		this.panel19.Controls.Add(this.label38);
		this.panel19.Controls.Add(this.label64);
		this.panel19.Controls.Add(this.label27);
		this.panel19.Controls.Add(this.labelIa);
		this.panel19.Controls.Add(this.labelBusVoltage);
		this.panel19.Controls.Add(this.label61);
		this.panel19.Controls.Add(this.labelMotorTemperature);
		this.panel19.Controls.Add(this.labelIq);
		this.panel19.Controls.Add(this.labelSpeed);
		this.panel19.Controls.Add(this.labelEncoder);
		this.panel19.Dock = System.Windows.Forms.DockStyle.Fill;
		this.panel19.Location = new System.Drawing.Point(0, 256);
		this.panel19.Name = "panel19";
		this.panel19.Size = new System.Drawing.Size(310, 341);
		this.panel19.TabIndex = 72;
		this.label18.AutoSize = true;
		this.label18.Font = new System.Drawing.Font("Arial", 11.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.label18.Location = new System.Drawing.Point(11, 36);
		this.label18.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label18.Name = "label18";
		this.label18.Size = new System.Drawing.Size(87, 17);
		this.label18.TabIndex = 74;
		this.label18.Text = "Bus Current";
		this.labelBusCurrent.AutoSize = true;
		this.labelBusCurrent.Font = new System.Drawing.Font("Arial", 11.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.labelBusCurrent.Location = new System.Drawing.Point(129, 36);
		this.labelBusCurrent.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.labelBusCurrent.Name = "labelBusCurrent";
		this.labelBusCurrent.Size = new System.Drawing.Size(28, 17);
		this.labelBusCurrent.TabIndex = 75;
		this.labelBusCurrent.Text = "0 A";
		this.buttonBrakeRelease.BackColor = System.Drawing.SystemColors.Window;
		this.buttonBrakeRelease.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonBrakeRelease.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonBrakeRelease.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonBrakeRelease.Font = new System.Drawing.Font("Arial", 12f);
		this.buttonBrakeRelease.Location = new System.Drawing.Point(159, 303);
		this.buttonBrakeRelease.Margin = new System.Windows.Forms.Padding(6);
		this.buttonBrakeRelease.Name = "buttonBrakeRelease";
		this.buttonBrakeRelease.Size = new System.Drawing.Size(130, 29);
		this.buttonBrakeRelease.TabIndex = 73;
		this.buttonBrakeRelease.Text = "Brake Release";
		this.buttonBrakeRelease.UseVisualStyleBackColor = false;
		this.buttonBrakeRelease.Click += new System.EventHandler(buttonBrakeRelease_Click);
		this.buttonBrake.BackColor = System.Drawing.SystemColors.Window;
		this.buttonBrake.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonBrake.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonBrake.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonBrake.Font = new System.Drawing.Font("Arial", 12f);
		this.buttonBrake.Location = new System.Drawing.Point(10, 303);
		this.buttonBrake.Margin = new System.Windows.Forms.Padding(6);
		this.buttonBrake.Name = "buttonBrake";
		this.buttonBrake.Size = new System.Drawing.Size(130, 29);
		this.buttonBrake.TabIndex = 72;
		this.buttonBrake.Text = "Brake";
		this.buttonBrake.UseVisualStyleBackColor = false;
		this.buttonBrake.Click += new System.EventHandler(buttonBrake_Click);
		this.buttonClearError.BackColor = System.Drawing.SystemColors.Window;
		this.buttonClearError.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonClearError.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonClearError.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonClearError.Font = new System.Drawing.Font("Arial", 12f);
		this.buttonClearError.Location = new System.Drawing.Point(159, 231);
		this.buttonClearError.Margin = new System.Windows.Forms.Padding(6);
		this.buttonClearError.Name = "buttonClearError";
		this.buttonClearError.Size = new System.Drawing.Size(130, 29);
		this.buttonClearError.TabIndex = 71;
		this.buttonClearError.Text = "Clear Error";
		this.buttonClearError.UseVisualStyleBackColor = false;
		this.buttonClearError.Click += new System.EventHandler(buttonClearError_Click);
		this.checkBoxLostInputProtection.AutoSize = true;
		this.checkBoxLostInputProtection.Enabled = false;
		this.checkBoxLostInputProtection.Font = new System.Drawing.Font("Arial", 11.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.checkBoxLostInputProtection.Location = new System.Drawing.Point(239, 199);
		this.checkBoxLostInputProtection.Name = "checkBoxLostInputProtection";
		this.checkBoxLostInputProtection.Size = new System.Drawing.Size(48, 21);
		this.checkBoxLostInputProtection.TabIndex = 19;
		this.checkBoxLostInputProtection.Text = "LIP";
		this.checkBoxLostInputProtection.UseVisualStyleBackColor = true;
		this.label32.AutoSize = true;
		this.label32.Font = new System.Drawing.Font("Arial", 11.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.label32.Location = new System.Drawing.Point(11, 12);
		this.label32.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label32.Name = "label32";
		this.label32.Size = new System.Drawing.Size(85, 17);
		this.label32.TabIndex = 53;
		this.label32.Text = "Bus Voltage";
		this.labelIc.AutoSize = true;
		this.labelIc.Font = new System.Drawing.Font("Arial", 11.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.labelIc.Location = new System.Drawing.Point(129, 204);
		this.labelIc.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.labelIc.Name = "labelIc";
		this.labelIc.Size = new System.Drawing.Size(16, 17);
		this.labelIc.TabIndex = 70;
		this.labelIc.Text = "0";
		this.checkBoxUnderVoltageProtection.AutoSize = true;
		this.checkBoxUnderVoltageProtection.Enabled = false;
		this.checkBoxUnderVoltageProtection.Font = new System.Drawing.Font("Arial", 11.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.checkBoxUnderVoltageProtection.Location = new System.Drawing.Point(239, 10);
		this.checkBoxUnderVoltageProtection.Name = "checkBoxUnderVoltageProtection";
		this.checkBoxUnderVoltageProtection.Size = new System.Drawing.Size(56, 21);
		this.checkBoxUnderVoltageProtection.TabIndex = 12;
		this.checkBoxUnderVoltageProtection.Text = "UVP";
		this.checkBoxUnderVoltageProtection.UseVisualStyleBackColor = true;
		this.checkBoxMotorStallProtection.AutoSize = true;
		this.checkBoxMotorStallProtection.Enabled = false;
		this.checkBoxMotorStallProtection.Font = new System.Drawing.Font("Arial", 11.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.checkBoxMotorStallProtection.Location = new System.Drawing.Point(239, 172);
		this.checkBoxMotorStallProtection.Name = "checkBoxMotorStallProtection";
		this.checkBoxMotorStallProtection.Size = new System.Drawing.Size(58, 21);
		this.checkBoxMotorStallProtection.TabIndex = 18;
		this.checkBoxMotorStallProtection.Text = "MSP";
		this.checkBoxMotorStallProtection.UseVisualStyleBackColor = true;
		this.label26.AutoSize = true;
		this.label26.Font = new System.Drawing.Font("Arial", 11.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.label26.Location = new System.Drawing.Point(11, 60);
		this.label26.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label26.Name = "label26";
		this.label26.Size = new System.Drawing.Size(84, 17);
		this.label26.TabIndex = 41;
		this.label26.Text = "Motor Temp";
		this.label66.AutoSize = true;
		this.label66.Font = new System.Drawing.Font("Arial", 11.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.label66.Location = new System.Drawing.Point(11, 204);
		this.label66.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label66.Name = "label66";
		this.label66.Size = new System.Drawing.Size(22, 17);
		this.label66.TabIndex = 69;
		this.label66.Text = "IC";
		this.checkBoxShortCircuitProtection.AutoSize = true;
		this.checkBoxShortCircuitProtection.Enabled = false;
		this.checkBoxShortCircuitProtection.Font = new System.Drawing.Font("Arial", 11.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.checkBoxShortCircuitProtection.Location = new System.Drawing.Point(239, 145);
		this.checkBoxShortCircuitProtection.Name = "checkBoxShortCircuitProtection";
		this.checkBoxShortCircuitProtection.Size = new System.Drawing.Size(58, 21);
		this.checkBoxShortCircuitProtection.TabIndex = 17;
		this.checkBoxShortCircuitProtection.Text = "SCP";
		this.checkBoxShortCircuitProtection.UseVisualStyleBackColor = true;
		this.buttonReadState3.BackColor = System.Drawing.SystemColors.Window;
		this.buttonReadState3.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonReadState3.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonReadState3.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonReadState3.Font = new System.Drawing.Font("Arial", 12f);
		this.buttonReadState3.Location = new System.Drawing.Point(159, 267);
		this.buttonReadState3.Margin = new System.Windows.Forms.Padding(6);
		this.buttonReadState3.Name = "buttonReadState3";
		this.buttonReadState3.Size = new System.Drawing.Size(130, 29);
		this.buttonReadState3.TabIndex = 9;
		this.buttonReadState3.Text = "Read State 3";
		this.buttonReadState3.UseVisualStyleBackColor = false;
		this.buttonReadState3.Click += new System.EventHandler(buttonReadState3_Click);
		this.checkBoxOverCurrentProtection.AutoSize = true;
		this.checkBoxOverCurrentProtection.Enabled = false;
		this.checkBoxOverCurrentProtection.Font = new System.Drawing.Font("Arial", 11.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.checkBoxOverCurrentProtection.Location = new System.Drawing.Point(239, 118);
		this.checkBoxOverCurrentProtection.Name = "checkBoxOverCurrentProtection";
		this.checkBoxOverCurrentProtection.Size = new System.Drawing.Size(60, 21);
		this.checkBoxOverCurrentProtection.TabIndex = 16;
		this.checkBoxOverCurrentProtection.Text = "OCP";
		this.checkBoxOverCurrentProtection.UseVisualStyleBackColor = true;
		this.checkBoxOverVoltageProtection.AutoSize = true;
		this.checkBoxOverVoltageProtection.Enabled = false;
		this.checkBoxOverVoltageProtection.Font = new System.Drawing.Font("Arial", 11.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.checkBoxOverVoltageProtection.Location = new System.Drawing.Point(239, 37);
		this.checkBoxOverVoltageProtection.Name = "checkBoxOverVoltageProtection";
		this.checkBoxOverVoltageProtection.Size = new System.Drawing.Size(58, 21);
		this.checkBoxOverVoltageProtection.TabIndex = 13;
		this.checkBoxOverVoltageProtection.Text = "OVP";
		this.checkBoxOverVoltageProtection.UseVisualStyleBackColor = true;
		this.checkBoxMotorTemperatureProtection.AutoSize = true;
		this.checkBoxMotorTemperatureProtection.Enabled = false;
		this.checkBoxMotorTemperatureProtection.Font = new System.Drawing.Font("Arial", 11.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.checkBoxMotorTemperatureProtection.Location = new System.Drawing.Point(239, 91);
		this.checkBoxMotorTemperatureProtection.Name = "checkBoxMotorTemperatureProtection";
		this.checkBoxMotorTemperatureProtection.Size = new System.Drawing.Size(57, 21);
		this.checkBoxMotorTemperatureProtection.TabIndex = 15;
		this.checkBoxMotorTemperatureProtection.Text = "MTP";
		this.checkBoxMotorTemperatureProtection.UseVisualStyleBackColor = true;
		this.checkBoxDriverTemperatureProtection.AutoSize = true;
		this.checkBoxDriverTemperatureProtection.Enabled = false;
		this.checkBoxDriverTemperatureProtection.Font = new System.Drawing.Font("Arial", 11.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.checkBoxDriverTemperatureProtection.Location = new System.Drawing.Point(239, 64);
		this.checkBoxDriverTemperatureProtection.Name = "checkBoxDriverTemperatureProtection";
		this.checkBoxDriverTemperatureProtection.Size = new System.Drawing.Size(57, 21);
		this.checkBoxDriverTemperatureProtection.TabIndex = 14;
		this.checkBoxDriverTemperatureProtection.Text = "DTP";
		this.checkBoxDriverTemperatureProtection.UseVisualStyleBackColor = true;
		this.buttonReadState2.BackColor = System.Drawing.SystemColors.Window;
		this.buttonReadState2.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonReadState2.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonReadState2.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonReadState2.Font = new System.Drawing.Font("Arial", 12f);
		this.buttonReadState2.Location = new System.Drawing.Point(10, 267);
		this.buttonReadState2.Margin = new System.Windows.Forms.Padding(6);
		this.buttonReadState2.Name = "buttonReadState2";
		this.buttonReadState2.Size = new System.Drawing.Size(130, 29);
		this.buttonReadState2.TabIndex = 8;
		this.buttonReadState2.Text = "Read State 2";
		this.buttonReadState2.UseVisualStyleBackColor = false;
		this.buttonReadState2.Click += new System.EventHandler(buttonReadState2_Click);
		this.label25.AutoSize = true;
		this.label25.Font = new System.Drawing.Font("Arial", 11.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.label25.Location = new System.Drawing.Point(11, 108);
		this.label25.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label25.Name = "label25";
		this.label25.Size = new System.Drawing.Size(50, 17);
		this.label25.TabIndex = 39;
		this.label25.Text = "Speed";
		this.buttonReadState1.BackColor = System.Drawing.SystemColors.Window;
		this.buttonReadState1.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonReadState1.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonReadState1.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonReadState1.Font = new System.Drawing.Font("Arial", 12f);
		this.buttonReadState1.Location = new System.Drawing.Point(10, 231);
		this.buttonReadState1.Margin = new System.Windows.Forms.Padding(6);
		this.buttonReadState1.Name = "buttonReadState1";
		this.buttonReadState1.Size = new System.Drawing.Size(130, 29);
		this.buttonReadState1.TabIndex = 7;
		this.buttonReadState1.Text = "Read State 1";
		this.buttonReadState1.UseVisualStyleBackColor = false;
		this.buttonReadState1.Click += new System.EventHandler(buttonReadState1_Click);
		this.labelIb.AutoSize = true;
		this.labelIb.Font = new System.Drawing.Font("Arial", 11.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.labelIb.Location = new System.Drawing.Point(129, 180);
		this.labelIb.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.labelIb.Name = "labelIb";
		this.labelIb.Size = new System.Drawing.Size(16, 17);
		this.labelIb.TabIndex = 68;
		this.labelIb.Text = "0";
		this.label38.AutoSize = true;
		this.label38.Font = new System.Drawing.Font("Arial", 11.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.label38.Location = new System.Drawing.Point(11, 132);
		this.label38.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label38.Name = "label38";
		this.label38.Size = new System.Drawing.Size(63, 17);
		this.label38.TabIndex = 37;
		this.label38.Text = "Encoder";
		this.label64.AutoSize = true;
		this.label64.Font = new System.Drawing.Font("Arial", 11.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.label64.Location = new System.Drawing.Point(11, 180);
		this.label64.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label64.Name = "label64";
		this.label64.Size = new System.Drawing.Size(21, 17);
		this.label64.TabIndex = 67;
		this.label64.Text = "IB";
		this.label27.AutoSize = true;
		this.label27.Font = new System.Drawing.Font("Arial", 11.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.label27.Location = new System.Drawing.Point(11, 84);
		this.label27.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label27.Name = "label27";
		this.label27.Size = new System.Drawing.Size(105, 17);
		this.label27.TabIndex = 43;
		this.label27.Text = "Torque Current";
		this.labelIa.AutoSize = true;
		this.labelIa.Font = new System.Drawing.Font("Arial", 11.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.labelIa.Location = new System.Drawing.Point(129, 156);
		this.labelIa.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.labelIa.Name = "labelIa";
		this.labelIa.Size = new System.Drawing.Size(16, 17);
		this.labelIa.TabIndex = 66;
		this.labelIa.Text = "0";
		this.labelBusVoltage.AutoSize = true;
		this.labelBusVoltage.Font = new System.Drawing.Font("Arial", 11.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.labelBusVoltage.Location = new System.Drawing.Point(129, 12);
		this.labelBusVoltage.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.labelBusVoltage.Name = "labelBusVoltage";
		this.labelBusVoltage.Size = new System.Drawing.Size(29, 17);
		this.labelBusVoltage.TabIndex = 54;
		this.labelBusVoltage.Text = "0 V";
		this.label61.AutoSize = true;
		this.label61.Font = new System.Drawing.Font("Arial", 11.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.label61.Location = new System.Drawing.Point(11, 156);
		this.label61.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label61.Name = "label61";
		this.label61.Size = new System.Drawing.Size(20, 17);
		this.label61.TabIndex = 65;
		this.label61.Text = "IA";
		this.labelMotorTemperature.AutoSize = true;
		this.labelMotorTemperature.Font = new System.Drawing.Font("Arial", 11.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.labelMotorTemperature.Location = new System.Drawing.Point(129, 60);
		this.labelMotorTemperature.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.labelMotorTemperature.Name = "labelMotorTemperature";
		this.labelMotorTemperature.Size = new System.Drawing.Size(32, 17);
		this.labelMotorTemperature.TabIndex = 54;
		this.labelMotorTemperature.Text = "0 ℃";
		this.labelIq.AutoSize = true;
		this.labelIq.Font = new System.Drawing.Font("Arial", 11.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.labelIq.Location = new System.Drawing.Point(129, 84);
		this.labelIq.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.labelIq.Name = "labelIq";
		this.labelIq.Size = new System.Drawing.Size(16, 17);
		this.labelIq.TabIndex = 54;
		this.labelIq.Text = "0";
		this.labelSpeed.AutoSize = true;
		this.labelSpeed.Font = new System.Drawing.Font("Arial", 11.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.labelSpeed.Location = new System.Drawing.Point(129, 108);
		this.labelSpeed.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.labelSpeed.Name = "labelSpeed";
		this.labelSpeed.Size = new System.Drawing.Size(44, 17);
		this.labelSpeed.TabIndex = 54;
		this.labelSpeed.Text = "0 dps";
		this.labelEncoder.AutoSize = true;
		this.labelEncoder.Font = new System.Drawing.Font("Arial", 11.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.labelEncoder.Location = new System.Drawing.Point(129, 132);
		this.labelEncoder.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.labelEncoder.Name = "labelEncoder";
		this.labelEncoder.Size = new System.Drawing.Size(16, 17);
		this.labelEncoder.TabIndex = 54;
		this.labelEncoder.Text = "0";
		this.panel21.BackColor = System.Drawing.Color.WhiteSmoke;
		this.panel21.Dock = System.Windows.Forms.DockStyle.Top;
		this.panel21.Location = new System.Drawing.Point(0, 251);
		this.panel21.Name = "panel21";
		this.panel21.Size = new System.Drawing.Size(310, 5);
		this.panel21.TabIndex = 79;
		this.panel18.Controls.Add(this.label29);
		this.panel18.Controls.Add(this.buttonSendControlCommand);
		this.panel18.Controls.Add(this.buttonMotorRestore);
		this.panel18.Controls.Add(this.numericUpDownControlAngle);
		this.panel18.Controls.Add(this.label35);
		this.panel18.Controls.Add(this.numericUpDownControlSpeed);
		this.panel18.Controls.Add(this.label36);
		this.panel18.Controls.Add(this.checkBoxControlReverse);
		this.panel18.Controls.Add(this.comboBoxSelectControlMode);
		this.panel18.Controls.Add(this.numericUpDownControlTorque);
		this.panel18.Controls.Add(this.label30);
		this.panel18.Controls.Add(this.buttonMotorStop);
		this.panel18.Dock = System.Windows.Forms.DockStyle.Top;
		this.panel18.Location = new System.Drawing.Point(0, 0);
		this.panel18.Name = "panel18";
		this.panel18.Size = new System.Drawing.Size(310, 251);
		this.panel18.TabIndex = 71;
		this.label29.AutoSize = true;
		this.label29.Location = new System.Drawing.Point(80, 5);
		this.label29.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label29.Name = "label29";
		this.label29.Size = new System.Drawing.Size(125, 22);
		this.label29.TabIndex = 51;
		this.label29.Text = "Control mode";
		this.buttonSendControlCommand.BackColor = System.Drawing.SystemColors.Window;
		this.buttonSendControlCommand.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonSendControlCommand.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonSendControlCommand.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonSendControlCommand.Font = new System.Drawing.Font("Arial", 12f);
		this.buttonSendControlCommand.Location = new System.Drawing.Point(166, 179);
		this.buttonSendControlCommand.Margin = new System.Windows.Forms.Padding(6);
		this.buttonSendControlCommand.Name = "buttonSendControlCommand";
		this.buttonSendControlCommand.Size = new System.Drawing.Size(130, 29);
		this.buttonSendControlCommand.TabIndex = 6;
		this.buttonSendControlCommand.Text = "Send";
		this.buttonSendControlCommand.UseVisualStyleBackColor = false;
		this.buttonSendControlCommand.Click += new System.EventHandler(buttonSendControlCommand_Click);
		this.buttonMotorRestore.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonMotorRestore.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonMotorRestore.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonMotorRestore.Font = new System.Drawing.Font("Arial", 12f);
		this.buttonMotorRestore.Location = new System.Drawing.Point(10, 216);
		this.buttonMotorRestore.Margin = new System.Windows.Forms.Padding(6);
		this.buttonMotorRestore.Name = "buttonMotorRestore";
		this.buttonMotorRestore.Size = new System.Drawing.Size(130, 29);
		this.buttonMotorRestore.TabIndex = 53;
		this.buttonMotorRestore.Text = "Motor Restore";
		this.buttonMotorRestore.UseVisualStyleBackColor = false;
		this.buttonMotorRestore.Click += new System.EventHandler(buttonMotorRestore_Click);
		this.numericUpDownControlAngle.DecimalPlaces = 2;
		this.numericUpDownControlAngle.Location = new System.Drawing.Point(10, 142);
		this.numericUpDownControlAngle.Margin = new System.Windows.Forms.Padding(2);
		this.numericUpDownControlAngle.Maximum = new decimal(new int[4] { 359999999, 0, 0, 131072 });
		this.numericUpDownControlAngle.Minimum = new decimal(new int[4] { 359999999, 0, 0, -2147352576 });
		this.numericUpDownControlAngle.Name = "numericUpDownControlAngle";
		this.numericUpDownControlAngle.Size = new System.Drawing.Size(130, 29);
		this.numericUpDownControlAngle.TabIndex = 3;
		this.label35.AutoSize = true;
		this.label35.Location = new System.Drawing.Point(146, 108);
		this.label35.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label35.Name = "label35";
		this.label35.Size = new System.Drawing.Size(67, 22);
		this.label35.TabIndex = 46;
		this.label35.Text = "Speed";
		this.numericUpDownControlSpeed.DecimalPlaces = 2;
		this.numericUpDownControlSpeed.Location = new System.Drawing.Point(10, 105);
		this.numericUpDownControlSpeed.Margin = new System.Windows.Forms.Padding(2);
		this.numericUpDownControlSpeed.Maximum = new decimal(new int[4] { 48000, 0, 0, 0 });
		this.numericUpDownControlSpeed.Minimum = new decimal(new int[4] { 48000, 0, 0, -2147483648 });
		this.numericUpDownControlSpeed.Name = "numericUpDownControlSpeed";
		this.numericUpDownControlSpeed.Size = new System.Drawing.Size(130, 29);
		this.numericUpDownControlSpeed.TabIndex = 2;
		this.numericUpDownControlSpeed.Value = new decimal(new int[4] { 360, 0, 0, 0 });
		this.label36.AutoSize = true;
		this.label36.Location = new System.Drawing.Point(146, 145);
		this.label36.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label36.Name = "label36";
		this.label36.Size = new System.Drawing.Size(59, 22);
		this.label36.TabIndex = 49;
		this.label36.Text = "Angle";
		this.checkBoxControlReverse.AutoSize = true;
		this.checkBoxControlReverse.Location = new System.Drawing.Point(233, 143);
		this.checkBoxControlReverse.Margin = new System.Windows.Forms.Padding(2);
		this.checkBoxControlReverse.Name = "checkBoxControlReverse";
		this.checkBoxControlReverse.Size = new System.Drawing.Size(63, 26);
		this.checkBoxControlReverse.TabIndex = 4;
		this.checkBoxControlReverse.Text = "Rev";
		this.checkBoxControlReverse.UseVisualStyleBackColor = true;
		this.comboBoxSelectControlMode.FormattingEnabled = true;
		this.comboBoxSelectControlMode.Items.AddRange(new object[8] { "Torque Control", "Speed Control", "Multi Loop Angle Control1", "Multi Loop Angle Control2", "Single Loop Angle Control1", "Single Loop Angle Control2", "Increment Angle Control1", "Increment Angle Control2" });
		this.comboBoxSelectControlMode.Location = new System.Drawing.Point(10, 30);
		this.comboBoxSelectControlMode.Margin = new System.Windows.Forms.Padding(2);
		this.comboBoxSelectControlMode.Name = "comboBoxSelectControlMode";
		this.comboBoxSelectControlMode.Size = new System.Drawing.Size(286, 30);
		this.comboBoxSelectControlMode.TabIndex = 0;
		this.comboBoxSelectControlMode.SelectedIndexChanged += new System.EventHandler(comboBoxSelectControlMode_SelectedIndexChanged);
		this.numericUpDownControlTorque.Location = new System.Drawing.Point(10, 68);
		this.numericUpDownControlTorque.Margin = new System.Windows.Forms.Padding(2);
		this.numericUpDownControlTorque.Maximum = new decimal(new int[4] { 2000, 0, 0, 0 });
		this.numericUpDownControlTorque.Minimum = new decimal(new int[4] { 2000, 0, 0, -2147483648 });
		this.numericUpDownControlTorque.Name = "numericUpDownControlTorque";
		this.numericUpDownControlTorque.Size = new System.Drawing.Size(130, 29);
		this.numericUpDownControlTorque.TabIndex = 1;
		this.label30.AutoSize = true;
		this.label30.Location = new System.Drawing.Point(146, 71);
		this.label30.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label30.Name = "label30";
		this.label30.Size = new System.Drawing.Size(136, 22);
		this.label30.TabIndex = 52;
		this.label30.Text = "Torque Current";
		this.buttonMotorStop.BackColor = System.Drawing.SystemColors.Window;
		this.buttonMotorStop.FlatAppearance.MouseDownBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonMotorStop.FlatAppearance.MouseOverBackColor = System.Drawing.Color.LightSkyBlue;
		this.buttonMotorStop.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonMotorStop.Font = new System.Drawing.Font("Arial", 12f);
		this.buttonMotorStop.Location = new System.Drawing.Point(10, 179);
		this.buttonMotorStop.Margin = new System.Windows.Forms.Padding(6);
		this.buttonMotorStop.Name = "buttonMotorStop";
		this.buttonMotorStop.Size = new System.Drawing.Size(130, 29);
		this.buttonMotorStop.TabIndex = 5;
		this.buttonMotorStop.Text = "Motor Stop";
		this.buttonMotorStop.UseVisualStyleBackColor = false;
		this.buttonMotorStop.Click += new System.EventHandler(buttonMotorStop_Click);
		this.tabPage5.Controls.Add(this.label46);
		this.tabPage5.Controls.Add(this.pictureBox3);
		this.tabPage5.Controls.Add(this.label47);
		this.tabPage5.Controls.Add(this.pictureBox2);
		this.tabPage5.Controls.Add(this.pictureBox4);
		this.tabPage5.Location = new System.Drawing.Point(4, 31);
		this.tabPage5.Margin = new System.Windows.Forms.Padding(2);
		this.tabPage5.Name = "tabPage5";
		this.tabPage5.Padding = new System.Windows.Forms.Padding(2);
		this.tabPage5.Size = new System.Drawing.Size(936, 601);
		this.tabPage5.TabIndex = 6;
		this.tabPage5.Text = "About";
		this.tabPage5.UseVisualStyleBackColor = true;
		this.label46.AutoSize = true;
		this.label46.Location = new System.Drawing.Point(271, 405);
		this.label46.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label46.Name = "label46";
		this.label46.Size = new System.Drawing.Size(229, 22);
		this.label46.TabIndex = 11;
		this.label46.Text = "请使用淘宝APP扫描打开";
		this.pictureBox3.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
		this.pictureBox3.Image = WindowsFormsApplication1.Properties.Resources.logo;
		this.pictureBox3.Location = new System.Drawing.Point(299, 68);
		this.pictureBox3.Margin = new System.Windows.Forms.Padding(2);
		this.pictureBox3.Name = "pictureBox3";
		this.pictureBox3.Size = new System.Drawing.Size(310, 92);
		this.pictureBox3.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
		this.pictureBox3.TabIndex = 10;
		this.pictureBox3.TabStop = false;
		this.label47.AutoSize = true;
		this.label47.Font = new System.Drawing.Font("Arial", 26.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.label47.Location = new System.Drawing.Point(268, 177);
		this.label47.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label47.Name = "label47";
		this.label47.Size = new System.Drawing.Size(377, 40);
		this.label47.TabIndex = 9;
		this.label47.Text = "上海瓴控科技有限公司";
		this.pictureBox2.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
		this.pictureBox2.Image = WindowsFormsApplication1.Properties.Resources.company_shop2;
		this.pictureBox2.Location = new System.Drawing.Point(469, 240);
		this.pictureBox2.Margin = new System.Windows.Forms.Padding(2);
		this.pictureBox2.Name = "pictureBox2";
		this.pictureBox2.Size = new System.Drawing.Size(160, 153);
		this.pictureBox2.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
		this.pictureBox2.TabIndex = 8;
		this.pictureBox2.TabStop = false;
		this.pictureBox4.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Zoom;
		this.pictureBox4.Image = WindowsFormsApplication1.Properties.Resources.company_shop1;
		this.pictureBox4.Location = new System.Drawing.Point(276, 240);
		this.pictureBox4.Margin = new System.Windows.Forms.Padding(2);
		this.pictureBox4.Name = "pictureBox4";
		this.pictureBox4.Size = new System.Drawing.Size(160, 153);
		this.pictureBox4.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
		this.pictureBox4.TabIndex = 7;
		this.pictureBox4.TabStop = false;
		this.buttonMotorOn.BackColor = System.Drawing.Color.MediumSeaGreen;
		this.buttonMotorOn.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonMotorOn.Font = new System.Drawing.Font("Arial", 12f);
		this.buttonMotorOn.Location = new System.Drawing.Point(152, 3);
		this.buttonMotorOn.Margin = new System.Windows.Forms.Padding(6);
		this.buttonMotorOn.Name = "buttonMotorOn";
		this.buttonMotorOn.Size = new System.Drawing.Size(130, 29);
		this.buttonMotorOn.TabIndex = 33;
		this.buttonMotorOn.Text = "Motor On";
		this.buttonMotorOn.UseVisualStyleBackColor = false;
		this.buttonMotorOn.Click += new System.EventHandler(buttonMotorOn_Click);
		this.buttonMotorOff.BackColor = System.Drawing.Color.Tomato;
		this.buttonMotorOff.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
		this.buttonMotorOff.Font = new System.Drawing.Font("Arial", 12f);
		this.buttonMotorOff.Location = new System.Drawing.Point(5, 3);
		this.buttonMotorOff.Margin = new System.Windows.Forms.Padding(6);
		this.buttonMotorOff.Name = "buttonMotorOff";
		this.buttonMotorOff.Size = new System.Drawing.Size(130, 29);
		this.buttonMotorOff.TabIndex = 5;
		this.buttonMotorOff.Text = "Motor Off";
		this.buttonMotorOff.UseVisualStyleBackColor = false;
		this.buttonMotorOff.Click += new System.EventHandler(ButtonMotorOff_Click);
		this.label12.AutoSize = true;
		this.label12.Font = new System.Drawing.Font("Arial", 12f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.label12.Location = new System.Drawing.Point(766, 8);
		this.label12.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.label12.Name = "label12";
		this.label12.Size = new System.Drawing.Size(102, 18);
		this.label12.TabIndex = 43;
		this.label12.Text = "Comm Error :";
		this.labelErrorCount.AutoSize = true;
		this.labelErrorCount.Font = new System.Drawing.Font("Arial", 12f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		this.labelErrorCount.Location = new System.Drawing.Point(873, 8);
		this.labelErrorCount.Margin = new System.Windows.Forms.Padding(2, 0, 2, 0);
		this.labelErrorCount.Name = "labelErrorCount";
		this.labelErrorCount.Size = new System.Drawing.Size(17, 18);
		this.labelErrorCount.TabIndex = 44;
		this.labelErrorCount.Text = "0";
		this.pictureBox1.Image = WindowsFormsApplication1.Properties.Resources.logo;
		this.pictureBox1.Location = new System.Drawing.Point(788, 3);
		this.pictureBox1.Margin = new System.Windows.Forms.Padding(2);
		this.pictureBox1.Name = "pictureBox1";
		this.pictureBox1.Size = new System.Drawing.Size(144, 44);
		this.pictureBox1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
		this.pictureBox1.TabIndex = 46;
		this.pictureBox1.TabStop = false;
		this.panel6.Controls.Add(this.buttonConnect);
		this.panel6.Controls.Add(this.pictureBox1);
		this.panel6.Controls.Add(this.comboBoxSelectCom);
		this.panel6.Controls.Add(this.label1);
		this.panel6.Controls.Add(this.label6);
		this.panel6.Controls.Add(this.comboBoxSelectBaudRate);
		this.panel6.Controls.Add(this.numericUpDownSelectId);
		this.panel6.Controls.Add(this.label23);
		this.panel6.Dock = System.Windows.Forms.DockStyle.Top;
		this.panel6.Location = new System.Drawing.Point(0, 0);
		this.panel6.Name = "panel6";
		this.panel6.Size = new System.Drawing.Size(944, 50);
		this.panel6.TabIndex = 47;
		this.panel8.Controls.Add(this.buttonRebootDevice);
		this.panel8.Controls.Add(this.buttonMotorOn);
		this.panel8.Controls.Add(this.label12);
		this.panel8.Controls.Add(this.labelErrorCount);
		this.panel8.Controls.Add(this.buttonMotorOff);
		this.panel8.Dock = System.Windows.Forms.DockStyle.Bottom;
		this.panel8.Location = new System.Drawing.Point(0, 686);
		this.panel8.Name = "panel8";
		this.panel8.Size = new System.Drawing.Size(944, 35);
		this.panel8.TabIndex = 48;
		base.AutoScaleDimensions = new System.Drawing.SizeF(11f, 22f);
		base.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
		this.BackColor = System.Drawing.Color.White;
		base.ClientSize = new System.Drawing.Size(944, 721);
		base.Controls.Add(this.TabControl);
		base.Controls.Add(this.panel8);
		base.Controls.Add(this.panel6);
		this.DoubleBuffered = true;
		this.Font = new System.Drawing.Font("Arial", 14.25f, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, 0);
		base.Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
		base.Margin = new System.Windows.Forms.Padding(6);
		base.MaximizeBox = false;
		base.Name = "FormMainSetting";
		base.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
		this.Text = "LingKong Motor Tool V2.361";
		((System.ComponentModel.ISupportInitialize)this.numericUpDownSelectId).EndInit();
		this.tabPage1.ResumeLayout(false);
		this.tabPage1.PerformLayout();
		this.panelWriteProgressFull.ResumeLayout(false);
		this.tabPage2.ResumeLayout(false);
		this.panel12.ResumeLayout(false);
		this.panel15.ResumeLayout(false);
		this.panel14.ResumeLayout(false);
		this.panel14.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownReductionRatio).EndInit();
		this.panel13.ResumeLayout(false);
		this.panel13.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownAlignVoltage).EndInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownMotorPoles).EndInit();
		this.tabPage3.ResumeLayout(false);
		this.panel3.ResumeLayout(false);
		this.panel3.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownProtectStallTime).EndInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownProtectOverCurrentTime).EndInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownProtectOverCurrent).EndInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownProtectDriverTemp).EndInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownProtectOverVoltage).EndInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownProtectMotorTemp).EndInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownProtectUnderVoltage).EndInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownProtectLostInputTime).EndInit();
		this.panel2.ResumeLayout(false);
		this.panel2.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownDriverID).EndInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownBrakeResOnVoltage).EndInit();
		this.panel7.ResumeLayout(false);
		this.panel5.ResumeLayout(false);
		this.panel5.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownSpeedKp).EndInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownAngleKp).EndInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownCurrentKp).EndInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownSpeedKi).EndInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownCurrentKi).EndInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownAngleKi).EndInit();
		this.panel9.ResumeLayout(false);
		this.panel9.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownCurrentRamp).EndInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownMaxTorque).EndInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownMaxSpeed).EndInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownMaxAngle).EndInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownSpeedRamp).EndInit();
		this.TabControl.ResumeLayout(false);
		this.tabPage4.ResumeLayout(false);
		this.panel17.ResumeLayout(false);
		this.panel20.ResumeLayout(false);
		this.panel20.PerformLayout();
		this.panel1.ResumeLayout(false);
		this.panel19.ResumeLayout(false);
		this.panel19.PerformLayout();
		this.panel18.ResumeLayout(false);
		this.panel18.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownControlAngle).EndInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownControlSpeed).EndInit();
		((System.ComponentModel.ISupportInitialize)this.numericUpDownControlTorque).EndInit();
		this.tabPage5.ResumeLayout(false);
		this.tabPage5.PerformLayout();
		((System.ComponentModel.ISupportInitialize)this.pictureBox3).EndInit();
		((System.ComponentModel.ISupportInitialize)this.pictureBox2).EndInit();
		((System.ComponentModel.ISupportInitialize)this.pictureBox4).EndInit();
		((System.ComponentModel.ISupportInitialize)this.pictureBox1).EndInit();
		this.panel6.ResumeLayout(false);
		this.panel6.PerformLayout();
		this.panel8.ResumeLayout(false);
		this.panel8.PerformLayout();
		base.ResumeLayout(false);
	}
}
