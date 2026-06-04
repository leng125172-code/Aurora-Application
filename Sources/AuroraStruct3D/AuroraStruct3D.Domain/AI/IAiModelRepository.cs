using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.AI;

/// <summary>
/// AI 模型仓储接口。
/// </summary>
public interface IAiModelRepository : IRepository<AiModel, Guid>
{
    /// <summary>
    /// 分页查询 AI 模型列表。
    /// </summary>
    Task<List<AiModel>> GetListAsync(
        string? filter = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        Guid? creatorId = null,
        string? locationKey = null,
        string? generationCondition = null,
        AiModelConversionPreference? conversionPreference = null,
        AiModelResolvedConversionType? resolvedConversionType = null,
        AiModelFileRole? fileRole = null,
        string? fileFormat = null,
        int skipCount = 0,
        int maxResultCount = 20,
        string? sorting = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 查询 AI 模型总数。
    /// </summary>
    Task<int> GetCountAsync(
        string? filter = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        Guid? creatorId = null,
        string? locationKey = null,
        string? generationCondition = null,
        AiModelConversionPreference? conversionPreference = null,
        AiModelResolvedConversionType? resolvedConversionType = null,
        AiModelFileRole? fileRole = null,
        string? fileFormat = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 按 MD5 查找模型。
    /// </summary>
    Task<AiModel?> FindByMd5Async(string md5, CancellationToken cancellationToken = default);
}

/// <summary>
/// AI 模型文件仓储接口。
/// </summary>
public interface IAiModelFileRepository : IRepository<AiModelFile, Guid>
{
    /// <summary>
    /// 按模型 ID 列表获取文件列表。
    /// </summary>
    Task<List<AiModelFile>> GetListByModelIdsAsync(
        IEnumerable<Guid> aiModelIds,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 按模型 ID 获取文件列表。
    /// </summary>
    Task<List<AiModelFile>> GetListByModelIdAsync(
        Guid aiModelId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 删除指定模型的全部文件。
    /// </summary>
    Task DeleteByModelIdAsync(Guid aiModelId, CancellationToken cancellationToken = default);
}

/// <summary>
/// AI 模型标识仓储接口。
/// </summary>
public interface IAiModelIdentifierRepository : IRepository<AiModelIdentifier, Guid>
{
    /// <summary>
    /// 查询标识列表。
    /// </summary>
    Task<List<AiModelIdentifier>> GetListAsync(
        string? filter = null,
        int maxResultCount = 100,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 按 ID 列表获取标识。
    /// </summary>
    Task<List<AiModelIdentifier>> GetByIdsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 按规范化名称列表获取标识。
    /// </summary>
    Task<List<AiModelIdentifier>> GetByNamesAsync(
        IEnumerable<string> normalizedNames,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 删除未被任何模型引用的标识。
    /// </summary>
    Task<int> DeleteUnusedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// AI 模型标识关联仓储接口。
/// </summary>
public interface IAiModelIdentifierLinkRepository : IRepository<AiModelIdentifierLink, Guid>
{
    /// <summary>
    /// 按模型 ID 列表获取关联。
    /// </summary>
    Task<List<AiModelIdentifierLink>> GetListByModelIdsAsync(
        IEnumerable<Guid> aiModelIds,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 替换模型关联的标识列表。
    /// </summary>
    Task ReplaceAsync(
        Guid aiModelId,
        IEnumerable<Guid> identifierIds,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 删除指定模型的全部标识关联。
    /// </summary>
    Task DeleteByModelIdAsync(Guid aiModelId, CancellationToken cancellationToken = default);
}

/// <summary>
/// AI 模型操作日志仓储接口。
/// </summary>
public interface IAiModelOperationLogRepository : IRepository<AiModelOperationLog, Guid>
{
    /// <summary>
    /// 分页查询 AI 模型操作日志。
    /// </summary>
    Task<List<AiModelOperationLog>> GetPagedListAsync(
        Guid? aiModelId = null,
        string? filter = null,
        AiModelOperationType? operationType = null,
        bool onlyFailures = false,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int skipCount = 0,
        int maxResultCount = 20,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 查询 AI 模型操作日志总数。
    /// </summary>
    Task<long> GetCountAsync(
        Guid? aiModelId = null,
        string? filter = null,
        AiModelOperationType? operationType = null,
        bool onlyFailures = false,
        DateTime? startTime = null,
        DateTime? endTime = null,
        CancellationToken cancellationToken = default
    );
}
