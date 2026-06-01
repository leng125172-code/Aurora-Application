using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.CalibrationManagement.Projects;

/// <summary>
/// 标定工程聚合根自定义仓储接口。
/// </summary>
public interface ICalibrationProjectRepository : IRepository<CalibrationProject, Guid>
{
    /// <summary>加载工程及帧/图像子集合</summary>
    Task<CalibrationProject?> FindWithDetailsAsync(
        Guid id,
        CancellationToken cancellationToken = default
    );

    /// <summary>分页查询（含帧计数）</summary>
    Task<List<CalibrationProject>> GetListAsync(
        string? filter = null,
        Guid? calibrationDeviceId = null,
        CalibrationProjectStatus? status = null,
        int skipCount = 0,
        int maxResultCount = 20,
        string? sorting = null,
        CancellationToken cancellationToken = default
    );

    Task<int> GetCountAsync(
        string? filter = null,
        Guid? calibrationDeviceId = null,
        CalibrationProjectStatus? status = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>查询同工程内最大帧序号（用于生成新帧 FrameIndex）</summary>
    Task<int> GetMaxFrameIndexAsync(
        Guid projectId,
        CancellationToken cancellationToken = default
    );
}
