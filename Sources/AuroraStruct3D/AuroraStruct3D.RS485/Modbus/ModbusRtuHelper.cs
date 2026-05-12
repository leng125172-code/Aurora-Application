namespace AuroraStruct3D.RS485.Modbus;

/// <summary>
/// Modbus RTU 功能码定义
/// </summary>
public enum ModbusFunctionCode : byte
{
    /// <summary>读线圈（FC01）</summary>
    ReadCoils = 0x01,

    /// <summary>读离散输入（FC02）</summary>
    ReadDiscreteInputs = 0x02,

    /// <summary>读保持寄存器（FC03）</summary>
    ReadHoldingRegisters = 0x03,

    /// <summary>读输入寄存器（FC04）</summary>
    ReadInputRegisters = 0x04,

    /// <summary>写单个线圈（FC05）</summary>
    WriteSingleCoil = 0x05,

    /// <summary>写单个寄存器（FC06）</summary>
    WriteSingleRegister = 0x06,

    /// <summary>写多个寄存器（FC16）</summary>
    WriteMultipleRegisters = 0x10,
}

/// <summary>
/// Modbus RTU 帧构造与解析工具类
/// </summary>
public static class ModbusRtuHelper
{
    /// <summary>
    /// 构造「读保持寄存器」请求帧（FC03）
    /// </summary>
    /// <param name="slaveId">从机地址（1~247）</param>
    /// <param name="startAddress">起始寄存器地址（0x0000~0xFFFF）</param>
    /// <param name="quantity">读取寄存器数量</param>
    /// <returns>带 CRC 的完整请求帧</returns>
    public static byte[] BuildReadHoldingRegisters(
        byte slaveId,
        ushort startAddress,
        ushort quantity
    )
    {
        byte[] frame =
        [
            slaveId,
            (byte)ModbusFunctionCode.ReadHoldingRegisters,
            (byte)(startAddress >> 8), // 起始地址高字节
            (byte)(startAddress & 0xFF), // 起始地址低字节
            (byte)(quantity >> 8), // 数量高字节
            (byte)(quantity & 0xFF), // 数量低字节
        ];
        return ModbusCrc16.AppendCrc(frame);
    }

    /// <summary>
    /// 构造「写单个寄存器」请求帧（FC06）
    /// </summary>
    /// <param name="slaveId">从机地址</param>
    /// <param name="address">寄存器地址</param>
    /// <param name="value">要写入的寄存器值</param>
    /// <returns>带 CRC 的完整请求帧</returns>
    public static byte[] BuildWriteSingleRegister(byte slaveId, ushort address, ushort value)
    {
        byte[] frame =
        [
            slaveId,
            (byte)ModbusFunctionCode.WriteSingleRegister,
            (byte)(address >> 8),
            (byte)(address & 0xFF),
            (byte)(value >> 8),
            (byte)(value & 0xFF),
        ];
        return ModbusCrc16.AppendCrc(frame);
    }

    /// <summary>
    /// 构造「写多个寄存器」请求帧（FC16）
    /// </summary>
    /// <param name="slaveId">从机地址</param>
    /// <param name="startAddress">起始寄存器地址</param>
    /// <param name="values">要写入的寄存器值数组</param>
    /// <returns>带 CRC 的完整请求帧</returns>
    public static byte[] BuildWriteMultipleRegisters(
        byte slaveId,
        ushort startAddress,
        ushort[] values
    )
    {
        int byteCount = values.Length * 2;
        byte[] frame = new byte[7 + byteCount];
        frame[0] = slaveId;
        frame[1] = (byte)ModbusFunctionCode.WriteMultipleRegisters;
        frame[2] = (byte)(startAddress >> 8);
        frame[3] = (byte)(startAddress & 0xFF);
        frame[4] = (byte)(values.Length >> 8);
        frame[5] = (byte)(values.Length & 0xFF);
        frame[6] = (byte)byteCount;

        for (int i = 0; i < values.Length; i++)
        {
            frame[7 + i * 2] = (byte)(values[i] >> 8);
            frame[7 + i * 2 + 1] = (byte)(values[i] & 0xFF);
        }

        return ModbusCrc16.AppendCrc(frame);
    }

    /// <summary>
    /// 解析「读保持寄存器」响应帧，提取寄存器值列表
    /// </summary>
    /// <param name="response">完整响应帧（含 CRC）</param>
    /// <param name="slaveId">期望的从机地址，用于验证</param>
    /// <returns>解析出的寄存器值数组（大端序）</returns>
    /// <exception cref="InvalidDataException">帧格式错误或 CRC 不匹配时抛出</exception>
    public static ushort[] ParseReadHoldingRegisters(ReadOnlySpan<byte> response, byte slaveId)
    {
        if (response.Length < 5)
        {
            throw new InvalidDataException(
                $"响应帧太短（{response.Length} 字节），至少需要 5 字节"
            );
        }

        if (!ModbusCrc16.Validate(response))
        {
            throw new InvalidDataException(
                $"Modbus CRC16 校验失败，响应帧: {Convert.ToHexString(response)}"
            );
        }

        if (response[0] != slaveId)
        {
            throw new InvalidDataException($"从机地址不匹配，期望 {slaveId}，实际 {response[0]}");
        }

        // 检查异常响应（功能码最高位置1）
        if ((response[1] & 0x80) != 0)
        {
            throw new InvalidDataException($"Modbus 从机返回异常码: 0x{response[2]:X2}");
        }

        if (response[1] != (byte)ModbusFunctionCode.ReadHoldingRegisters)
        {
            throw new InvalidDataException($"功能码不匹配，期望 0x03，实际 0x{response[1]:X2}");
        }

        int byteCount = response[2];
        if (response.Length < 3 + byteCount + 2)
        {
            throw new InvalidDataException("响应帧数据区长度与字节计数不符");
        }

        int registerCount = byteCount / 2;
        ushort[] registers = new ushort[registerCount];
        for (int i = 0; i < registerCount; i++)
        {
            registers[i] = (ushort)((response[3 + i * 2] << 8) | response[4 + i * 2]);
        }

        return registers;
    }

    /// <summary>
    /// 计算「读保持寄存器」响应帧的预期总字节数
    /// </summary>
    /// <param name="quantity">请求读取的寄存器数量</param>
    /// <returns>响应帧总字节数（含 CRC）</returns>
    public static int GetReadResponseLength(int quantity) => 3 + quantity * 2 + 2;

    /// <summary>
    /// 计算「写单个/多个寄存器」响应帧的预期字节数（8 字节固定）
    /// </summary>
    public static int GetWriteResponseLength() => 8;
}
