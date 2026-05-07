namespace Lion.AbpPro.MasterDataManagement.EntityFrameworkCore.MasterDataValues;

/// <summary>
/// 主数据值 仓储Ef core 实现
/// </summary>
public class EfCoreMasterDataValueRepository
    : EfCoreRepository<IMasterDataManagementDbContext, MasterDataValue, Guid>,
        IMasterDataValueRepository
{
    public EfCoreMasterDataValueRepository(
        IDbContextProvider<IMasterDataManagementDbContext> dbContextProvider
    )
        : base(dbContextProvider) { }

    public async Task<List<MasterDataValue>> GetListByMasterDataTypeIdAsync(
        Guid masterDataTypeId,
        string masterDataCode = null,
        int maxResultCount = 10,
        int skipCount = 0
    )
    {
        var dbContext = await GetDbContextAsync();
        var query =
            from mdv in await GetDbSetAsync()
            join md in dbContext.MasterDatas on mdv.MasterDataId equals md.Id
            where
                md.MasterDataTypeId == masterDataTypeId
                && (masterDataCode == null || md.Code.Contains(masterDataCode))
            orderby mdv.CreationTime descending
            select mdv;

        return await query.PageBy(skipCount, maxResultCount).ToListAsync();
    }

    public async Task<long> GetCountByMasterDataTypeIdAsync(
        Guid masterDataTypeId,
        string masterDataCode = null
    )
    {
        var dbContext = await GetDbContextAsync();
        var query =
            from md in dbContext.MasterDatas
            where
                md.MasterDataTypeId == masterDataTypeId
                && (masterDataCode == null || md.Code.Contains(masterDataCode))
            orderby md.CreationTime descending
            select md;

        return await query.LongCountAsync();
    }

    public async Task<long> GetCountByMasterDataTypeCodeAsync(
        string masterDataTypeCode,
        string masterDataCode = null
    )
    {
        var dbContext = await GetDbContextAsync();
        // 先获取主数据分页列表
        var query =
            from md in dbContext.MasterDatas
            join mdt in dbContext.MasterDataTypes on md.MasterDataTypeId equals mdt.Id
            where
                mdt.Code == masterDataTypeCode
                && (masterDataCode == null || md.Code.Contains(masterDataCode))
            orderby md.CreationTime descending
            select md;

        return await query.LongCountAsync();
    }

    public async Task<
        List<MasterDataValueWithMasterDataDto>
    > GetListWithMasterDataByMasterDataTypeIdAsync(
        Guid masterDataTypeId,
        string masterDataCode = null,
        int maxResultCount = 10,
        int skipCount = 0
    )
    {
        var dbContext = await GetDbContextAsync();

        // 先获取主数据分页列表
        var masterDataQuery =
            from md in dbContext.MasterDatas
            where
                md.MasterDataTypeId == masterDataTypeId
                && (masterDataCode == null || md.Code.Contains(masterDataCode))
            orderby md.CreationTime descending
            select md;

        var pagedMasterData = await masterDataQuery.PageBy(skipCount, maxResultCount).ToListAsync();

        if (!pagedMasterData.Any())
            return new List<MasterDataValueWithMasterDataDto>();

        // 获取这些主数据的所有属性值
        var masterDataIds = pagedMasterData.Select(md => md.Id).ToList();
        var query =
            from md in dbContext.MasterDatas
            join mdv in await GetDbSetAsync() on md.Id equals mdv.MasterDataId into mdvGroup
            from mdv in mdvGroup.DefaultIfEmpty()
            join mda in dbContext.MasterDataAttributes
                on mdv.MasterDataAttributeId equals mda.Id
                into mdaGroup
            from mda in mdaGroup.DefaultIfEmpty()
            where masterDataIds.Contains(md.Id)
            orderby md.CreationTime descending, mdv.CreationTime descending
            select new MasterDataValueWithMasterDataDto
            {
                Id = md.Id,
                MasterDataId = md.Id,
                MasterDataAttributeId = mdv != null ? mdv.MasterDataAttributeId : Guid.Empty,
                Value = mdv != null ? mdv.Value : null,
                MasterDataCode = md.Code,
                MasterDataName = md.Name,
                MasterDataAttributeCode = mda != null ? mda.Code : null,
                MasterDataAttributeName = mda != null ? mda.Name : null,
                Enabled = md.Enabled,
            };
        var dataValue = await query.ToListAsync();
        return dataValue;
    }

    public async Task<
        List<MasterDataValueWithMasterDataDto>
    > GetListWithMasterDataByMasterDataTypeCodeAsync(
        string masterDataTypeCode,
        string masterDataCode = null,
        int maxResultCount = 10,
        int skipCount = 0
    )
    {
        var dbContext = await GetDbContextAsync();

        // 使用LEFT JOIN确保即使主数据没有值也会被包含
        var query =
            from md in dbContext.MasterDatas
            join mdt in dbContext.MasterDataTypes on md.MasterDataTypeId equals mdt.Id
            join mdv in await GetDbSetAsync() on md.Id equals mdv.MasterDataId into mdvGroup
            from mdv in mdvGroup.DefaultIfEmpty()
            join mda in dbContext.MasterDataAttributes
                on mdv.MasterDataAttributeId equals mda.Id
                into mdaGroup
            from mda in mdaGroup.DefaultIfEmpty()
            where
                mdt.Code == masterDataTypeCode
                && (masterDataCode == null || md.Code.Contains(masterDataCode))
            orderby md.CreationTime descending, mdv.CreationTime descending
            select new MasterDataValueWithMasterDataDto
            {
                Id = md.Id,
                MasterDataId = mdv != null ? mdv.MasterDataId : md.Id,
                MasterDataAttributeId = mdv != null ? mdv.MasterDataAttributeId : Guid.Empty,
                Value = mdv != null ? mdv.Value : string.Empty,
                MasterDataCode = md.Code,
                MasterDataName = md.Name,
                MasterDataAttributeCode = mda != null ? mda.Code : string.Empty,
                MasterDataAttributeName = mda != null ? mda.Name : string.Empty,
                Enabled = md.Enabled,
            };

        return await query.PageBy(skipCount, maxResultCount).ToListAsync();
    }

    /// <summary>
    /// 根据主数据属性Id删除主数据值
    /// </summary>
    public async Task DeleteByMasterDataAttributeIdAsync(Guid masterDataAttributeId)
    {
        var dbSet = await GetDbSetAsync();
        await dbSet
            .Where(x => x.MasterDataAttributeId == masterDataAttributeId)
            .ExecuteDeleteAsync();
    }

    /// <summary>
    /// 根据主数据类型ID分页获取主数据及其属性值（基于主数据表分页）
    /// </summary>
    public async Task<
        List<MasterDataValueWithMasterDataDto>
    > GetListWithMasterDataByMasterDataTypeIdPagedByMasterDataAsync(
        Guid masterDataTypeId,
        string masterDataCode = null,
        int maxResultCount = 10,
        int skipCount = 0
    )
    {
        var dbContext = await GetDbContextAsync();

        // 先获取主数据分页列表
        var masterDataQuery =
            from md in dbContext.MasterDatas
            where
                md.MasterDataTypeId == masterDataTypeId
                && md.Enabled
                && (masterDataCode == null || md.Code.Contains(masterDataCode))
            orderby md.CreationTime descending
            select md;

        var pagedMasterData = await masterDataQuery.PageBy(skipCount, maxResultCount).ToListAsync();

        if (!pagedMasterData.Any())
            return new List<MasterDataValueWithMasterDataDto>();

        // 获取这些主数据的所有属性值
        var masterDataIds = pagedMasterData.Select(md => md.Id).ToList();
        var query =
            from mdv in await GetDbSetAsync()
            join md in dbContext.MasterDatas on mdv.MasterDataId equals md.Id
            join mda in dbContext.MasterDataAttributes on mdv.MasterDataAttributeId equals mda.Id
            where masterDataIds.Contains(md.Id)
            orderby mdv.CreationTime descending
            select new MasterDataValueWithMasterDataDto
            {
                Id = mdv.Id,
                MasterDataId = mdv.MasterDataId,
                MasterDataAttributeId = mdv.MasterDataAttributeId,
                Value = mdv.Value,
                MasterDataCode = md.Code,
                MasterDataName = md.Name,
                MasterDataAttributeCode = mda.Code,
                MasterDataAttributeName = mda.Name,
            };

        return await query.ToListAsync();
    }

    /// <summary>
    /// 根据主数据类型编码分页获取主数据及其属性值（基于主数据表分页）
    /// </summary>
    public async Task<
        List<MasterDataValueWithMasterDataDto>
    > GetListWithMasterDataByMasterDataTypeCodePagedByMasterDataAsync(
        string masterDataTypeCode,
        string masterDataCode = null,
        int maxResultCount = 10,
        int skipCount = 0
    )
    {
        var dbContext = await GetDbContextAsync();

        // 先获取主数据分页列表
        var masterDataQuery =
            from md in dbContext.MasterDatas
            join mdt in dbContext.MasterDataTypes on md.MasterDataTypeId equals mdt.Id
            where
                mdt.Code == masterDataTypeCode
                && md.Enabled
                && (masterDataCode == null || md.Code.Contains(masterDataCode))
            orderby md.CreationTime descending
            select md;

        var pagedMasterData = await masterDataQuery.PageBy(skipCount, maxResultCount).ToListAsync();

        if (!pagedMasterData.Any())
            return new List<MasterDataValueWithMasterDataDto>();

        // 获取这些主数据的所有属性值
        var masterDataIds = pagedMasterData.Select(md => md.Id).ToList();
        var query =
            from mdv in await GetDbSetAsync()
            join md in dbContext.MasterDatas on mdv.MasterDataId equals md.Id
            join mda in dbContext.MasterDataAttributes on mdv.MasterDataAttributeId equals mda.Id
            where masterDataIds.Contains(md.Id)
            orderby mdv.CreationTime descending
            select new MasterDataValueWithMasterDataDto
            {
                Id = mdv.Id,
                MasterDataId = mdv.MasterDataId,
                MasterDataAttributeId = mdv.MasterDataAttributeId,
                Value = mdv.Value,
                MasterDataCode = md.Code,
                MasterDataName = md.Name,
                MasterDataAttributeCode = mda.Code,
                MasterDataAttributeName = mda.Name,
            };

        return await query.ToListAsync();
    }
}
