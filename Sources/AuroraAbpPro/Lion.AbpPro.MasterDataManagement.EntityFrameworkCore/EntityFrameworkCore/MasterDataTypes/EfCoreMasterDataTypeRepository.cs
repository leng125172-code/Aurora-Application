namespace Lion.AbpPro.MasterDataManagement.EntityFrameworkCore.MasterDataTypes;

public class EfCoreMasterDataTypeRepository
    : EfCoreRepository<IMasterDataManagementDbContext, MasterDataType, Guid>,
        IMasterDataTypeRepository
{
    public EfCoreMasterDataTypeRepository(
        IDbContextProvider<IMasterDataManagementDbContext> dbContextProvider
    )
        : base(dbContextProvider) { }

    public async Task<List<MasterDataType>> GetListAsync(
        DateTime? startDateTime = null,
        DateTime? endDateTime = null,
        int maxResultCount = 10,
        int skipCount = 0
    )
    {
        return await (await GetDbSetAsync())
            .WhereIf(startDateTime.HasValue, e => e.CreationTime >= startDateTime.Value)
            .WhereIf(endDateTime.HasValue, e => e.CreationTime <= endDateTime.Value)
            .OrderByDescending(e => e.Name)
            .PageBy(skipCount, maxResultCount)
            .ToListAsync();
    }

    public async Task<long> GetCountAsync(
        DateTime? startDateTime = null,
        DateTime? endDateTime = null
    )
    {
        return await (await GetDbSetAsync())
            .WhereIf(startDateTime.HasValue, e => e.CreationTime >= startDateTime.Value)
            .WhereIf(endDateTime.HasValue, e => e.CreationTime <= endDateTime.Value)
            .CountAsync();
    }

    public async Task<bool> ExistsAsync(string code, Guid? exceptId = null)
    {
        return await (await GetDbSetAsync())
            .WhereIf(exceptId.HasValue, e => e.Id != exceptId)
            .AnyAsync(x => x.Code == code);
    }

    public async Task<bool> ExistsAsync(Guid id)
    {
        return await (await GetDbSetAsync()).AnyAsync(x => x.Id == id);
    }

    public async Task<MasterDataType> FindByCodeAsync(string code)
    {
        return await (await GetDbSetAsync()).Where(x => x.Code == code).FirstOrDefaultAsync();
    }
}
