namespace Lion.AbpPro.CodeManagement.DataTypes;

/// <summary>
/// 实体 仓储接口
/// </summary>
public interface IDataTypeRepository : IBasicRepository<DataTypes.Aggregates.DataType, Guid>
{
    Task<DataTypes.Aggregates.DataType> FindByCodeAsync(string code);
}
