namespace Lion.AbpPro.MasterDataManagement.MasterDatas;

/// <summary>
/// 主数据
/// </summary>
public class MasterData : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    private MasterData() { }

    public MasterData(
        Guid id,
        Guid masterDataTypeId,
        string name,
        string code,
        bool enabled,
        Guid? tenantId = null
    )
        : base(id)
    {
        SetMasterDataTypeId(masterDataTypeId);
        SetName(name);
        SetCode(code);
        SetEnabled(enabled);

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
    /// 是否启用
    /// </summary>
    public bool Enabled { get; private set; }

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
    /// 设置是否启用
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
    }

    /// <summary>
    /// 更新主数据
    /// </summary>
    public void Update(Guid masterDataTypeId, string name, string code)
    {
        SetMasterDataTypeId(masterDataTypeId);
        SetName(name);
        SetCode(code);
    }
}
