namespace Lion.AbpPro.CodeManagement.DataTypes.Aggregates;

/// <summary>
/// 数据类型
/// </summary>
public class DataType : FullAuditedAggregateRoot<Guid>
{
    /// <summary>
    /// 编码
    /// </summary>
    public string Code { get; private set; }

    /// <summary>
    /// 描述
    /// </summary>
    public string Description { get; private set; }

    private DataType() { }

    public DataType(Guid id, string code, string description)
        : base(id)
    {
        SetCode(code);
        SetDescription(description);
    }

    private void SetCode(string code)
    {
        Guard.NotNullOrWhiteSpace(code, nameof(code), CodeManagementConsts.MaxLength128);
        Code = code;
    }

    private void SetDescription(string description)
    {
        Guard.NotNullOrWhiteSpace(
            description,
            nameof(description),
            CodeManagementConsts.MaxLength128
        );
        Description = description;
    }
}
