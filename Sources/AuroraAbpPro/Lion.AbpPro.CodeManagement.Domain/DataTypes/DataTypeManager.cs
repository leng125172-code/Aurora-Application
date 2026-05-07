namespace Lion.AbpPro.CodeManagement.DataTypes;

public class DataTypeManager : CodeManagementDomainService
{
    private readonly IDataTypeRepository _dataTypeRepository;

    public DataTypeManager(IDataTypeRepository dataTypeRepository)
    {
        _dataTypeRepository = dataTypeRepository;
    }

    public async Task<List<DataTypeDto>> ListAsync()
    {
        var list = await _dataTypeRepository.GetListAsync();
        return list.Adapt<List<DataTypeDto>>();
    }
}
