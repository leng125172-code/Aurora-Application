namespace Lion.AbpPro.MasterDataManagement.MasterDataValues;

/// <summary>
/// 主数据值
/// </summary>
public class MasterDataValue : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    private MasterDataValue() { }

    public MasterDataValue(
        Guid id,
        Guid masterDataId,
        Guid masterDataAttributeId,
        string value,
        Guid? tenantId = null
    )
        : base(id)
    {
        SetMasterDataId(masterDataId);
        SetMasterDataAttributeId(masterDataAttributeId);
        SetValue(value);

        TenantId = tenantId;
    }

    public Guid? TenantId { get; private set; }

    /// <summary>
    /// 主数据Id(MasterData的Id)
    /// </summary>
    public Guid MasterDataId { get; private set; }

    /// <summary>
    /// 主属性属性Id(MasterDataAttribute表的主键Id)
    /// </summary>
    public Guid MasterDataAttributeId { get; private set; }

    /// <summary>
    /// 属性值
    /// </summary>
    public string Value { get; private set; }

    /// <summary>
    /// 设置主数据Id(MasterData的Id)
    /// </summary>
    private void SetMasterDataId(Guid masterDataId)
    {
        MasterDataId = masterDataId;
    }

    /// <summary>
    /// 设置主属性属性Id(MasterDataAttribute表的主键Id)
    /// </summary>
    private void SetMasterDataAttributeId(Guid masterDataAttributeId)
    {
        MasterDataAttributeId = masterDataAttributeId;
    }

    /// <summary>
    /// 设置属性值
    /// </summary>
    private void SetValue(string value)
    {
        Value = value.IsNotNullOrWhiteSpace() ? value : string.Empty;
    }

    /// <summary>
    /// 更新主数据值
    /// </summary>
    public void Update(Guid masterDataId, Guid masterDataAttributeId, string value)
    {
        SetMasterDataId(masterDataId);
        SetMasterDataAttributeId(masterDataAttributeId);
        SetValue(value);
    }
}
