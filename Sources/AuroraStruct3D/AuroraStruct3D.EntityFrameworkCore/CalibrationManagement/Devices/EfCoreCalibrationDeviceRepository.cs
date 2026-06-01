using AuroraStruct3D.CalibrationManagement;
using AuroraStruct3D.CalibrationManagement.Devices;
using AuroraStruct3D.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace AuroraStruct3D.CalibrationManagement.Devices;

/// <summary>
/// 标定设备 EF Core 仓储实现。
/// 集中处理所有子集合的预加载，避免在 Application 层引用 EF Core。
/// </summary>
public class EfCoreCalibrationDeviceRepository
    : EfCoreRepository<AuroraStruct3DDbContext, CalibrationDevice, Guid>,
        ICalibrationDeviceRepository
{
    public EfCoreCalibrationDeviceRepository(
        IDbContextProvider<AuroraStruct3DDbContext> dbContextProvider
    )
        : base(dbContextProvider) { }

    /// <inheritdoc/>
    public async Task<CalibrationDevice?> FindWithDetailsAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .CalibrationDevices.Where(x => x.Id == id)
            .Include(x => x.CameraBindings)
            .Include(x => x.MotorBindings)
            .Include(x => x.ProjectorBindings)
            .Include(x => x.GimbalGroups)
            .ThenInclude(g => g.PresetPositions)
            .Include(x => x.InterlockRules)
            .Include(x => x.CameraParameters)
            .Include(x => x.ProjectorParameters)
            .Include(x => x.MotorParameters)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<List<CalibrationDevice>> GetListAsync(
        string? filter = null,
        CalibrationDeviceType? deviceType = null,
        bool? isActive = null,
        int skipCount = 0,
        int maxResultCount = 20,
        string? sorting = null,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        IQueryable<CalibrationDevice> query = BuildFilteredQuery(
            context,
            filter,
            deviceType,
            isActive
        );

        query = string.IsNullOrWhiteSpace(sorting)
            ? query.OrderByDescending(x => x.CreationTime)
            : ApplySorting(query, sorting);

        return await query
            .Skip(skipCount)
            .Take(maxResultCount)
            .Include(x => x.CameraBindings)
            .Include(x => x.MotorBindings)
            .Include(x => x.ProjectorBindings)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<int> GetCountAsync(
        string? filter = null,
        CalibrationDeviceType? deviceType = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await BuildFilteredQuery(context, filter, deviceType, isActive)
            .CountAsync(cancellationToken);
    }

    // ─────────────────────────── 私有辅助方法 ───────────────────────────

    /// <summary>构建多条件过滤查询</summary>
    private static IQueryable<CalibrationDevice> BuildFilteredQuery(
        AuroraStruct3DDbContext context,
        string? filter,
        CalibrationDeviceType? deviceType,
        bool? isActive
    )
    {
        IQueryable<CalibrationDevice> query = context.CalibrationDevices;

        if (!string.IsNullOrWhiteSpace(filter))
        {
            string keyword = filter.Trim();
            query = query.Where(x =>
                x.Name.Contains(keyword)
                || (x.Description != null && x.Description.Contains(keyword))
            );
        }
        if (deviceType.HasValue)
        {
            query = query.Where(x => x.DeviceType == deviceType.Value);
        }
        if (isActive.HasValue)
        {
            query = query.Where(x => x.IsActive == isActive.Value);
        }
        return query;
    }

    /// <summary>简单字符串排序（仅支持 "字段 asc/desc" 形式，安全白名单）</summary>
    private static IQueryable<CalibrationDevice> ApplySorting(
        IQueryable<CalibrationDevice> query,
        string sorting
    )
    {
        string normalized = sorting.Trim().ToLowerInvariant();
        return normalized switch
        {
            "name" or "name asc" => query.OrderBy(x => x.Name),
            "name desc" => query.OrderByDescending(x => x.Name),
            "creationtime" or "creationtime asc" => query.OrderBy(x => x.CreationTime),
            "creationtime desc" => query.OrderByDescending(x => x.CreationTime),
            "isactive" or "isactive asc" => query.OrderBy(x => x.IsActive),
            "isactive desc" => query.OrderByDescending(x => x.IsActive),
            _ => query.OrderByDescending(x => x.CreationTime),
        };
    }
}
