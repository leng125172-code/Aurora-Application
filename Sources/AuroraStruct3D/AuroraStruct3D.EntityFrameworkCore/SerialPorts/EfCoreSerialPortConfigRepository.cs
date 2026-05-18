using AuroraStruct3D.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace AuroraStruct3D.SerialPorts;

/// <summary>
/// 串口通讯配置 EFCore 仓储实现
/// </summary>
public class EfCoreSerialPortConfigRepository
    : EfCoreRepository<AuroraStruct3DDbContext, SerialPortConfig, Guid>,
        ISerialPortConfigRepository
{
    public EfCoreSerialPortConfigRepository(
        IDbContextProvider<AuroraStruct3DDbContext> dbContextProvider
    )
        : base(dbContextProvider) { }

    /// <inheritdoc/>
    public async Task<SerialPortConfig?> FindByPortNameAsync(
        string portName,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .SerialPortConfigs.AsNoTracking()
            .FirstOrDefaultAsync(s => s.PortName == portName, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<List<SerialPortConfig>> GetListAsync(
        bool? isEnabled = null,
        CancellationToken cancellationToken = default
    )
    {
        AuroraStruct3DDbContext context = await GetDbContextAsync();
        return await context
            .SerialPortConfigs.AsNoTracking()
            .WhereIf(isEnabled.HasValue, s => s.IsEnabled == isEnabled!.Value)
            .OrderBy(s => s.DisplayName)
            .ToListAsync(cancellationToken);
    }
}
