using Lion.AbpPro.MasterDataManagement.MasterDataValues;

namespace Lion.AbpPro.MasterDataManagement.MasterData;

[Route("MasterData")]
public class MasterDataController : MasterDataManagementController, IMasterDataAppService
{
    private readonly IMasterDataAppService _masterDataAppService;

    public MasterDataController(IMasterDataAppService masterDataAppService)
    {
        _masterDataAppService = masterDataAppService;
    }

    /// <summary>
    /// 获取所有主数据类型
    /// </summary>
    [HttpPost("GetMasterDataTypes")]
    public async Task<List<MasterDataTypeOutput>> GetMasterDataTypesAsync()
    {
        return await _masterDataAppService.GetMasterDataTypesAsync();
    }

    /// <summary>
    /// 创建主数据类型
    /// </summary>
    [HttpPost("CreateMasterDataType")]
    public async Task<MasterDataTypeOutput> CreateMasterDataTypeAsync(
        CreateMasterDataTypeInput input
    )
    {
        return await _masterDataAppService.CreateMasterDataTypeAsync(input);
    }

    /// <summary>
    /// 更新主数据类型
    /// </summary>
    [HttpPost("UpdateMasterDataType")]
    public async Task<MasterDataTypeOutput> UpdateMasterDataTypeAsync(
        UpdateMasterDataTypeInput input
    )
    {
        return await _masterDataAppService.UpdateMasterDataTypeAsync(input);
    }

    /// <summary>
    /// 根据编码删除主数据类型
    /// </summary>
    [HttpPost("DeleteMasterDataType")]
    public async Task DeleteMasterDataTypeByCodeAsync(DeleteMasterDataTypeInput input)
    {
        await _masterDataAppService.DeleteMasterDataTypeByCodeAsync(input);
    }

    /// <summary>
    /// 获取主数据属性列表
    /// </summary>
    [HttpPost("GetMasterDataAttributes")]
    public async Task<List<GetMasterDataAttributeOutput>> GetMasterDataAttributesAsync(
        GetMasterDataAttributesInput input
    )
    {
        return await _masterDataAppService.GetMasterDataAttributesAsync(input);
    }

    /// <summary>
    /// 创建主数据属性
    /// </summary>
    [HttpPost("CreateMasterDataAttribute")]
    public async Task CreateMasterDataAttributeAsync(CreateMasterDataAttributeInput input)
    {
        await _masterDataAppService.CreateMasterDataAttributeAsync(input);
    }

    /// <summary>
    /// 更新主数据属性
    /// </summary>
    [HttpPost("UpdateMasterDataAttribute")]
    public async Task UpdateMasterDataAttributeAsync(UpdateMasterDataAttributeInput input)
    {
        await _masterDataAppService.UpdateMasterDataAttributeAsync(input);
    }

    /// <summary>
    /// 删除主数据属性
    /// </summary>
    [HttpPost("DeleteMasterDataAttribute")]
    public async Task DeleteMasterDataAttributeAsync(DeleteMasterDataAttributeInput input)
    {
        await _masterDataAppService.DeleteMasterDataAttributeAsync(input);
    }

    /// <summary>
    /// 获取主数据信息
    /// </summary>
    [HttpPost("GetMasterDataInfo")]
    public async Task<PagedResultDto<GetMasterDataInfoOutput>> GetMasterDataInfoAsync(
        GetMasterDataInfoInput input
    )
    {
        return await _masterDataAppService.GetMasterDataInfoAsync(input);
    }

    /// <summary>
    /// 创建主数据
    /// </summary>
    [HttpPost("CreateMasterData")]
    public async Task CreateMasterDataAsync(CreateMasterDataInput input)
    {
        await _masterDataAppService.CreateMasterDataAsync(input);
    }

    /// <summary>
    /// 更新主数据
    /// </summary>
    [HttpPost("UpdateMasterData")]
    public async Task UpdateMasterDataAsync(UpdateMasterDataInput input)
    {
        await _masterDataAppService.UpdateMasterDataAsync(input);
    }

    /// <summary>
    /// 设置主数据启用/禁用状态
    /// </summary>
    [HttpPost("SetMasterDataStatus")]
    public async Task SetMasterDataEnabledAsync(SetMasterDataEnabledInput input)
    {
        await _masterDataAppService.SetMasterDataEnabledAsync(input);
    }
}
