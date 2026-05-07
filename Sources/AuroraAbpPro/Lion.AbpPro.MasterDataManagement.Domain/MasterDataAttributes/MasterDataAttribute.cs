namespace Lion.AbpPro.MasterDataManagement.MasterDataAttributes;

/// <summary>
/// 主数据属性
/// </summary>
public class MasterDataAttribute : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    private MasterDataAttribute() { }

    public MasterDataAttribute(
        Guid id,
        Guid masterDataTypeId,
        string name,
        string code,
        string attributeType,
        Guid? tenantId = null
    )
        : base(id)
    {
        SetMasterDataTypeId(masterDataTypeId);
        SetName(name);
        SetCode(code);
        SetAttributeType(attributeType);

        TenantId = tenantId;
    }

    public Guid? TenantId { get; private set; }

    /// <summary>
    /// 主数据类型Id
    /// </summary>
    public Guid MasterDataTypeId { get; private set; }

    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// 编码
    /// </summary>
    public string Code { get; private set; }

    /// <summary>
    /// 属性类型(string,date,long等)
    /// </summary>
    public string AttributeType { get; private set; }

    /// <summary>
    /// 设置主数据类型Id
    /// </summary>
    private void SetMasterDataTypeId(Guid masterDataTypeId)
    {
        MasterDataTypeId = masterDataTypeId;
    }

    /// <summary>
    /// 设置名称
    /// </summary>
    private void SetName(string name)
    {
        Guard.NotNullOrWhiteSpace(name, nameof(name), 128, 0);
        Name = name;
    }

    /// <summary>
    /// 设置编码
    /// </summary>
    private void SetCode(string code)
    {
        Guard.NotNullOrWhiteSpace(code, nameof(code), 128, 0);
        Code = code;
    }

    /// <summary>
    /// 设置属性类型(string,date,long等)
    /// </summary>
    private void SetAttributeType(string attributeType)
    {
        Guard.NotNullOrWhiteSpace(attributeType, nameof(attributeType), 128, 0);
        AttributeType = attributeType;
    }

    /// <summary>
    /// 更新主数据属性
    /// </summary>
    public void Update(Guid masterDataTypeId, string name, string code, string attributeType)
    {
        SetMasterDataTypeId(masterDataTypeId);
        SetName(name);
        SetCode(code);
        SetAttributeType(attributeType);
    }
}
