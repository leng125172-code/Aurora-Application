using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace Lion.AbpPro.MasterDataManagement.MasterDataAttributes
{
    /// <summary>
    /// 主数据属性仓储接口
    /// </summary>
    public interface IMasterDataAttributeRepository : IBasicRepository<MasterDataAttribute, Guid>
    {
        /// <summary>
        /// 根据主数据类型Id获取主数据属性列表
        /// </summary>
        /// <param name="masterDataTypeId">主数据类型Id</param>
        /// <returns>主数据属性列表</returns>
        Task<List<MasterDataAttribute>> GetListByMasterDataTypeIdAsync(Guid masterDataTypeId);

        /// <summary>
        /// 根据主数据类型编码获取主数据属性列表
        /// </summary>
        /// <param name="masterDataTypeCode">主数据类型编码</param>
        /// <returns>主数据属性列表</returns>
        Task<List<MasterDataAttribute>> GetListByMasterDataTypeCodeAsync(string masterDataTypeCode);

        /// <summary>
        /// 检查同一主数据类型下编码是否已存在
        /// </summary>
        /// <param name="masterDataTypeId">主数据类型Id</param>
        /// <param name="code">编码</param>
        /// <param name="exceptId">排除的Id（用于更新时检查）</param>
        /// <returns>是否存在</returns>
        Task<bool> ExistsAsync(Guid masterDataTypeId, string code, Guid? exceptId = null);

        /// <summary>
        /// 根据主数据类型Id删除主数据属性
        /// </summary>
        /// <param name="masterDataTypeId">主数据类型Id</param>
        Task DeleteByMasterDataTypeIdAsync(Guid masterDataTypeId);
    }
}
