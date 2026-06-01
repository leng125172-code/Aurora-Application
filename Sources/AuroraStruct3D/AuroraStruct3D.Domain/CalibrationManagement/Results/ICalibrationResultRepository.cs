using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.CalibrationManagement.Results;

/// <summary>
/// 标定结果聚合根自定义仓储接口。
/// </summary>
public interface ICalibrationResultRepository : IRepository<CalibrationResult, Guid>
{
    /// <summary>加载结果及验证记录子集合</summary>
    Task<CalibrationResult?> FindWithDetailsAsync(
        Guid id,
        CancellationToken cancellationToken = default
    );

    /// <summary>分页查询</summary>
    Task<List<CalibrationResult>> GetListAsync(
        Guid? projectId = null,
        Guid? deviceId = null,
        bool? isActive = null,
        int skipCount = 0,
        int maxResultCount = 20,
        string? sorting = null,
        CancellationToken cancellationToken = default
    );

    Task<int> GetCountAsync(
        Guid? projectId = null,
        Guid? deviceId = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>查询同工程内最大版本号（用于生成新版本号）</summary>
    Task<int> GetMaxVersionAsync(
        Guid projectId,
        CancellationToken cancellationToken = default
    );

    /// <summary>取同工程其它已激活结果（用于切换前批量取消）</summary>
    Task<List<CalibrationResult>> GetActiveResultsAsync(
        Guid projectId,
        Guid? excludeResultId = null,
        CancellationToken cancellationToken = default
    );
}
