using Volo.Abp.Domain.Entities;

namespace AuroraStruct3D.SerialPorts;

/// <summary>
/// 串口操作日志实体。
/// 记录每次对串口（打开、关闭、发送原始数据等）的操作时间、类型、参数和结果，便于追溯和故障分析。
///
/// 数据库表：AbpProSerialPortOperationLogs
/// </summary>
public class SerialPortOperationLog : Entity<Guid>
{
    // ─────────────────────────── 关联 ───────────────────────────

    /// <summary>所属串口配置 ID（对应 SerialPortConfig 聚合根）</summary>
    public Guid SerialPortConfigId { get; private set; }

    // ─────────────────────────── 操作信息 ───────────────────────────

    /// <summary>操作类型名称（如 Connect、Disconnect、SendRaw），最多 64 字符</summary>
    public string OperationType { get; private set; } = null!;

    /// <summary>操作发生时间（UTC）</summary>
    public DateTime OccurredAt { get; private set; }

    /// <summary>操作是否成功</summary>
    public bool IsSuccess { get; private set; }

    // ─────────────────────────── 详情 ───────────────────────────

    /// <summary>操作参数摘要（如发送字节数、波特率等），最多 256 字符</summary>
    public string? ParameterSummary { get; private set; }

    /// <summary>失败时的错误消息，最多 512 字符</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>命令往返耗时（毫秒），-1 表示未记录</summary>
    public long RoundTripMs { get; private set; }

    // ─────────────────────────── 构造函数 ───────────────────────────

    /// <summary>EF Core 专用无参构造函数</summary>
    protected SerialPortOperationLog() { }

    // ─────────────────────────── 工厂方法 ───────────────────────────

    /// <summary>
    /// 创建成功操作日志
    /// </summary>
    public static SerialPortOperationLog Success(
        Guid id,
        Guid serialPortConfigId,
        string operationType,
        long roundTripMs = -1,
        string? parameterSummary = null
    )
    {
        return new SerialPortOperationLog
        {
            Id = id,
            SerialPortConfigId = serialPortConfigId,
            OperationType = operationType[
                ..Math.Min(operationType.Length, SerialPortConsts.MaxOperationTypeNameLength)
            ],
            OccurredAt = DateTime.UtcNow,
            IsSuccess = true,
            ParameterSummary = parameterSummary?[
                ..Math.Min(
                    parameterSummary.Length,
                    SerialPortConsts.MaxOperationParameterSummaryLength
                )
            ],
            RoundTripMs = roundTripMs,
        };
    }

    /// <summary>
    /// 创建失败操作日志
    /// </summary>
    public static SerialPortOperationLog Failure(
        Guid id,
        Guid serialPortConfigId,
        string operationType,
        string errorMessage,
        long roundTripMs = -1,
        string? parameterSummary = null
    )
    {
        return new SerialPortOperationLog
        {
            Id = id,
            SerialPortConfigId = serialPortConfigId,
            OperationType = operationType[
                ..Math.Min(operationType.Length, SerialPortConsts.MaxOperationTypeNameLength)
            ],
            OccurredAt = DateTime.UtcNow,
            IsSuccess = false,
            ParameterSummary = parameterSummary?[
                ..Math.Min(
                    parameterSummary.Length,
                    SerialPortConsts.MaxOperationParameterSummaryLength
                )
            ],
            ErrorMessage = errorMessage[
                ..Math.Min(errorMessage.Length, SerialPortConsts.MaxOperationLogErrorMessageLength)
            ],
            RoundTripMs = roundTripMs,
        };
    }
}
