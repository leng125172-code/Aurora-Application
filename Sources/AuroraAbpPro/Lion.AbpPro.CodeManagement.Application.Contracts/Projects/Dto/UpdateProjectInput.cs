namespace Lion.AbpPro.CodeManagement.Projects.Dto;

public class UpdateProjectInput
{
    public Guid Id { get; set; }

    /// <summary>
    /// 负责人
    /// </summary>
    public string Owner { get; set; }

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
