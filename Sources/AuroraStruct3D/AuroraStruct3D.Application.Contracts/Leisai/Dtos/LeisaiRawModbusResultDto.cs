namespace AuroraStruct3D.Leisai.Dtos;

/// <summary>
/// 手动 Modbus 命令执行结果。
/// </summary>
public class LeisaiRawModbusResultDto
{
    /// <summary>是否成功</summary>
    public bool Success { get; set; }

    /// <summary>请求帧 hex 字符串（含 CRC，空格分隔）</summary>
    public string RawRequest { get; set; } = string.Empty;

    /// <summary>响应帧 hex 字符串（空格分隔；超时为空字符串）</summary>
    public string RawResponse { get; set; } = string.Empty;

    /// <summary>解析后的寄存器值列表（仅 FC03 读操作有值；写操作为空数组）</summary>
    public ushort[] ParsedValues { get; set; } = [];

    /// <summary>错误信息（成功时为空）</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>本次往返耗时（毫秒）</summary>
    public int ElapsedMs { get; set; }
}

/// <summary>
/// 实时采样开关状态 DTO。
/// </summary>
public class LeisaiSamplingStateDto
{
    /// <summary>是否启用实时采样</summary>
    public bool IsPollingEnabled { get; set; }
}
