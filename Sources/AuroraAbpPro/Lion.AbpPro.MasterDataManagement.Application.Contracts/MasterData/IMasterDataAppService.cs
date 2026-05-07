using System.Collections.Generic;
using System.Threading.Tasks;
using Lion.AbpPro.MasterDataManagement.MasterDataValues;

namespace Lion.AbpPro.MasterDataManagement.MasterData;

/// <summary>
/// 主数据
/// </summary>
public interface IMasterDataAppService : IApplicationService
{
    /// <summary>
    /// 获取所有主数据类型
    /// </summary>
    /// <returns></returns>
    Task<List<MasterDataTypeOutput>> GetMasterDataTypesAsync();

    /// <summary>
    /// 创建主数据类型
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    Task<MasterDataTypeOutput> CreateMasterDataTypeAsync(CreateMasterDataTypeInput input);

    /// <summary>
    /// 更新主数据类型
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    Task<MasterDataTypeOutput> UpdateMasterDataTypeAsync(UpdateMasterDataTypeInput input);

    /// <summary>
    /// 根据主数据类型Id获取属性列表
    /// </summary>
    Task<List<GetMasterDataAttributeOutput>> GetMasterDataAttributesAsync(
        GetMasterDataAttributesInput input
    );

    /// <summary>
    /// 创建主数据属性
    /// </summary>
    Task CreateMasterDataAttributeAsync(CreateMasterDataAttributeInput input);

    /// <summary>
    /// 更新主数据属性
    /// </summary>
    Task UpdateMasterDataAttributeAsync(UpdateMasterDataAttributeInput input);

    /// <summary>
    /// 删除主数据属性
    /// </summary>
    Task DeleteMasterDataAttributeAsync(DeleteMasterDataAttributeInput input);

    /// <summary>
    /// 根据主数据类型Id获取主数据主数据信息
    /// </summary>
    Task<PagedResultDto<GetMasterDataInfoOutput>> GetMasterDataInfoAsync(
        GetMasterDataInfoInput input
    );

    /// <summary>
    /// 创建主数据
    /// </summary>
    Task CreateMasterDataAsync(CreateMasterDataInput input);

    /// <summary>
    /// 更新主数据
    /// </summary>
    Task UpdateMasterDataAsync(UpdateMasterDataInput input);

    /// <summary>
    /// 设置主数据启用/禁用状态
    /// </summary>
    Task SetMasterDataEnabledAsync(SetMasterDataEnabledInput input);

    /// <summary>
    /// 根据编码删除主数据类型
    /// </summary>
    Task DeleteMasterDataTypeByCodeAsync(DeleteMasterDataTypeInput input);
}
