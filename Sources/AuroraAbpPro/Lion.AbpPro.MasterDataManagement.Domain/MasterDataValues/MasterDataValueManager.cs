using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Domain.Services;
using Volo.Abp.ObjectMapping;

namespace Lion.AbpPro.MasterDataManagement.MasterDataValues;

public class MasterDataValueManager : DomainService
{
    private readonly IMasterDataValueRepository _masterDataValueRepository;
    private readonly IObjectMapper _objectMapper;
    private readonly ICurrentTenant _currentTenant;

    public MasterDataValueManager(
        IMasterDataValueRepository masterDataValueRepository,
        IObjectMapper objectMapper,
        ICurrentTenant currentTenant
    )
    {
        _masterDataValueRepository = masterDataValueRepository;
        _objectMapper = objectMapper;
        _currentTenant = currentTenant;
    }

    public async Task<List<MasterDataValueDto>> GetListByMasterDataTypeIdAsync(
        Guid masterDataTypeId,
        string masterDataCode = null,
        int maxResultCount = 10,
        int skipCount = 0
    )
    {
        var list = await _masterDataValueRepository.GetListByMasterDataTypeIdAsync(
            masterDataTypeId,
            masterDataCode,
            maxResultCount,
            skipCount
        );
        return list.Adapt<List<MasterDataValueDto>>();
    }

    public async Task<long> GetCountByMasterDataTypeIdAsync(
        Guid masterDataTypeId,
        string masterDataCode = null
    )
    {
        return await _masterDataValueRepository.GetCountByMasterDataTypeIdAsync(
            masterDataTypeId,
            masterDataCode
        );
    }

    public async Task<long> GetCountByMasterDataTypeCodeAsync(
        string masterDataTypeCode,
        string masterDataCode = null
    )
    {
        return await _masterDataValueRepository.GetCountByMasterDataTypeCodeAsync(
            masterDataTypeCode,
            masterDataCode
        );
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
        return await _masterDataValueRepository.GetListWithMasterDataByMasterDataTypeIdAsync(
            masterDataTypeId,
            masterDataCode,
            maxResultCount,
            skipCount
        );
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
        return await _masterDataValueRepository.GetListWithMasterDataByMasterDataTypeCodeAsync(
            masterDataTypeCode,
            masterDataCode,
            maxResultCount,
            skipCount
        );
    }

    /// <summary>
    /// 创建主数据值
    /// </summary>
    public async Task<MasterDataValueDto> CreateAsync(
        Guid id,
        Guid masterDataId,
        Guid masterDataAttributeId,
        string value
    )
    {
        var entity = new MasterDataValue(
            id,
            masterDataId,
            masterDataAttributeId,
            value,
            _currentTenant.Id
        );
        entity = await _masterDataValueRepository.InsertAsync(entity);
        return entity.Adapt<MasterDataValueDto>();
    }

    /// <summary>
    /// 更新主数据值
    /// </summary>
    public async Task<MasterDataValueDto> UpdateAsync(
        Guid id,
        Guid masterDataId,
        Guid masterDataAttributeId,
        string value
    )
    {
        var entity = await _masterDataValueRepository.FindAsync(id);
        if (entity == null)
            throw new MasterDataManagementException(
                MasterDataManagementErrorCodes.MasterDataCodeExists
            );
        entity.Update(masterDataId, masterDataAttributeId, value);
        entity = await _masterDataValueRepository.UpdateAsync(entity);
        return entity.Adapt<MasterDataValueDto>();
    }

    /// <summary>
    /// 批量创建主数据值
    /// </summary>
    public async Task BatchCreateAsync(List<MasterDataValue> masterDataValues)
    {
        var entities = new List<MasterDataValue>();

        foreach (var entity in masterDataValues)
        {
            var newEntity = new MasterDataValue(
                entity.Id,
                entity.MasterDataId,
                entity.MasterDataAttributeId,
                entity.Value,
                _currentTenant.Id
            );
            entities.Add(newEntity);
        }

        await _masterDataValueRepository.InsertManyAsync(entities);
    }

    /// <summary>
    /// 删除主数据值
    /// </summary>
    public async Task DeleteAsync(Guid id)
    {
        var entity = await _masterDataValueRepository.FindAsync(id);
        if (entity == null)
            throw new MasterDataManagementException(
                MasterDataManagementErrorCodes.MasterDataCodeExists
            );
        await _masterDataValueRepository.DeleteAsync(entity);
    }

    /// <summary>
    /// 根据主数据属性Id删除主数据值
    /// </summary>
    public async Task DeleteByMasterDataAttributeIdAsync(Guid masterDataAttributeId)
    {
        await _masterDataValueRepository.DeleteByMasterDataAttributeIdAsync(masterDataAttributeId);
    }
}
