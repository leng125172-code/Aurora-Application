using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.CalibrationManagement;

/// <summary>
/// 相机参数模板（独立聚合根）。
/// 按相机型号分类管理，便于快速套用到新建的 <see cref="CalibrationCameraParameter"/>。
/// 参数细节以 JSON 形式存储，避免与硬件参数实体字段强耦合。
/// </summary>
public class CalibrationCameraTemplate : FullAuditedAggregateRoot<Guid>
{
    /// <summary>模板名称</summary>
    public string Name { get; private set; } = null!;

    /// <summary>分类的相机型号</summary>
    public string? CameraModel { get; private set; }

    /// <summary>描述</summary>
    public string? Description { get; private set; }

    /// <summary>参数 JSON（与 <see cref="CalibrationCameraParameter"/> 字段一一映射）</summary>
    public string ParametersJson { get; private set; } = "{}";

    /// <summary>EF Core 构造函数</summary>
    protected CalibrationCameraTemplate() { }

    /// <summary>创建相机模板</summary>
    public CalibrationCameraTemplate(
        Guid id,
        string name,
        string parametersJson,
        string? cameraModel = null,
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
        CameraModel = cameraModel;
        Description = description;
    }

    /// <summary>更新模板内容</summary>
    public void Update(string name, string parametersJson, string? cameraModel, string? description)
    {
        Name = Check.NotNullOrWhiteSpace(
            name,
            nameof(name),
            CalibrationManagementConsts.MaxNameLength
        );
        ParametersJson = Check.NotNullOrWhiteSpace(parametersJson, nameof(parametersJson));
        CameraModel = cameraModel;
        Description = description;
    }
}
