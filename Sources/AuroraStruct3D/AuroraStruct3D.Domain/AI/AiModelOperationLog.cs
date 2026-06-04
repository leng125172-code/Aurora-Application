using Volo.Abp.Domain.Entities;

namespace AuroraStruct3D.AI;

/// <summary>
/// AI 模型操作历史日志实体。
/// </summary>
public class AiModelOperationLog : Entity<Guid>
{
    /// <summary>关联的模型 ID。</summary>
    public Guid? AiModelId { get; private set; }

    /// <summary>模型名称快照。</summary>
    public string ModelName { get; private set; } = null!;

    /// <summary>原始文件名快照。</summary>
    public string? OriginalFileName { get; private set; }

    /// <summary>操作类型。</summary>
    public AiModelOperationType OperationType { get; private set; }

    /// <summary>操作发生时间（UTC）。</summary>
    public DateTime OccurredAt { get; private set; }

    /// <summary>操作是否成功。</summary>
    public bool IsSuccess { get; private set; }

    /// <summary>参数摘要。</summary>
    public string? ParameterSummary { get; private set; }

    /// <summary>失败时的错误消息。</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>操作耗时（毫秒）。</summary>
    public int DurationMs { get; private set; }

    /// <summary>EF Core 使用的无参构造函数。</summary>
    protected AiModelOperationLog() { }

    /// <summary>
    /// 创建成功日志。
    /// </summary>
    public static AiModelOperationLog Success(
        Guid id,
        Guid? aiModelId,
        string modelName,
        string? originalFileName,
        AiModelOperationType operationType,
        string? parameterSummary = null,
        int durationMs = -1
    )
    {
        return new AiModelOperationLog
        {
            Id = id,
            AiModelId = aiModelId,
            ModelName = TrimOrDefault(modelName, AiModelConsts.MaxOperationLogModelNameLength),
            OriginalFileName = TrimOrNull(
                originalFileName,
                AiModelConsts.MaxOperationLogFileNameLength
            ),
            OperationType = operationType,
            OccurredAt = DateTime.UtcNow,
            IsSuccess = true,
            ParameterSummary = TrimOrNull(
                parameterSummary,
                AiModelConsts.MaxOperationLogParameterLength
            ),
            DurationMs = durationMs,
        };
    }

    /// <summary>
    /// 创建失败日志。
    /// </summary>
    public static AiModelOperationLog Failure(
        Guid id,
        Guid? aiModelId,
        string modelName,
        string? originalFileName,
        AiModelOperationType operationType,
        string errorMessage,
        string? parameterSummary = null,
        int durationMs = -1
    )
    {
        return new AiModelOperationLog
        {
            Id = id,
            AiModelId = aiModelId,
            ModelName = TrimOrDefault(modelName, AiModelConsts.MaxOperationLogModelNameLength),
            OriginalFileName = TrimOrNull(
                originalFileName,
                AiModelConsts.MaxOperationLogFileNameLength
            ),
            OperationType = operationType,
            OccurredAt = DateTime.UtcNow,
            IsSuccess = false,
            ParameterSummary = TrimOrNull(
                parameterSummary,
                AiModelConsts.MaxOperationLogParameterLength
            ),
            ErrorMessage = TrimOrDefault(
                errorMessage,
                AiModelConsts.MaxOperationLogErrorMessageLength
            ),
            DurationMs = durationMs,
        };
    }

    private static string TrimOrDefault(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "未命名模型";
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static string? TrimOrNull(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
