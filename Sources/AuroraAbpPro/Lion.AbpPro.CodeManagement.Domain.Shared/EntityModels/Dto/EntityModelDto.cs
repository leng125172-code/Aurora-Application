namespace Lion.AbpPro.CodeManagement.EntityModels.Dto;

public class EntityModelDto : AggregateDtoBase<Guid>
{
    /// <summary>
    /// 租户id
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 项目id
    /// </summary>
    public Guid ProjectId { get; set; }

    /// <summary>
    /// 编码
    /// </summary>
    public string Code { get; set; }

    /// <summary>
    /// 描述
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// 实体关系
    /// </summary>
    public RelationalType? RelationalType { get; set; }

    /// <summary>
    /// 父类Id
    /// </summary>
    public Guid? ParentId { get; set; }

    /// <summary>
    /// 聚合根Id
    /// </summary>
    public Guid AggregateId { get; set; }

    /// <summary>
    /// 实体模型属性集合
    /// </summary>
    public List<EntityModelPropertyDto> EntityModelProperties { get; set; }
}
