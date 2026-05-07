namespace Lion.AbpPro.MasterDataManagement.EntityFrameworkCore.MasterDataAttributes;

public class EfCoreMasterDataAttributeRepository
    : EfCoreRepository<IMasterDataManagementDbContext, MasterDataAttribute, Guid>,
        IMasterDataAttributeRepository
{
    public EfCoreMasterDataAttributeRepository(
        IDbContextProvider<IMasterDataManagementDbContext> dbContextProvider
    )
        : base(dbContextProvider) { }

    public async Task<List<MasterDataAttribute>> GetListByMasterDataTypeIdAsync(
        Guid masterDataTypeId
    )
    {
        return await (await GetDbSetAsync())
            .Where(e => e.MasterDataTypeId == masterDataTypeId)
            .OrderBy(e => e.Name)
            .ToListAsync();
    }

    public async Task<List<MasterDataAttribute>> GetListByMasterDataTypeCodeAsync(
        string masterDataTypeCode
    )
    {
        var dbContext = await GetDbContextAsync();
        return await (
            from ma in (await GetDbSetAsync())
            join mt in dbContext.MasterDataTypes on ma.MasterDataTypeId equals mt.Id
            where mt.Code == masterDataTypeCode
            orderby ma.Name
            select ma
        ).ToListAsync();
    }

    /// <summary>
    /// 检查同一主数据类型下编码是否已存在
    /// </summary>
    /// <param name="masterDataTypeId">主数据类型Id</param>
    /// <param name="code">编码</param>
    /// <param name="exceptId">排除的Id（用于更新时检查）</param>
    /// <returns></returns>
    public async Task<bool> ExistsAsync(Guid masterDataTypeId, string code, Guid? exceptId = null)
    {
        return await (await GetDbSetAsync())
            .WhereIf(exceptId.HasValue, e => e.Id != exceptId)
            .Where(e => e.MasterDataTypeId == masterDataTypeId && e.Code == code)
            .AnyAsync();
    }

    /// <summary>
    /// 根据主数据类型Id删除主数据属性
    /// </summary>
    /// <param name="masterDataTypeId">主数据类型Id</param>
    public async Task DeleteByMasterDataTypeIdAsync(Guid masterDataTypeId)
    {
        var dbSet = await GetDbSetAsync();
        await dbSet.Where(x => x.MasterDataTypeId == masterDataTypeId).ExecuteDeleteAsync();
    }
}
