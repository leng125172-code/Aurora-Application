using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.CalibrationManagement;

/// <summary>
/// 结构光参数模板（独立聚合根）。
/// 按投射器型号分类管理，便于快速套用到新建的 <see cref="CalibrationProjectorParameter"/>。
/// </summary>
public class CalibrationProjectorTemplate : FullAuditedAggregateRoot<Guid>
{
    /// <summary>模板名称</summary>
    public string Name { get; private set; } = null!;

    /// <summary>分类的投射器型号</summary>
    public string? ProjectorModel { get; private set; }

    /// <summary>描述</summary>
    public string? Description { get; private set; }

    /// <summary>参数 JSON</summary>
    public string ParametersJson { get; private set; } = "{}";

    /// <summary>EF Core 构造函数</summary>
    protected CalibrationProjectorTemplate() { }

    /// <summary>创建投射器模板</summary>
    public CalibrationProjectorTemplate(
        Guid id,
        string name,
        string parametersJson,
        string? projectorModel = null,
        string? description = null
    )
        : base(id)
    {
        Name = Check.NotNullOrWhiteSpace(
            name,
            nameof(name),
            CalibrationManagementConsts.MaxNameLength
        );
        ParametersJson = Check.NotNullOrWhiteSpace(parametersJson, nameof(parametersJson));
        ProjectorModel = projectorModel;
        Description = description;
    }

    /// <summary>更新模板内容</summary>
    public void Update(
        string name,
        string parametersJson,
        string? projectorModel,
        string? description
    )
    {
        Name = Check.NotNullOrWhiteSpace(
            name,
            nameof(name),
            CalibrationManagementConsts.MaxNameLength
        );
        ParametersJson = Check.NotNullOrWhiteSpace(parametersJson, nameof(parametersJson));
        ProjectorModel = projectorModel;
        Description = description;
    }
}
