using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.CalibrationManagement.Devices;

/// <summary>
/// 标定设备聚合根自定义仓储接口。
/// 提供带子集合预加载的查询方法，避免在 Application 层直接依赖 EF Core。
/// </summary>
public interface ICalibrationDeviceRepository : IRepository<CalibrationDevice, Guid>
{
    /// <summary>
    /// 加载标定设备及全部子集合（绑定/云台/规则/参数）。返回 <c>null</c> 表示不存在。
    /// </summary>
    Task<CalibrationDevice?> FindWithDetailsAsync(
        Guid id,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 分页查询标定设备（含绑定子集合用于计数显示）。
    /// </summary>
    Task<List<CalibrationDevice>> GetListAsync(
        string? filter = null,
        CalibrationDeviceType? deviceType = null,
        bool? isActive = null,
        int skipCount = 0,
        int maxResultCount = 20,
        string? sorting = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 查询符合条件的标定设备总数。
    /// </summary>
    Task<int> GetCountAsync(
        string? filter = null,
        CalibrationDeviceType? deviceType = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default
    );
}
