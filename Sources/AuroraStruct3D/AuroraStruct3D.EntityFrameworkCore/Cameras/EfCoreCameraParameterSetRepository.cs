using AuroraStruct3D.Cameras;
using AuroraStruct3D.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace AuroraStruct3D.Cameras;

/// <summary>
/// 相机参数集 EFCore 仓储实现
/// </summary>
public class EfCoreCameraParameterSetRepository
    : EfCoreRepository<AuroraStruct3DDbContext, CameraParameterSet, Guid>,
        ICameraParameterSetRepository
{
    public EfCoreCameraParameterSetRepository(
        IDbContextProvider<AuroraStruct3DDbContext> dbContextProvider
    )
        : base(dbContextProvider) { }

    /// <inheritdoc/>
    public async Task<List<CameraParameterSet>> GetListByCameraAsync(
        Guid cameraDeviceId,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .CameraParameterSets.AsNoTracking()
            .Include(s => s.Parameters)
            .Where(s => s.CameraDeviceId == cameraDeviceId)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<CameraParameterSet?> GetWithParametersAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .CameraParameterSets.Include(s => s.Parameters)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<CameraParameterSet?> GetDefaultAsync(
        Guid cameraDeviceId,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .CameraParameterSets.Include(s => s.Parameters)
            .FirstOrDefaultAsync(
                s => s.CameraDeviceId == cameraDeviceId && s.IsDefault,
                cancellationToken
            );
    }
}
