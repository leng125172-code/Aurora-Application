namespace Lion.AbpPro.MasterDataManagement.MasterDataAttributes;

public class MasterDataAttributeManager : DomainService
{
    private readonly IMasterDataAttributeRepository _masterDataAttributeRepository;
    private readonly IObjectMapper _objectMapper;
    private readonly ICurrentTenant _currentTenant;

    public MasterDataAttributeManager(
        IMasterDataAttributeRepository masterDataAttributeRepository,
        IObjectMapper objectMapper,
        ICurrentTenant currentTenant
    )
    {
        _masterDataAttributeRepository = masterDataAttributeRepository;
        _objectMapper = objectMapper;
        _currentTenant = currentTenant;
    }

    /// <summary>
    /// 根据主数据类型Id获取属性列表
    /// </summary>
    public async Task<List<MasterDataAttributeDto>> GetListByMasterDataTypeIdAsync(
        Guid masterDataTypeId
    )
    {
        var list = await _masterDataAttributeRepository.GetListByMasterDataTypeIdAsync(
            masterDataTypeId
        );
        return list.Adapt<List<MasterDataAttributeDto>>();
    }

    /// <summary>
    /// 根据主数据类型编码获取属性列表
    /// </summary>
    public async Task<List<MasterDataAttributeDto>> GetListByMasterDataTypeCodeAsync(
        string masterDataTypeCode
    )
    {
        var list = await _masterDataAttributeRepository.GetListByMasterDataTypeCodeAsync(
            masterDataTypeCode
        );
        return list.Adapt<List<MasterDataAttributeDto>>();
    }

    /// <summary>
    /// 创建主数据属性
    /// </summary>
    public async Task<MasterDataAttributeDto> CreateAsync(
        Guid id,
        Guid masterDataTypeId,
        string name,
        string code,
        string attributeType
    )
    {
        // 检查同一主数据类型下编码是否已存在
        if (await _masterDataAttributeRepository.ExistsAsync(masterDataTypeId, code))
        {
            throw new MasterDataManagementException(
                MasterDataManagementErrorCodes.MasterDataAttributeCodeExists
            );
        }

        var entity = new MasterDataAttribute(
            id,
            masterDataTypeId,
            name,
            code,
            attributeType,
            _currentTenant.Id
        );
        entity = await _masterDataAttributeRepository.InsertAsync(entity);
        return entity.Adapt<MasterDataAttributeDto>();
    }

    /// <summary>
    /// 更新主数据属性
    /// </summary>
    public async Task<MasterDataAttributeDto> UpdateAsync(
        Guid id,
        Guid masterDataTypeId,
        string name,
        string code,
        string attributeType
    )
    {
        // 检查同一主数据类型下编码是否已存在（排除当前记录）
        if (await _masterDataAttributeRepository.ExistsAsync(masterDataTypeId, code, id))
        {
            throw new MasterDataManagementException(
                MasterDataManagementErrorCodes.MasterDataAttributeCodeExists
            );
        }

        var entity = await _masterDataAttributeRepository.FindAsync(id);
        if (entity == null)
            throw new MasterDataManagementException(
                MasterDataManagementErrorCodes.MasterDataAttributeCodeExists
            );
        entity.Update(masterDataTypeId, name, code, attributeType);
        entity = await _masterDataAttributeRepository.UpdateAsync(entity);
        return entity.Adapt<MasterDataAttributeDto>();
    }

    /// <summary>
    /// 删除主数据属性
    /// </summary>
    public async Task DeleteAsync(Guid id)
    {
        var entity = await _masterDataAttributeRepository.FindAsync(id);
        if (entity == null)
            throw new MasterDataManagementException(
                MasterDataManagementErrorCodes.MasterDataAttributeCodeExists
            );
        await _masterDataAttributeRepository.DeleteAsync(entity);
    }

    /// <summary>
    /// 根据主数据类型Id删除主数据属性
    /// </summary>
    public async Task DeleteByMasterDataTypeIdAsync(Guid masterDataTypeId)
    {
        await _masterDataAttributeRepository.DeleteByMasterDataTypeIdAsync(masterDataTypeId);
    }

    /// <summary>
    /// 获取主数据属性
    /// </summary>
    public async Task<MasterDataAttributeDto> GetAsync(Guid id)
    {
        var entity = await _masterDataAttributeRepository.FindAsync(id);
        if (entity == null)
            throw new MasterDataManagementException(
                MasterDataManagementErrorCodes.MasterDataAttributeCodeExists
            );
        return entity.Adapt<MasterDataAttributeDto>();
    }
}
