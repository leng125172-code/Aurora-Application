using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace Lion.AbpPro.MasterDataManagement.MasterDataValues;

/// <summary>
/// 主数据值仓储接口
/// </summary>
public interface IMasterDataValueRepository : IBasicRepository<MasterDataValue, Guid>
{
    /// <summary>
    /// 根据主数据类型Id获取主数据值列表
    /// </summary>
    /// <param name="masterDataTypeId">主数据类型Id</param>
    /// <param name="masterDataCode">主数据编码</param>
    /// <param name="maxResultCount">最大返回数量</param>
    /// <param name="skipCount">跳过数量</param>
    /// <returns>主数据值列表</returns>
    Task<List<MasterDataValue>> GetListByMasterDataTypeIdAsync(
        Guid masterDataTypeId,
        string masterDataCode = null,
        int maxResultCount = 10,
        int skipCount = 0
    );

    /// <summary>
    /// 根据主数据类型Id获取主数据值总数
    /// </summary>
    /// <param name="masterDataTypeId">主数据类型Id</param>
    /// <param name="masterDataCode">主数据编码</param>
    /// <returns>主数据值总数</returns>
    Task<long> GetCountByMasterDataTypeIdAsync(Guid masterDataTypeId, string masterDataCode = null);

    /// <summary>
    /// 根据主数据类型编码获取主数据值总数
    /// </summary>
    /// <param name="masterDataTypeCode">主数据类型编码</param>
    /// <param name="masterDataCode">主数据编码</param>
    /// <returns>主数据值总数</returns>
    Task<long> GetCountByMasterDataTypeCodeAsync(
        string masterDataTypeCode,
        string masterDataCode = null
    );

    /// <summary>
    /// 根据主数据类型Id获取主数据值及主数据信息列表
    /// </summary>
    /// <param name="masterDataTypeId">主数据类型Id</param>
    /// <param name="masterDataCode">主数据编码</param>
    /// <param name="maxResultCount">最大返回数量</param>
    /// <param name="skipCount">跳过数量</param>
    /// <returns>主数据值及主数据信息列表</returns>
    Task<List<MasterDataValueWithMasterDataDto>> GetListWithMasterDataByMasterDataTypeIdAsync(
        Guid masterDataTypeId,
        string masterDataCode = null,
        int maxResultCount = 10,
        int skipCount = 0
    );

    /// <summary>
    /// 根据主数据类型编码获取主数据值及主数据信息列表
    /// </summary>
    /// <param name="masterDataTypeCode">主数据类型编码</param>
    /// <param name="masterDataCode">主数据编码</param>
    /// <param name="maxResultCount">最大返回数量</param>
    /// <param name="skipCount">跳过数量</param>
    /// <returns>主数据值及主数据信息列表</returns>
    Task<List<MasterDataValueWithMasterDataDto>> GetListWithMasterDataByMasterDataTypeCodeAsync(
        string masterDataTypeCode,
        string masterDataCode = null,
        int maxResultCount = 10,
        int skipCount = 0
    );

    /// <summary>
    /// 根据主数据属性Id删除主数据值
    /// </summary>
    /// <param name="masterDataAttributeId">主数据属性Id</param>
    Task DeleteByMasterDataAttributeIdAsync(Guid masterDataAttributeId);
}
