using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace Lion.AbpPro.MasterDataManagement.MasterDatas
{
    /// <summary>
    /// 主数据仓储接口
    /// </summary>
    public interface IMasterDataRepository : IBasicRepository<MasterData, Guid>
    {
        /// <summary>
        /// 根据编码获取主数据
        /// </summary>
        /// <param name="code">主数据编码</param>
        /// <returns>主数据</returns>
        Task<MasterData> GetByCodeAsync(string code);

        /// <summary>
        /// 检查主数据编码是否存在
        /// </summary>
        /// <param name="code">主数据编码</param>
        /// <param name="exceptId">排除的Id（用于更新时检查）</param>
        /// <returns>是否存在</returns>
        Task<bool> ExistsAsync(string code, Guid? exceptId = null);

        /// <summary>
        /// 根据主数据类型Id删除主数据
        /// </summary>
        /// <param name="masterDataTypeId">主数据类型Id</param>
        Task DeleteByMasterDataTypeIdAsync(Guid masterDataTypeId);
    }
}
