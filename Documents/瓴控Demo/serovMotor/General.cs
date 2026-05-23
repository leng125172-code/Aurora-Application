using System.IO.Ports;

namespace serovMotor;

public static class General
{
	public static SerialPort userSerialPort = new SerialPort();

	private static ushort CRC16_UpdateByte(ushort crcIn, byte data)
	{
		uint num = crcIn;
		uint num2 = (uint)(data | 0x100);
		do
		{
			num <<= 1;
			num2 <<= 1;
			if ((num2 & 0x100) != 0)
			{
				num++;
			}
			if ((num & 0x10000) != 0)
			{
				num ^= 0x1021;
			}
		}
		while ((num2 & 0x10000) == 0);
		return (ushort)(num & 0xFFFF);
	}

	public static ushort CRC16_Cal(byte[] data, uint size)
	{
		uint num = 0u;
		for (int i = 0; i < size; i++)
		{
			num = CRC16_UpdateByte((ushort)num, data[i]);
		}
		num = CRC16_UpdateByte((ushort)num, 0);
		num = CRC16_UpdateByte((ushort)num, 0);
		return (ushort)(num & 0xFFFF);
	}
}
