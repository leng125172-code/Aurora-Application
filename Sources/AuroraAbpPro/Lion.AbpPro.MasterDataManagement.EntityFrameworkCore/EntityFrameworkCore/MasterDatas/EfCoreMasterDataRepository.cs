using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lion.AbpPro.MasterDataManagement.MasterDatas;
using Microsoft.EntityFrameworkCore;

namespace Lion.AbpPro.MasterDataManagement.EntityFrameworkCore.MasterDatas
{
    public class EfCoreMasterDataRepository
        : EfCoreRepository<IMasterDataManagementDbContext, MasterData, Guid>,
            IMasterDataRepository
    {
        public EfCoreMasterDataRepository(
            IDbContextProvider<IMasterDataManagementDbContext> dbContextProvider
        )
            : base(dbContextProvider) { }

        public async Task<MasterData> GetByCodeAsync(string code)
        {
            return await (await GetDbSetAsync()).Where(e => e.Code == code).FirstOrDefaultAsync();
        }

        public async Task<bool> ExistsAsync(string code, Guid? exceptId = null)
        {
            return await (await GetDbSetAsync())
                .WhereIf(exceptId.HasValue, e => e.Id != exceptId)
                .Where(e => e.Code == code)
                .AnyAsync();
        }

        /// <summary>
        /// 根据主数据类型Id删除主数据
        /// </summary>
        /// <param name="masterDataTypeId">主数据类型Id</param>
        public async Task DeleteByMasterDataTypeIdAsync(Guid masterDataTypeId)
        {
            var dbSet = await GetDbSetAsync();
            await dbSet.Where(x => x.MasterDataTypeId == masterDataTypeId).ExecuteDeleteAsync();
        }
    }
}
