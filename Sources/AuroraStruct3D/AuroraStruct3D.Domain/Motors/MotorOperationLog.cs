using Volo.Abp.Domain.Entities;

namespace AuroraStruct3D.Motors;

/// <summary>
/// RS485 伺服电机操作历史日志实体。
/// 记录每次对电机发出指令的时间、类型、参数和结果，便于追溯和故障分析。
///
/// 数据库表：AbpProMotorOperationLogs
/// </summary>
public class MotorOperationLog : Entity<Guid>
{
    // ─────────────────────────── 关联 ───────────────────────────

    /// <summary>所属电机轴 ID（对应 MotorAxis 聚合根）</summary>
    public Guid MotorAxisId { get; private set; }

    /// <summary>Modbus / 私有协议从机地址（1~247），冗余记录便于快速定位</summary>
    public int SlaveId { get; private set; }

    // ─────────────────────────── 操作信息 ───────────────────────────

    /// <summary>操作类型（使能、运动、回零等）</summary>
    public MotorOperationType OperationType { get; private set; }

    /// <summary>操作发生时间（UTC）</summary>
    public DateTime OccurredAt { get; private set; }

    /// <summary>操作是否成功</summary>
    public bool IsSuccess { get; private set; }

    // ─────────────────────────── 命令详情 ───────────────────────────

    /// <summary>
    /// 协议命令标识（如 "0x9A", "FC10", "FC06"），最多 32 字符。
    /// 用于协议层调试。
    /// </summary>
    public string? CommandCode { get; private set; }

    /// <summary>
    /// 操作参数摘要（如 "位置=10000, 速度=300rpm"、"轴=2"），最多 128 字符。
    /// </summary>
    public string? ParameterSummary { get; private set; }

    /// <summary>
    /// 失败时的错误消息，最多 512 字符
    /// </summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// 命令往返耗时（毫秒），-1 表示未记录
    /// </summary>
    public long RoundTripMs { get; private set; }

    // ─────────────────────────── 构造函数 ───────────────────────────

    /// <summary>EF Core 专用无参构造函数</summary>
    protected MotorOperationLog() { }

    // ─────────────────────────── 工厂方法 ───────────────────────────

    /// <summary>
    /// 创建成功操作日志
    /// </summary>
    /// <param name="id">日志 ID（由调用方提供，通常为 IGuidGenerator.Create()）</param>
    /// <param name="motorAxisId">电机轴实体 ID</param>
    /// <param name="slaveId">从机地址</param>
    /// <param name="operationType">操作类型</param>
    /// <param name="roundTripMs">命令往返耗时（毫秒），-1 表示未记录</param>
    /// <param name="commandCode">协议命令标识（可选）</param>
    /// <param name="parameterSummary">参数摘要（可选）</param>
    public static MotorOperationLog Success(
        Guid id,
        Guid motorAxisId,
        int slaveId,
        MotorOperationType operationType,
        long roundTripMs = -1,
        string? commandCode = null,
        string? parameterSummary = null
    )
    {
        return new MotorOperationLog
        {
            Id = id,
            MotorAxisId = motorAxisId,
            SlaveId = slaveId,
            OperationType = operationType,
            OccurredAt = DateTime.UtcNow,
            IsSuccess = true,
            CommandCode = commandCode?[..Math.Min(commandCode.Length, MotorConsts.MaxCommandCodeLength)],
            ParameterSummary = parameterSummary?[
                ..Math.Min(parameterSummary.Length, MotorConsts.MaxOperationParameterSummaryLength)
            ],
            RoundTripMs = roundTripMs,
        };
    }

    /// <summary>
    /// 创建失败操作日志
    /// </summary>
    /// <param name="id">日志 ID</param>
    /// <param name="motorAxisId">电机轴实体 ID</param>
    /// <param name="slaveId">从机地址</param>
    /// <param name="operationType">操作类型</param>
    /// <param name="errorMessage">错误消息</param>
    /// <param name="roundTripMs">命令往返耗时（毫秒），-1 表示未记录</param>
    /// <param name="commandCode">协议命令标识（可选）</param>
    /// <param name="parameterSummary">参数摘要（可选）</param>
    public static MotorOperationLog Failure(
        Guid id,
        Guid motorAxisId,
        int slaveId,
        MotorOperationType operationType,
        string errorMessage,
        long roundTripMs = -1,
        string? commandCode = null,
        string? parameterSummary = null
    )
    {
        return new MotorOperationLog
        {
            Id = id,
            MotorAxisId = motorAxisId,
            SlaveId = slaveId,
            OperationType = operationType,
            OccurredAt = DateTime.UtcNow,
            IsSuccess = false,
            CommandCode = commandCode?[..Math.Min(commandCode.Length, MotorConsts.MaxCommandCodeLength)],
            ParameterSummary = parameterSummary?[
                ..Math.Min(parameterSummary.Length, MotorConsts.MaxOperationParameterSummaryLength)
            ],
            ErrorMessage = errorMessage[
                ..Math.Min(errorMessage.Length, MotorConsts.MaxOperationLogErrorMessageLength)
            ],
            RoundTripMs = roundTripMs,
        };
    }
}
