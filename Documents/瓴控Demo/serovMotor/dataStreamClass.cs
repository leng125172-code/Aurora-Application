using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace serovMotor;

public class dataStreamClass
{
	public const uint SAVE_SETTING_SAVED_FLAG = 1437226616u;

	public const uint SAVE_CALIB_SAVED_FLAG = 1437209140u;

	public const ushort FIRMWARE_VERSION = 10;

	public string[] deviceTypeName = new string[255];

	public string[] encoderType = new string[20];

	public string[] encoderPosition = new string[3];

	public string[] motorPhaseSequence = new string[3];

	public string cmdText = "";

	public string ackText = "";

	public byte[] driverName = new byte[20];

	public byte[] motorName = new byte[20];

	public byte[] uniqueId = new byte[12];

	public ushort hardwareVersion;

	public ushort motorVersion;

	public ushort firmwareVersion;

	public List<byte> sendBuffer = new List<byte>(1024);

	public ushort sendCmd;

	public byte[] sendValidData = new byte[255];

	public bool sendSuccess;

	public List<byte> receiveBuffer = new List<byte>(1048576);

	public byte receiveCmd;

	public byte receiveId = 1;

	public byte[] receiveCmdData = new byte[255];

	public byte receiveCmdDataSize;

	public bool receiveSuccess;

	public string[] cmdConnectErrorText = new string[10];

	public dataStreamClass()
	{
		encoderType[0] = "AS5600";
		encoderType[1] = "AS5047P";
		encoderType[2] = "AS5048A";
		encoderType[3] = "AS5048B";
		encoderType[4] = "TLE5012B";
		encoderType[5] = "14Bit Encoder";
		encoderType[6] = "14Bit Encoder";
		encoderType[7] = "18Bit Encoder";
		encoderType[8] = "21Bit Encoder";
		encoderType[9] = "19Bit Encoder";
		encoderPosition[0] = "Normal";
		encoderPosition[1] = "Reverse";
		motorPhaseSequence[0] = "Normal";
		motorPhaseSequence[1] = "Reverse";
		cmdConnectErrorText[0] = "Connect error!\n\nNo response!";
		cmdConnectErrorText[1] = "Connect error!\n\nWrong device!";
		cmdConnectErrorText[2] = "Connect error!\n\nWrong firmware version!";
		cmdConnectErrorText[3] = "Connect error!\n\nRead product information failed!";
		cmdConnectErrorText[4] = "Connect error!\n\nRead calibration failed!";
		cmdConnectErrorText[5] = "Connect error!\n\nRead setting failed!";
		cmdConnectErrorText[6] = "Connect error!\n\nConnect failed!";
	}

	public void receiveDataParse()
	{
		byte b = 0;
		byte b2 = 0;
		receiveCmd = 0;
		receiveId = 0;
		receiveCmdDataSize = 0;
		receiveSuccess = false;
		while (receiveBuffer.Count >= 5)
		{
			if (receiveBuffer[0] == 62)
			{
				if (receiveBuffer[2] > 0 && receiveBuffer[2] <= 32)
				{
					for (int i = 0; i < 4; i++)
					{
						b2 += receiveBuffer[i];
					}
					if (b2 == receiveBuffer[4])
					{
						b2 = 0;
						b = receiveBuffer[3];
						if (b <= 0)
						{
							receiveCmd = receiveBuffer[1];
							receiveId = receiveBuffer[2];
							receiveCmdDataSize = receiveBuffer[3];
							receiveBuffer.RemoveRange(0, 5);
							receiveSuccess = true;
							break;
						}
						if (receiveBuffer.Count < 5 + b + 1)
						{
							break;
						}
						for (int j = 0; j < b; j++)
						{
							b2 += receiveBuffer[j + 5];
						}
						if (b2 == receiveBuffer[5 + b])
						{
							receiveCmd = receiveBuffer[1];
							receiveId = receiveBuffer[2];
							receiveCmdDataSize = receiveBuffer[3];
							receiveBuffer.CopyTo(5, receiveCmdData, 0, b);
							receiveBuffer.RemoveRange(0, 5 + b + 1);
							receiveSuccess = true;
							break;
						}
						receiveBuffer.RemoveRange(0, 1);
					}
					else
					{
						receiveBuffer.RemoveRange(0, 1);
					}
				}
				else
				{
					receiveBuffer.RemoveRange(0, 1);
				}
			}
			else
			{
				receiveBuffer.RemoveAt(0);
			}
		}
	}

	public object structUpdate(Type strType, int size)
	{
		byte[] array = new byte[size];
		for (int i = 0; i < receiveCmdDataSize; i++)
		{
			array[i] = receiveCmdData[i];
		}
		return BytesToStruct(array, strType);
	}

	public void cmdPack(byte cmd, byte id, byte dataLength, byte[] data)
	{
		byte b = 0;
		byte b2 = 0;
		sendBuffer.Clear();
		sendBuffer.Add(62);
		sendBuffer.Add(cmd);
		sendBuffer.Add(id);
		sendBuffer.Add(dataLength);
		b2 = (byte)(sendBuffer[0] + sendBuffer[1] + sendBuffer[2] + sendBuffer[3]);
		sendBuffer.Add(b2);
		b2 = 0;
		if (dataLength != 0)
		{
			for (b = 0; b < dataLength; b++)
			{
				sendBuffer.Add(data[b]);
				b2 += sendBuffer[b + 5];
			}
			sendBuffer.Add(b2);
		}
	}

	public byte saveSetting_GetSize()
	{
		return (byte)Marshal.SizeOf(default(saveSetting_t));
	}

	public byte saveCalibMsMfMh_GetSize()
	{
		return (byte)Marshal.SizeOf(default(saveCalibMsMfMh_struct));
	}

	public byte saveCalibMg_GetSize()
	{
		return (byte)Marshal.SizeOf(default(saveCalibMg_struct));
	}

	public long bytesToU64(byte[] bytes)
	{
		long num = 0L;
		return (long)BytesToStruct(bytes, num.GetType());
	}

	public uint bytesToU32(byte[] bytes)
	{
		uint num = 0u;
		return (uint)BytesToStruct(bytes, num.GetType());
	}

	public ushort bytesToU16(byte[] bytes)
	{
		ushort num = 0;
		return (ushort)BytesToStruct(bytes, num.GetType());
	}

	public static byte[] StructToBytes(object structObj)
	{
		int num = Marshal.SizeOf(structObj);
		byte[] array = new byte[num];
		IntPtr intPtr = Marshal.AllocHGlobal(num);
		Marshal.StructureToPtr(structObj, intPtr, fDeleteOld: false);
		Marshal.Copy(intPtr, array, 0, num);
		Marshal.FreeHGlobal(intPtr);
		return array;
	}

	public static object BytesToStruct(byte[] bytes, Type strType)
	{
		int num = Marshal.SizeOf(strType);
		if (num > bytes.Length)
		{
			return null;
		}
		IntPtr intPtr = Marshal.AllocHGlobal(num);
		Marshal.Copy(bytes, 0, intPtr, num);
		object result = Marshal.PtrToStructure(intPtr, strType);
		Marshal.FreeHGlobal(intPtr);
		return result;
	}
}
