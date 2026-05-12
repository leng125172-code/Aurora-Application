using AuroraStruct3D.Cameras;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Cameras;

/// <summary>
/// 相机参数集实体。
/// 一台相机可以有多套参数集（如：高速采集参数集、高质量参数集等），
/// 每套参数集包含一组具体的参数值，可在运行时切换应用。
/// </summary>
public class CameraParameterSet : FullAuditedEntity<Guid>
{
    /// <summary>所属相机设备ID</summary>
    public Guid CameraDeviceId { get; private set; }

    /// <summary>参数集名称（如"高速模式"、"夜间模式"）</summary>
    public string Name { get; private set; } = null!;

    /// <summary>参数集描述</summary>
    public string? Description { get; private set; }

    /// <summary>是否为默认参数集</summary>
    public bool IsDefault { get; private set; }

    /// <summary>排序序号</summary>
    public int SortOrder { get; private set; }

    /// <summary>该参数集包含的具体参数列表</summary>
    public ICollection<CameraParameter> Parameters { get; private set; } =
        new List<CameraParameter>();

    // EF Core 所需的无参构造函数
    protected CameraParameterSet() { }

    /// <summary>
    /// 创建参数集
    /// </summary>
    /// <param name="id">实体ID</param>
    /// <param name="cameraDeviceId">所属相机设备ID</param>
    /// <param name="name">参数集名称</param>
    public CameraParameterSet(Guid id, Guid cameraDeviceId, string name)
        : base(id)
    {
        CameraDeviceId = cameraDeviceId;
        SetName(name);
        IsDefault = false;
        SortOrder = 0;
    }

    /// <summary>设置参数集名称</summary>
    public CameraParameterSet SetName(string name)
    {
        Check.NotNullOrWhiteSpace(name, nameof(name), CameraConsts.MaxParameterSetNameLength);
        Name = name;
        return this;
    }

    /// <summary>设置描述</summary>
    public CameraParameterSet SetDescription(string? description)
    {
        if (description != null)
        {
            Check.Length(description, nameof(description), CameraConsts.MaxDescriptionLength);
        }
        Description = description;
        return this;
    }

    /// <summary>标记为默认参数集</summary>
    public CameraParameterSet SetDefault(bool isDefault)
    {
        IsDefault = isDefault;
        return this;
    }

    /// <summary>设置排序</summary>
    public CameraParameterSet SetSortOrder(int sortOrder)
    {
        SortOrder = sortOrder;
        return this;
    }

    /// <summary>
    /// 添加或更新一条参数
    /// </summary>
    /// <param name="paramKey">参数键（对应SDK的属性/能力ID名称）</param>
    /// <param name="paramType">参数类型</param>
    /// <param name="value">参数值</param>
    /// <param name="description">参数描述</param>
    public CameraParameterSet SetParameter(
        string paramKey,
        CameraParameterType paramType,
        string value,
        string? description = null
    )
    {
        Check.NotNullOrWhiteSpace(paramKey, nameof(paramKey), CameraConsts.MaxParameterKeyLength);

        var existing = Parameters.FirstOrDefault(p => p.ParamKey == paramKey);
        if (existing != null)
        {
            existing.UpdateValue(value);
        }
        else
        {
            Parameters.Add(
                new CameraParameter(Guid.NewGuid(), Id, paramKey, paramType, value, description)
            );
        }
        return this;
    }

    /// <summary>移除指定参数</summary>
    public CameraParameterSet RemoveParameter(string paramKey)
    {
        var param = Parameters.FirstOrDefault(p => p.ParamKey == paramKey);
        if (param != null)
        {
            Parameters.Remove(param);
        }
        return this;
    }
}
