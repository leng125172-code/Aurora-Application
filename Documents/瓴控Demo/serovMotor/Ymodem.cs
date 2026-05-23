using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace serovMotor;

public class Ymodem
{
	private const byte SOH = 1;

	private const byte STX = 2;

	private const byte EOT = 4;

	private const byte ACK = 6;

	private const byte NAK = 21;

	private const byte C = 67;

	private const int headerSize = 3;

	private const int sohDataSize = 128;

	private const int stxDataSize = 1024;

	private const int crcSize = 2;

	private string path;

	public string Path
	{
		get
		{
			return Path;
		}
		set
		{
			path = value;
		}
	}

	public event EventHandler NowDownloadProgressEvent;

	public event EventHandler DownloadResultEvent;

	public event EventHandler SerialSettingResetEvent;

	public void YmodemDownloadFile()
	{
		byte b = 0;
		byte[] array = new byte[1024];
		if (path.Length == 0)
		{
			YmodemDownloadTerminate(1);
			return;
		}
		FileStream fileStream = new FileStream(path, FileMode.Open, FileAccess.Read);
		try
		{
			if (!General.userSerialPort.IsOpen)
			{
				YmodemDownloadTerminate(2);
				return;
			}
			if (Ymodem_WaitByte(67, 2000) != 0)
			{
				YmodemDownloadTerminate(3);
				return;
			}
			Ymodem_SendSohPacket(path, fileStream);
			while (General.userSerialPort.BytesToWrite > 0)
			{
			}
			if (Ymodem_WaitByte(6, 2000) != 0)
			{
				YmodemDownloadTerminate(4);
				return;
			}
			if (Ymodem_WaitByte(67, 200) != 0)
			{
				YmodemDownloadTerminate(5);
				return;
			}
			int num;
			do
			{
				num = fileStream.Read(array, 0, 1024);
				if (num == 0)
				{
					break;
				}
				if (num != 1024)
				{
					for (int i = num; i < 1024; i++)
					{
						array[i] = byte.MaxValue;
					}
				}
				b++;
				Ymodem_SendStxPacket(b, array);
				while (General.userSerialPort.BytesToWrite > 0)
				{
				}
				int num2 = (int)(1024f * (float)(int)b / (float)fileStream.Length * 100f);
				if (num2 > 100)
				{
					num2 = 100;
				}
				this.NowDownloadProgressEvent(num2, new EventArgs());
				if (Ymodem_WaitByte(6, 1000) != 0)
				{
					YmodemDownloadTerminate(6);
					return;
				}
			}
			while (1024 == num);
			General.userSerialPort.Write(new byte[1] { 4 }, 0, 1);
			while (General.userSerialPort.BytesToWrite > 0)
			{
			}
			if (Ymodem_WaitByte(21, 1000) != 0)
			{
				YmodemDownloadTerminate(7);
				return;
			}
			General.userSerialPort.Write(new byte[1] { 4 }, 0, 1);
			while (General.userSerialPort.BytesToWrite > 0)
			{
			}
			if (Ymodem_WaitByte(6, 1000) != 0)
			{
				YmodemDownloadTerminate(8);
				return;
			}
			if (Ymodem_WaitByte(67, 1000) != 0)
			{
				YmodemDownloadTerminate(9);
				return;
			}
			Ymodem_SendEndPacket();
			while (General.userSerialPort.BytesToWrite > 0)
			{
			}
			if (Ymodem_WaitByte(6, 2000) != 0)
			{
				YmodemDownloadTerminate(10);
				return;
			}
		}
		catch (TimeoutException)
		{
			throw new Exception("Eductor does not answering");
		}
		finally
		{
			fileStream.Close();
		}
		YmodemDownloadTerminate(0);
		this.DownloadResultEvent(true, new EventArgs());
	}

	private void YmodemDownloadTerminate(int errorType)
	{
		if (errorType == 1)
		{
			MessageBox.Show("Error1, Empty file path!");
		}
		else if (errorType == 2)
		{
			MessageBox.Show("Error2! COM port error!");
		}
		else if (errorType > 2)
		{
			MessageBox.Show("Error" + errorType + "!");
		}
		this.SerialSettingResetEvent(1, new EventArgs());
	}

	private int Ymodem_WaitByte(byte data, int time)
	{
		int num = 0;
		for (int i = 0; i < time; i++)
		{
			Thread.Sleep(1);
			num = General.userSerialPort.BytesToRead;
			for (int j = 0; j < num; j++)
			{
				if (data == General.userSerialPort.ReadByte())
				{
					return 0;
				}
			}
		}
		return 1;
	}

	private int Ymodem_WaitByteOnce(byte data, int time)
	{
		Thread.Sleep(time);
		if (General.userSerialPort.BytesToRead >= 1)
		{
			if (General.userSerialPort.ReadByte() == data)
			{
				return 0;
			}
			return 1;
		}
		return -1;
	}

	private void Ymodem_SendSohPacket(string path, FileStream fileStream)
	{
		string fileName = System.IO.Path.GetFileName(path);
		string text = fileStream.Length.ToString();
		byte[] array = new byte[128];
		int i;
		for (i = 0; i < fileName.Length && fileName.ToCharArray()[i] != 0; i++)
		{
			array[i] = (byte)fileName.ToCharArray()[i];
		}
		array[i] = 0;
		int j;
		for (j = 0; j < text.Length && text.ToCharArray()[j] != 0; j++)
		{
			array[i + 1 + j] = (byte)text.ToCharArray()[j];
		}
		array[i + 1 + j] = 0;
		for (int k = i + 1 + j + 1; k < 128; k++)
		{
			array[k] = 0;
		}
		Ymodem_SendPacket(1, 0, array);
	}

	private void Ymodem_SendEndPacket()
	{
		byte[] array = new byte[128];
		byte[] array2 = array;
		foreach (byte b in array2)
		{
			array[b] = 0;
		}
		Ymodem_SendPacket(1, 0, array);
	}

	private void Ymodem_SendStxPacket(byte packetNumber, byte[] data)
	{
		Ymodem_SendPacket(2, packetNumber, data);
	}

	private void Ymodem_SendPacket(byte header, byte packetNumber, byte[] data)
	{
		byte[] array = new byte[1029];
		ushort num = 0;
		array[0] = header;
		array[1] = packetNumber;
		array[2] = (byte)(255 - packetNumber);
		data.CopyTo(array, 3);
		if (header == 1)
		{
			num = General.CRC16_Cal(data, 128u);
			array[131] = (byte)((num >> 8) & 0xFF);
			array[132] = (byte)(num & 0xFF);
			General.userSerialPort.Write(array, 0, 133);
		}
		else
		{
			num = General.CRC16_Cal(data, 1024u);
			array[1027] = (byte)((num >> 8) & 0xFF);
			array[1028] = (byte)(num & 0xFF);
			General.userSerialPort.Write(array, 0, 1029);
		}
	}
}
