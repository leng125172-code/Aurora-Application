namespace Lion.AbpPro.MasterDataManagement.MasterDataTypes;

/// <summary>
/// 主数据类型
/// </summary>
public class MasterDataType : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    private MasterDataType() { }

    public MasterDataType(Guid id, string name, string code, Guid? tenantId = null)
        : base(id)
    {
        SetName(name);
        SetCode(code);

        TenantId = tenantId;
    }

    public Guid? TenantId { get; private set; }

    /// <summary>
    /// 名称
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// 编码
    /// </summary>
    public string Code { get; private set; }

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
    /// 更新主数据类型
    /// </summary>
    public void Update(string name, string code)
    {
        SetName(name);
        SetCode(code);
    }
}
