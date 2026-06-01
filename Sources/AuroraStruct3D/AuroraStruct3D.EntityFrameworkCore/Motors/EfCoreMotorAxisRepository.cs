using AuroraStruct3D.EntityFrameworkCore;
using AuroraStruct3D.Motors;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace AuroraStruct3D.Motors;

/// <summary>
/// 电机轴 EFCore 仓储实现
/// </summary>
public class EfCoreMotorAxisRepository
    : EfCoreRepository<AuroraStruct3DDbContext, MotorAxis, Guid>,
        IMotorAxisRepository
{
    public EfCoreMotorAxisRepository(IDbContextProvider<AuroraStruct3DDbContext> dbContextProvider)
        : base(dbContextProvider) { }

    /// <inheritdoc/>
    public async Task<MotorAxis?> FindBySlaveIdAsync(
        Guid serialPortConfigId,
        int slaveId,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .MotorAxes.AsNoTracking()
            .FirstOrDefaultAsync(
                m => m.SerialPortConfigId == serialPortConfigId && m.SlaveId == slaveId,
                cancellationToken
            );
    }

    /// <inheritdoc/>
    public async Task<List<MotorAxis>> GetListByPortAsync(
        Guid serialPortConfigId,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .MotorAxes.AsNoTracking()
            .Where(m => m.SerialPortConfigId == serialPortConfigId)
            .OrderBy(m => m.AxisIndex)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<int?> GetMaxAxisIndexAsync(CancellationToken cancellationToken = default)
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .MotorAxes.AsNoTracking()
            .MaxAsync(a => (int?)a.AxisIndex, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<List<MotorAxis>> GetEnabledListAsync(
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .MotorAxes.AsNoTracking()
            .Include(m => m.SerialPortConfig)
            .Where(m => m.IsEnabled)
            .OrderBy(m => m.AxisIndex)
            .ToListAsync(cancellationToken);
    }
}
