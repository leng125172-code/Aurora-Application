using Lion.AbpPro.CodeManagement.DataTypes;
using Lion.AbpPro.CodeManagement.DataTypes.Dto;

namespace Lion.AbpPro.CodeManagement;

[Route("DataTypes")]
public class DataTypeController : CodeManagementController, IDataTypeAppService
{
    private readonly IDataTypeAppService _dataTypeAppService;

    public DataTypeController(IDataTypeAppService dataTypeAppService)
    {
        _dataTypeAppService = dataTypeAppService;
    }

    [HttpPost("List")]
    [SwaggerOperation(summary: "获取类型", Tags = new[] { "DataTypes" })]
    public Task<List<DataTypeDto>> ListAsync(GetDataTypeInput input)
    {
        return _dataTypeAppService.ListAsync(input);
    }
}
