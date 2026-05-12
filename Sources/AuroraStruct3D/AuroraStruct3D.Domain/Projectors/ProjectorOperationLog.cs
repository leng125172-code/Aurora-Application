using Volo.Abp.Domain.Entities;

namespace AuroraStruct3D.Projectors;

/// <summary>
/// DLP 投影机操作历史日志实体。
/// 记录每次对投影机发出指令的时间、类型、参数和结果，便于追溯和故障分析。
///
/// 数据库表：AbpProProjectorOperationLogs
/// </summary>
public class ProjectorOperationLog : Entity<Guid>
{
    // ─────────────────────────── 关联 ───────────────────────────

    /// <summary>所属投影机设备 ID</summary>
    public Guid ProjectorDeviceId { get; private set; }

    // ─────────────────────────── 操作信息 ───────────────────────────

    /// <summary>操作类型（开灯、关灯、触发、设置亮度等）</summary>
    public ProjectorOperationType OperationType { get; private set; }

    /// <summary>操作发生时间（UTC）</summary>
    public DateTime OccurredAt { get; private set; }

    /// <summary>操作是否成功</summary>
    public bool IsSuccess { get; private set; }

    // ─────────────────────────── 命令详情 ───────────────────────────

    /// <summary>
    /// 发送的原始 ASCII 命令（如 "LN\r\n"、"LA 100\r\n"），最多 64 字符。
    /// 可用于调试和协议分析。
    /// </summary>
    public string? RawCommand { get; private set; }

    /// <summary>
    /// 操作参数补充说明（如亮度值、显示模式名称），最多 128 字符。
    /// 例如："亮度=150"、"模式=棋盘格"
    /// </summary>
    public string? ParameterSummary { get; private set; }

    /// <summary>
    /// 失败时的错误消息（最多 512 字符）
    /// </summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// 命令往返耗时（毫秒），-1 表示未记录
    /// </summary>
    public int RoundTripMs { get; private set; }

    // ─────────────────────────── 构造函数 ───────────────────────────

    /// <summary>
    /// EF Core 专用无参构造函数
    /// </summary>
    protected ProjectorOperationLog() { }

    /// <summary>
    /// 创建成功操作日志
    /// </summary>
    public static ProjectorOperationLog Success(
        Guid id,
        Guid projectorDeviceId,
        ProjectorOperationType operationType,
        string? rawCommand = null,
        string? parameterSummary = null,
        int roundTripMs = -1
    )
    {
        return new ProjectorOperationLog
        {
            Id = id,
            ProjectorDeviceId = projectorDeviceId,
            OperationType = operationType,
            OccurredAt = DateTime.UtcNow,
            IsSuccess = true,
            RawCommand = rawCommand?[..Math.Min(rawCommand.Length, 64)].TrimEnd('\r', '\n'),
            ParameterSummary = parameterSummary?[..Math.Min(parameterSummary.Length, 128)],
            RoundTripMs = roundTripMs,
        };
    }

    /// <summary>
    /// 创建失败操作日志
    /// </summary>
    public static ProjectorOperationLog Failure(
        Guid id,
        Guid projectorDeviceId,
        ProjectorOperationType operationType,
        string errorMessage,
        string? rawCommand = null,
        string? parameterSummary = null,
        int roundTripMs = -1
    )
    {
        return new ProjectorOperationLog
        {
            Id = id,
            ProjectorDeviceId = projectorDeviceId,
            OperationType = operationType,
            OccurredAt = DateTime.UtcNow,
            IsSuccess = false,
            RawCommand = rawCommand?[..Math.Min(rawCommand.Length, 64)].TrimEnd('\r', '\n'),
            ParameterSummary = parameterSummary?[..Math.Min(parameterSummary.Length, 128)],
            ErrorMessage = errorMessage[
                ..Math.Min(errorMessage.Length, ProjectorConsts.MaxLogMessageLength)
            ],
            RoundTripMs = roundTripMs,
        };
    }
}
