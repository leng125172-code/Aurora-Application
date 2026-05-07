using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace Lion.AbpPro.MasterDataManagement.MasterDataTypes
{
    /// <summary>
    /// 主数据类型仓储接口
    /// </summary>
    public interface IMasterDataTypeRepository : IBasicRepository<MasterDataType, Guid>
    {
        /// <summary>
        /// 获取主数据类型列表
        /// </summary>
        /// <param name="startDateTime">创建时间起始</param>
        /// <param name="endDateTime">创建时间结束</param>
        /// <param name="maxResultCount">最大返回数量</param>
        /// <param name="skipCount">跳过数量</param>
        /// <returns>主数据类型列表</returns>
        Task<List<MasterDataType>> GetListAsync(
            DateTime? startDateTime = null,
            DateTime? endDateTime = null,
            int maxResultCount = 10,
            int skipCount = 0
        );

        /// <summary>
        /// 获取主数据类型数量
        /// </summary>
        /// <param name="startDateTime">创建时间起始</param>
        /// <param name="endDateTime">创建时间结束</param>
        /// <returns>主数据类型数量</returns>
        Task<long> GetCountAsync(DateTime? startDateTime = null, DateTime? endDateTime = null);

        /// <summary>
        /// 检查主数据类型编码是否存在
        /// </summary>
        /// <param name="code">主数据类型编码</param>
        /// <param name="exceptId">排除的Id（用于更新时检查）</param>
        /// <returns>是否存在</returns>
        Task<bool> ExistsAsync(string code, Guid? exceptId = null);

        /// <summary>
        /// 检查主数据类型是否存在
        /// </summary>
        /// <param name="id">主数据类型Id</param>
        /// <returns>是否存在</returns>
        Task<bool> ExistsAsync(Guid id);

        /// <summary>
        /// 根据编码查找主数据类型
        /// </summary>
        /// <param name="code">主数据类型编码</param>
        /// <returns>主数据类型</returns>
        Task<MasterDataType> FindByCodeAsync(string code);
    }
}
