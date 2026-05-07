namespace Lion.AbpPro.CodeManagement.Projects.Aggregates
{
    /// <summary>
    /// 项目
    /// </summary>
    public class Project : FullAuditedAggregateRoot<Guid>, IMultiTenant
    {
        /// <summary>
        /// 租户id
        /// </summary>
        public Guid? TenantId { get; private set; }

        /// <summary>
        /// 负责人
        /// </summary>
        public string Owner { get; private set; }

        /// <summary>
        /// 名称空间
        /// </summary>
        public string NameSpace { get; private set; }

        /// <summary>
        /// 备注
        /// </summary>
        public string Remark { get; private set; }

        /// <summary>
        /// 公司名称
        /// </summary>
        public string CompanyName { get; private set; }

        /// <summary>
        /// 项目名称
        /// </summary>
        public string ProjectName { get; private set; }

        /// <summary>
        /// 是否支持多租户
        /// </summary>
        public bool SupportTenant { get; private set; }

        private Project() { }

        public Project(
            Guid id,
            string owner,
            string companyName,
            string projectName,
            bool supportTenant,
            string remark = null,
            Guid? tenantId = null
        )
            : base(id)
        {
            TenantId = tenantId;
            SupportTenant = supportTenant;
            SetOwner(owner);
            SetNameSpace(companyName, projectName);
            SetRemark(remark);
        }

        private void SetOwner(string owner)
        {
            if (owner.IsNullOrWhiteSpace())
            {
                Owner = string.Empty;
                return;
            }

            Owner = owner;
        }

        private void SetNameSpace(string companyName, string projectName)
        {
            Guard.NotNullOrWhiteSpace(
                companyName,
                nameof(companyName),
                CodeManagementConsts.MaxLength128
            );
            Guard.NotNullOrWhiteSpace(
                projectName,
                nameof(projectName),
                CodeManagementConsts.MaxLength128
            );
            NameSpace = string.Concat(companyName, ".", projectName);
            CompanyName = companyName;
            ProjectName = projectName;
        }

        private void SetRemark(string remark)
        {
            if (remark.IsNullOrWhiteSpace())
            {
                Remark = string.Empty;
                return;
            }

            Remark = remark;
        }

        public void Update(
            string companyName,
            string projectName,
            string owner,
            string remark,
            bool supportTenant
        )
        {
            SetOwner(owner);
            SetNameSpace(companyName, projectName);
            SetRemark(remark);
            SupportTenant = supportTenant;
        }
    }
}
