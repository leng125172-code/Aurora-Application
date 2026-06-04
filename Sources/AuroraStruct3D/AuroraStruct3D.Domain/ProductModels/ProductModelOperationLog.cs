using Volo.Abp.Domain.Entities;

namespace AuroraStruct3D.ProductModels;

/// <summary>
/// 三维数模操作历史日志实体。
/// 记录上传、重命名、删除、重试转换、下载和孤立记录清理等操作。
///
/// 数据库表：AbpProProductModelOperationLogs
/// </summary>
public class ProductModelOperationLog : Entity<Guid>
{
    /// <summary>关联的数模 ID；当操作失败且尚未落库时允许为空</summary>
    public Guid? ProductModelId { get; private set; }

    /// <summary>数模名称快照</summary>
    public string ModelName { get; private set; } = null!;

    /// <summary>原始文件名快照</summary>
    public string? OriginalFileName { get; private set; }

    /// <summary>操作类型</summary>
    public ProductModelOperationType OperationType { get; private set; }

    /// <summary>操作发生时间（UTC）</summary>
    public DateTime OccurredAt { get; private set; }

    /// <summary>操作是否成功</summary>
    public bool IsSuccess { get; private set; }

    /// <summary>操作参数摘要</summary>
    public string? ParameterSummary { get; private set; }

    /// <summary>失败时的错误消息</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>操作耗时（毫秒），-1 表示未记录</summary>
    public int DurationMs { get; private set; }

    /// <summary>EF Core 专用无参构造函数</summary>
    protected ProductModelOperationLog() { }

    /// <summary>
    /// 创建成功操作日志。
    /// </summary>
    public static ProductModelOperationLog Success(
        Guid id,
        Guid? productModelId,
        string modelName,
        string? originalFileName,
        ProductModelOperationType operationType,
        string? parameterSummary = null,
        int durationMs = -1
    )
    {
        return new ProductModelOperationLog
        {
            Id = id,
            ProductModelId = productModelId,
            ModelName = TrimOrDefault(modelName, ProductModelConsts.MaxOperationLogModelNameLength),
            OriginalFileName = TrimOrNull(
                originalFileName,
                ProductModelConsts.MaxOperationLogFileNameLength
            ),
            OperationType = operationType,
            OccurredAt = DateTime.UtcNow,
            IsSuccess = true,
            ParameterSummary = TrimOrNull(
                parameterSummary,
                ProductModelConsts.MaxOperationLogParameterLength
            ),
            DurationMs = durationMs,
        };
    }

    /// <summary>
    /// 创建失败操作日志。
    /// </summary>
    public static ProductModelOperationLog Failure(
        Guid id,
        Guid? productModelId,
        string modelName,
        string? originalFileName,
        ProductModelOperationType operationType,
        string errorMessage,
        string? parameterSummary = null,
        int durationMs = -1
    )
    {
        return new ProductModelOperationLog
        {
            Id = id,
            ProductModelId = productModelId,
            ModelName = TrimOrDefault(modelName, ProductModelConsts.MaxOperationLogModelNameLength),
            OriginalFileName = TrimOrNull(
                originalFileName,
                ProductModelConsts.MaxOperationLogFileNameLength
            ),
            OperationType = operationType,
            OccurredAt = DateTime.UtcNow,
            IsSuccess = false,
            ParameterSummary = TrimOrNull(
                parameterSummary,
                ProductModelConsts.MaxOperationLogParameterLength
            ),
            ErrorMessage = TrimOrDefault(
                errorMessage,
                ProductModelConsts.MaxOperationLogErrorMessageLength
            ),
            DurationMs = durationMs,
        };
    }

    private static string TrimOrDefault(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "未命名数模";
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
