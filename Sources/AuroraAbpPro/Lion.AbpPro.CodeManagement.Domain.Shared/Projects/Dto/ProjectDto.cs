namespace Lion.AbpPro.CodeManagement.Projects.Dto;

public class ProjectDto : AggregateDtoBase<Guid>
{
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 负责人
    /// </summary>
    public string Owner { get; set; }

    /// <summary>
    /// 名称空间
    /// </summary>
    public string NameSpace { get; set; }

    /// <summary>
    /// 公司名称
    /// </summary>
    public string CompanyName { get; set; }

    /// <summary>
    /// 项目名称
    /// </summary>
    public string ProjectName { get; set; }

    /// <summary>
    /// 备注
    /// </summary>
    public string Remark { get; set; }

    /// <summary>
    /// 是否支持多租户
    /// </summary>
    public bool SupportTenant { get; set; }
}
