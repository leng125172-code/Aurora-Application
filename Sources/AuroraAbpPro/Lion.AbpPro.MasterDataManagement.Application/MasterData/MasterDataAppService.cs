namespace Lion.AbpPro.MasterDataManagement.MasterData;

[Authorize]
public class MasterDataAppService : MasterDataManagementAppService, IMasterDataAppService
{
    private readonly MasterDataTypeManager _masterDataTypeManager;
    private readonly MasterDataAttributeManager _masterDataAttributeManager;
    private readonly MasterDataValueManager _masterDataValueManager;
    private readonly MasterDataManager _masterDataManager;

    public MasterDataAppService(
        MasterDataTypeManager masterDataTypeManager,
        MasterDataAttributeManager masterDataAttributeManager,
        MasterDataValueManager masterDataValueManager,
        MasterDataManager masterDataManager
    )
    {
        _masterDataTypeManager = masterDataTypeManager;
        _masterDataAttributeManager = masterDataAttributeManager;
        _masterDataValueManager = masterDataValueManager;
        _masterDataManager = masterDataManager;
    }

    /// <summary>
    /// 获取所有主数据类型
    /// </summary>
    public async Task<List<MasterDataTypeOutput>> GetMasterDataTypesAsync()
    {
        var list = await _masterDataTypeManager.GetListAsync();
        return list.Adapt<List<MasterDataTypeOutput>>();
    }

    /// <summary>
    /// 创建主数据类型
    /// </summary>
    [Authorize(policy: MasterDataManagementPermissions.MasterDataManagement.Create)]
    public async Task<MasterDataTypeOutput> CreateMasterDataTypeAsync(
        CreateMasterDataTypeInput input
    )
    {
        var dto = await _masterDataTypeManager.CreateAsync(
            GuidGenerator.Create(),
            input.Name,
            input.Code
        );
        return dto.Adapt<MasterDataTypeOutput>();
    }

    /// <summary>
    /// 更新主数据类型
    /// </summary>
    [Authorize(policy: MasterDataManagementPermissions.MasterDataManagement.Update)]
    public async Task<MasterDataTypeOutput> UpdateMasterDataTypeAsync(
        UpdateMasterDataTypeInput input
    )
    {
        var dto = await _masterDataTypeManager.UpdateAsync(input.Id, input.Name, input.Code);
        return dto.Adapt<MasterDataTypeOutput>();
    }

    /// <summary>
    /// 根据编码删除主数据类型
    /// </summary>
    [Authorize(policy: MasterDataManagementPermissions.MasterDataManagement.Delete)]
    public async Task DeleteMasterDataTypeByCodeAsync(DeleteMasterDataTypeInput input)
    {
        // 删除主数据类型
        await _masterDataTypeManager.DeleteAsync(input.Id);
        var attributes = await _masterDataAttributeManager.GetListByMasterDataTypeIdAsync(input.Id);
        if (attributes?.Any() == true)
        {
            // 删除主数据属性
            await _masterDataAttributeManager.DeleteByMasterDataTypeIdAsync(input.Id);
            // 删除主数据value
            foreach (var attribute in attributes)
            {
                await _masterDataValueManager.DeleteByMasterDataAttributeIdAsync(attribute.Id);
            }
        }

        // 删除主数据
        await _masterDataManager.DeleteByMasterDataTypeIdAsync(input.Id);
    }

    /// <summary>
    /// 根据主数据类型Id获取属性列表
    /// </summary>
    public async Task<List<GetMasterDataAttributeOutput>> GetMasterDataAttributesAsync(
        GetMasterDataAttributesInput input
    )
    {
        var list = new List<GetMasterDataAttributeOutput>();
        if (input.MasterDataTypeId.HasValue)
        {
            var attributes = await _masterDataAttributeManager.GetListByMasterDataTypeIdAsync(
                input.MasterDataTypeId.Value
            );
            return attributes.Adapt<List<GetMasterDataAttributeOutput>>();
        }
        else if (input.MasterDataTypeCode.IsNotNullOrWhiteSpace())
        {
            var attributes = await _masterDataAttributeManager.GetListByMasterDataTypeCodeAsync(
                input.MasterDataTypeCode
            );
            return attributes.Adapt<List<GetMasterDataAttributeOutput>>();
        }
        else
        {
            throw new UserFriendlyException($"请选择主数据类型");
        }
    }

    /// <summary>
    /// 创建主数据属性
    /// </summary>
    [Authorize(policy: MasterDataManagementPermissions.MasterDataManagement.Create)]
    public async Task CreateMasterDataAttributeAsync(CreateMasterDataAttributeInput input)
    {
        // 检查主数据类型是否存在
        if (!await _masterDataTypeManager.ExistsAsync(input.MasterDataTypeId))
        {
            throw new UserFriendlyException($"主数据类型不存在");
        }

        await _masterDataAttributeManager.CreateAsync(
            GuidGenerator.Create(),
            input.MasterDataTypeId,
            input.Name,
            input.Code,
            input.AttributeType
        );
    }

    /// <summary>
    /// 更新主数据属性
    /// </summary>
    [Authorize(policy: MasterDataManagementPermissions.MasterDataManagement.Update)]
    public async Task UpdateMasterDataAttributeAsync(UpdateMasterDataAttributeInput input)
    {
        // 检查主数据类型是否存在
        if (!await _masterDataTypeManager.ExistsAsync(input.MasterDataTypeId))
        {
            throw new UserFriendlyException($"主数据类型不存在");
        }

        var dto = await _masterDataAttributeManager.UpdateAsync(
            input.Id,
            input.MasterDataTypeId,
            input.Name,
            input.Code,
            input.AttributeType
        );
    }

    /// <summary>
    /// 删除主数据属性
    /// </summary>
    [Authorize(policy: MasterDataManagementPermissions.MasterDataManagement.Delete)]
    public async Task DeleteMasterDataAttributeAsync(DeleteMasterDataAttributeInput input)
    {
        await _masterDataAttributeManager.DeleteAsync(input.Id);
        // 删除主数据属性的同时，需要删除MasterDataValue对应的属性值,删除谨慎操作
        await _masterDataValueManager.DeleteByMasterDataAttributeIdAsync(input.Id);
    }

    /// <summary>
    /// 获取主数据主数据信息
    /// </summary>
    public async Task<PagedResultDto<GetMasterDataInfoOutput>> GetMasterDataInfoAsync(
        GetMasterDataInfoInput input
    )
    {
        var result = new PagedResultDto<GetMasterDataInfoOutput>();

        var list = new List<MasterDataValueWithMasterDataDto>();

        if (input.MasterDataTypeId.HasValue)
        {
            list = await _masterDataValueManager.GetListWithMasterDataByMasterDataTypeIdAsync(
                input.MasterDataTypeId.Value,
                input.MasterDataCode,
                input.PageSize,
                input.SkipCount
            );
            result.TotalCount = await _masterDataValueManager.GetCountByMasterDataTypeIdAsync(
                input.MasterDataTypeId.Value,
                input.MasterDataCode
            );
        }
        else if (input.MasterDataTypeCode.IsNotNullOrWhiteSpace())
        {
            list = await _masterDataValueManager.GetListWithMasterDataByMasterDataTypeCodeAsync(
                input.MasterDataTypeCode,
                input.MasterDataCode,
                input.PageSize,
                input.SkipCount
            );
            result.TotalCount = await _masterDataValueManager.GetCountByMasterDataTypeCodeAsync(
                input.MasterDataTypeCode,
                input.MasterDataCode
            );
        }
        else
        {
            throw new UserFriendlyException($"请选择主数据类型");
        }

        var items = new List<GetMasterDataInfoOutput>();
        if (list.Any())
        {
            // 按MasterDataCode分组处理数据
            var groupedData = list.GroupBy(x => x.MasterDataCode);
            foreach (var group in groupedData)
            {
                var firstItem = group.First();
                var item = new GetMasterDataInfoOutput
                {
                    Id = firstItem.Id,
                    Code = firstItem.MasterDataCode,
                    Name = firstItem.MasterDataName,
                    Enabled = firstItem.Enabled,
                    Attributes = new List<GetMasterDataInfoAttributeOutput>(),
                };

                foreach (var dto in group)
                {
                    item.Attributes.Add(
                        new GetMasterDataInfoAttributeOutput(
                            dto.Value,
                            dto.MasterDataAttributeCode,
                            dto.MasterDataAttributeName
                        )
                    );
                }

                items.Add(item);
            }

            result.Items = items;
        }
        else
        {
            return new PagedResultDto<GetMasterDataInfoOutput>();
        }

        return result;
    }

    /// <summary>
    /// 创建主数据
    /// </summary>\
    [Authorize(policy: MasterDataManagementPermissions.MasterDataManagement.DataCreate)]
    public async Task CreateMasterDataAsync(CreateMasterDataInput input)
    {
        // 检查主数据类型是否存在
        var masterDataType = await _masterDataTypeManager.FindByCodeAsync(input.MasterDataTypeCode);
        if (masterDataType == null)
        {
            throw new UserFriendlyException($"主数据类型不存在");
        }

        var masterDataAttributes =
            await _masterDataAttributeManager.GetListByMasterDataTypeCodeAsync(
                input.MasterDataTypeCode
            );
        if (input.Attributes?.Any() == true)
        {
            var existingAttributeCodes = masterDataAttributes.Select(x => x.Code).ToHashSet();
            var invalidAttr = input.Attributes.FirstOrDefault(attr =>
                !existingAttributeCodes.Contains(attr.Key)
            );
            if (invalidAttr.Key != null)
            {
                throw new UserFriendlyException($"主数据属性 '{invalidAttr.Key}' 不存在");
            }
        }

        var dto = await _masterDataManager.CreateAsync(
            GuidGenerator.Create(),
            masterDataType.Id,
            input.Name,
            input.Code,
            true
        );

        // 处理属性值
        if (input.Attributes?.Any() == true)
        {
            await ProcessAttributesAsync(
                dto.Id,
                masterDataType.Id,
                input.Attributes,
                masterDataAttributes
            );
        }
    }

    /// <summary>
    /// 处理主数据属性值
    /// </summary>
    private async Task ProcessAttributesAsync(
        Guid masterDataId,
        Guid masterDataTypeId,
        Dictionary<string, object> attributes,
        List<MasterDataAttributeDto> masterDataAttributes
    )
    {
        // 准备要批量创建的属性值列表
        var masterDataValuesToCreate = new List<MasterDataValue>();

        foreach (var attribute in attributes)
        {
            // 查找对应的属性定义
            var masterDataAttribute = masterDataAttributes.FirstOrDefault(x =>
                x.Code == attribute.Key
            );
            if (masterDataAttribute != null)
            {
                // 创建属性值实体
                var masterDataValue = new MasterDataValue(
                    GuidGenerator.Create(),
                    masterDataId,
                    masterDataAttribute.Id,
                    attribute.Value == null ? "" : attribute.Value.ToString(),
                    CurrentTenant.Id
                );
                masterDataValuesToCreate.Add(masterDataValue);
            }
        }

        // 批量保存属性值
        if (masterDataValuesToCreate.Any())
        {
            await _masterDataValueManager.BatchCreateAsync(masterDataValuesToCreate);
        }
    }

    /// <summary>
    /// 根据编码获取主数据
    /// </summary>
    public async Task<MasterDataOutput> GetMasterDataByCodeAsync(string code)
    {
        var dto = await _masterDataManager.GetByCodeAsync(code);
        return dto.Adapt<MasterDataOutput>();
    }

    /// <summary>
    /// 更新主数据
    /// </summary>
    [Authorize(policy: MasterDataManagementPermissions.MasterDataManagement.DataUpdate)]
    public async Task UpdateMasterDataAsync(UpdateMasterDataInput input)
    {
        var masterData = await _masterDataManager.GetByIdAsync(input.Id);
        if (masterData == null)
        {
            throw new UserFriendlyException($"主数据不存在");
        }

        // 检查主数据类型是否存在
        var masterDataType = await _masterDataTypeManager.FindByCodeAsync(input.MasterDataTypeCode);
        if (masterDataType == null)
        {
            throw new UserFriendlyException($"主数据类型不存在");
        }

        var masterDataAttributes =
            await _masterDataAttributeManager.GetListByMasterDataTypeCodeAsync(
                input.MasterDataTypeCode
            );
        if (input.Attributes?.Any() == true)
        {
            var existingAttributeCodes = masterDataAttributes.Select(x => x.Code).ToHashSet();
            var invalidAttr = input.Attributes.FirstOrDefault(attr =>
                !existingAttributeCodes.Contains(attr.Key)
            );
            if (invalidAttr.Key != null)
            {
                throw new UserFriendlyException($"主数据属性 '{invalidAttr.Key}' 不存在");
            }
        }

        var dto = await _masterDataManager.UpdateAsync(
            masterData.Id,
            masterDataType.Id,
            input.Name,
            input.Code
        );

        // 处理属性值
        if (input.Attributes?.Any() == true)
        {
            await ProcessOrUpdateAttributesAsync(
                masterData.Id,
                masterDataType.Id,
                input.Attributes,
                masterDataAttributes
            );
        }
    }

    /// <summary>
    /// 处理主数据属性值的更新或新增
    /// </summary>
    private async Task ProcessOrUpdateAttributesAsync(
        Guid masterDataId,
        Guid masterDataTypeId,
        Dictionary<string, object> attributes,
        List<MasterDataAttributeDto> masterDataAttributes
    )
    {
        // 获取现有的属性值
        var existingValues = await _masterDataValueManager.GetListByMasterDataTypeIdAsync(
            masterDataTypeId
        );
        existingValues = existingValues.Where(v => v.MasterDataId == masterDataId).ToList();

        foreach (var attribute in attributes)
        {
            // 查找对应的属性定义
            var masterDataAttribute = masterDataAttributes.FirstOrDefault(x =>
                x.Code == attribute.Key
            );
            if (masterDataAttribute != null)
            {
                // 查找是否已经存在该属性值
                var existingValue = existingValues.FirstOrDefault(v =>
                    v.MasterDataAttributeId == masterDataAttribute.Id
                );

                if (existingValue != null)
                {
                    // 如果存在，则更新属性值
                    await _masterDataValueManager.UpdateAsync(
                        existingValue.Id,
                        masterDataId,
                        masterDataAttribute.Id,
                        attribute.Value == null ? string.Empty : attribute.Value.ToString()
                    );
                }
                else
                {
                    // 如果不存在，则创建新的属性值
                    await _masterDataValueManager.CreateAsync(
                        GuidGenerator.Create(),
                        masterDataId,
                        masterDataAttribute.Id,
                        attribute.Value == null ? string.Empty : attribute.Value.ToString()
                    );
                }
            }
        }
    }

    /// <summary>
    /// 设置主数据启用/禁用状态
    /// </summary>
    public async Task SetMasterDataEnabledAsync(SetMasterDataEnabledInput input)
    {
        await _masterDataManager.EnabledAsync(input.Id, input.Enabled);
    }
}
