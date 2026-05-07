using Lion.AbpPro.CodeManagement.DataTypes;
using Lion.AbpPro.CodeManagement.DataTypes.Aggregates;

namespace Lion.AbpPro.CodeManagement.EntityFrameworkCore.DataTypes
{
    /// <summary>
    /// 数据类型 仓储Ef core 实现
    /// </summary>
    public class EfCoreDataTypeRepository
        : EfCoreRepository<ICodeManagementDbContext, DataType, Guid>,
            IDataTypeRepository
    {
        public EfCoreDataTypeRepository(
            IDbContextProvider<ICodeManagementDbContext> dbContextProvider
        )
            : base(dbContextProvider) { }

        public async Task<DataType> FindByCodeAsync(string code)
        {
            return await (await GetDbSetAsync()).FirstOrDefaultAsync(t => t.Code == code);
        }
    }
}
