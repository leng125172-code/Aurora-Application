using AuroraStruct3D.RS485.Modbus;

namespace AuroraStruct3D.RS485.Leisai;

/// <summary>
/// 雷赛 iCL-RS 驱动器 Modbus RTU 命令构造与解析工具类。
/// </summary>
/// <remarks>
/// <para>
/// 雷赛参数寄存器采用 32 位数据类型：每个参数占相邻两个 16 位寄存器。
/// <list type="bullet">
///   <item>高 16 位寄存器（偶数地址）：实际值固定为 0x0000，不携带有效数据。</item>
///   <item>低 16 位寄存器（奇数地址，= 高 16 位地址 + 1）：存储参数的有效数值。</item>
/// </list>
/// </para>
/// <para>各功能码的操作约定：</para>
/// <list type="table">
///   <listheader><term>功能码</term><description>操作约定</description></listheader>
///   <item>
///     <term>FC03 读单个参数</term>
///     <description>起始地址 = 参数低 16 位寄存器地址，数量 = 1。</description>
///   </item>
///   <item>
///     <term>FC03 读多个连续参数</term>
///     <description>起始地址 = 首参数高 16 位地址，数量 = 参数个数 × 2；解析时取奇数偏移（低 16 位）。</description>
///   </item>
///   <item>
///     <term>FC06 写单个参数</term>
///     <description>地址 = 参数低 16 位寄存器地址，值 = 有效数值。</description>
///   </item>
///   <item>
///     <term>FC16 写多个连续参数</term>
///     <description>起始地址 = 首参数高 16 位地址；每个参数展开为 [0x0000（高）, 值（低）] 两个寄存器写入。</description>
///   </item>
/// </list>
/// </remarks>
public static class LeisaiModbusHelper
{
    /// <summary>辅助控制字寄存器地址（用于触发 EEPROM 保存）</summary>
    private const ushort RegAuxControlWord = 0x1801;

    /// <summary>写入辅助控制字后触发参数保存至 EEPROM 的固定值</summary>
    private const ushort CmdSaveToEeprom = 0x2211;

    // ═══════════════════════════════════════════════════════════
    // FC03 — 读取 N 个数据
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 构造「读单个 32 位参数」请求帧（FC03，数量 = 1）。
    /// </summary>
    /// <param name="slaveId">从机地址</param>
    /// <param name="paramLowAddress">参数低 16 位寄存器地址（= 高 16 位地址 + 1）</param>
    /// <returns>带 CRC 的完整请求帧</returns>
    /// <example>
    /// 读取 0x0191 峰值电流：BuildReadSingleParameter(0x01, 0x0191)
    /// → 01 03 01 91 00 01 D4 1B
    /// </example>
    public static byte[] BuildReadSingleParameter(byte slaveId, ushort paramLowAddress) =>
        ModbusRtuHelper.BuildReadHoldingRegisters(slaveId, paramLowAddress, quantity: 1);

    /// <summary>
    /// 构造「读 N 个连续 32 位参数」请求帧（FC03，数量 = count × 2）。
    /// </summary>
    /// <param name="slaveId">从机地址</param>
    /// <param name="firstParamHighAddress">首参数高 16 位寄存器地址（低 16 位地址 = 此地址 + 1）</param>
    /// <param name="count">要读取的参数个数</param>
    /// <returns>带 CRC 的完整请求帧</returns>
    /// <example>
    /// 读取 Pr5.22 / Pr5.23 / Pr5.24（低 16 位地址 0x01BD / 0x01BF / 0x01C1，首参高地址 0x01BC）：
    /// BuildReadMultipleParameters(0x01, 0x01BC, 3) → 01 03 01 BC 00 06 05 D0
    /// </example>
    public static byte[] BuildReadMultipleParameters(
        byte slaveId,
        ushort firstParamHighAddress,
        ushort count
    ) =>
        ModbusRtuHelper.BuildReadHoldingRegisters(
            slaveId,
            firstParamHighAddress,
            quantity: (ushort)(count * 2)
        );

    /// <summary>
    /// 解析「读 N 个连续 32 位参数」响应帧，提取每个参数的低 16 位有效值。
    /// </summary>
    /// <param name="response">完整响应帧（含 CRC）</param>
    /// <param name="slaveId">期望的从机地址，用于验证</param>
    /// <param name="count">期望的参数个数（寄存器总数 = count × 2）</param>
    /// <returns>各参数低 16 位值数组，长度 = count</returns>
    /// <exception cref="InvalidDataException">帧格式错误、CRC 不匹配或寄存器数不符时抛出</exception>
    /// <example>
    /// 响应帧 01 03 0C 00 00 00 02 00 00 00 01 00 00 00 04 B6 13（3 个参数）
    /// → 返回 [0x0002, 0x0001, 0x0004]（Pr5.22=2, Pr5.23=1, Pr5.24=4）
    /// </example>
    public static ushort[] ParseReadMultipleParametersResponse(
        ReadOnlySpan<byte> response,
        byte slaveId,
        int count
    )
    {
        ushort[] registers = ModbusRtuHelper.ParseReadHoldingRegisters(response, slaveId);

        int expectedRegisters = count * 2;
        if (registers.Length != expectedRegisters)
        {
            throw new InvalidDataException(
                $"雷赛参数响应寄存器数不符：期望 {expectedRegisters}（{count} 个参数 × 2），实际 {registers.Length}"
            );
        }

        // 偶数偏移（0, 2, 4...）= 高 16 位，始终为 0，忽略
        // 奇数偏移（1, 3, 5...）= 低 16 位，有效值
        ushort[] values = new ushort[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = registers[i * 2 + 1];
        }

        return values;
    }

    /// <summary>
    /// 计算「读 N 个 32 位参数」响应帧的预期总字节数。
    /// </summary>
    /// <param name="count">参数个数</param>
    /// <returns>响应帧总字节数（含 CRC）</returns>
    public static int GetReadMultipleParametersResponseLength(int count) =>
        ModbusRtuHelper.GetReadResponseLength(count * 2);

    // ═══════════════════════════════════════════════════════════
    // FC06 — 写入单个数据
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 构造「写单个 32 位参数」请求帧（FC06）。
    /// 直接向参数的低 16 位寄存器写入有效值。
    /// </summary>
    /// <param name="slaveId">从机地址</param>
    /// <param name="paramLowAddress">参数低 16 位寄存器地址（= 高 16 位地址 + 1）</param>
    /// <param name="value">要写入的参数值（仅低 16 位有效）</param>
    /// <returns>带 CRC 的完整请求帧</returns>
    /// <example>
    /// 将 0x0191 峰值电流改为 1.0A（0x000A = 10 → 1.0A）：
    /// BuildWriteSingleParameter(0x01, 0x0191, 0x000A) → 01 06 01 91 00 0A 59 DC
    /// </example>
    public static byte[] BuildWriteSingleParameter(
        byte slaveId,
        ushort paramLowAddress,
        ushort value
    ) => ModbusRtuHelper.BuildWriteSingleRegister(slaveId, paramLowAddress, value);

    /// <summary>
    /// 构造「保存参数至 EEPROM」请求帧（FC06）。
    /// 固定写辅助控制字寄存器 0x1801 = 0x2211，修改参数后须调用以防断电丢失。
    /// </summary>
    /// <param name="slaveId">从机地址</param>
    /// <returns>带 CRC 的完整请求帧</returns>
    /// <example>
    /// BuildSaveToEeprom(0x01) → 01 06 18 01 22 11 06 06
    /// </example>
    public static byte[] BuildSaveToEeprom(byte slaveId) =>
        ModbusRtuHelper.BuildWriteSingleRegister(slaveId, RegAuxControlWord, CmdSaveToEeprom);

    /// <summary>
    /// 计算 FC06 / FC16 写操作响应帧的预期字节数（固定 8 字节）。
    /// </summary>
    /// <returns>响应帧总字节数（含 CRC），固定为 8</returns>
    public static int GetWriteResponseLength() => ModbusRtuHelper.GetWriteResponseLength();

    // ═══════════════════════════════════════════════════════════
    // FC16 — 写入多个数据
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 构造「写 N 个连续 32 位参数」请求帧（FC16）。
    /// 雷赛每个参数展开为 [0x0000（高 16 位）, 值（低 16 位）] 两个寄存器，
    /// 起始地址须为首参数的高 16 位寄存器地址。
    /// </summary>
    /// <param name="slaveId">从机地址</param>
    /// <param name="firstParamHighAddress">首参数高 16 位寄存器地址</param>
    /// <param name="values">各参数低 16 位有效值数组</param>
    /// <returns>带 CRC 的完整请求帧</returns>
    /// <example>
    /// 将 DI2=0x28、DI3=0x29（首参高地址 0x0146）：
    /// BuildWriteMultipleParameters(0x01, 0x0146, [0x0028, 0x0029])
    /// → 01 10 01 46 00 04 08 00 00 00 28 00 00 00 29 1C 14
    /// </example>
    public static byte[] BuildWriteMultipleParameters(
        byte slaveId,
        ushort firstParamHighAddress,
        ushort[] values
    )
    {
        // 每个参数展开为 [0x0000（高 16 位，固定为 0）, value（低 16 位，有效值）]
        ushort[] registerPairs = new ushort[values.Length * 2];
        for (int i = 0; i < values.Length; i++)
        {
            registerPairs[i * 2] = 0x0000; // 高 16 位：固定为 0
            registerPairs[i * 2 + 1] = values[i]; // 低 16 位：有效值
        }

        return ModbusRtuHelper.BuildWriteMultipleRegisters(
            slaveId,
            firstParamHighAddress,
            registerPairs
        );
    }
}
