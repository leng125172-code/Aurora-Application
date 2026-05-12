using AuroraStruct3D.Cameras;
using Volo.Abp;
using Volo.Abp.Domain.Entities;

namespace AuroraStruct3D.Cameras;

/// <summary>
/// 相机参数项实体。
/// 存储参数集中的单条参数，对应相机SDK的一个属性或能力值。
/// </summary>
public class CameraParameter : Entity<Guid>
{
    /// <summary>所属参数集ID</summary>
    public Guid ParameterSetId { get; private set; }

    /// <summary>
    /// 参数键名，对应SDK枚举名称。
    /// 例如：Property类型对应 TUCamIdProp 枚举名（如 "ExposureTime"），
    ///       Capability类型对应 TUCamIdCapa 枚举名（如 "BitOfDepth"）。
    /// </summary>
    public string ParamKey { get; private set; } = null!;

    /// <summary>参数类型（属性/能力）</summary>
    public CameraParameterType ParamType { get; private set; }

    /// <summary>参数值（统一存储为字符串，使用时转换）</summary>
    public string Value { get; private set; } = null!;

    /// <summary>参数描述（可选）</summary>
    public string? Description { get; private set; }

    // EF Core 所需的无参构造函数
    protected CameraParameter() { }

    /// <summary>
    /// 创建参数项
    /// </summary>
    /// <param name="id">实体ID</param>
    /// <param name="parameterSetId">所属参数集ID</param>
    /// <param name="paramKey">参数键名</param>
    /// <param name="paramType">参数类型</param>
    /// <param name="value">参数值</param>
    /// <param name="description">描述</param>
    public CameraParameter(
        Guid id,
        Guid parameterSetId,
        string paramKey,
        CameraParameterType paramType,
        string value,
        string? description = null
    )
        : base(id)
    {
        ParameterSetId = parameterSetId;

        Check.NotNullOrWhiteSpace(paramKey, nameof(paramKey), CameraConsts.MaxParameterKeyLength);
        ParamKey = paramKey;
        ParamType = paramType;
        UpdateValue(value);
        Description = description;
    }

    /// <summary>更新参数值</summary>
    public void UpdateValue(string value)
    {
        Check.NotNullOrWhiteSpace(value, nameof(value), CameraConsts.MaxParameterValueLength);
        Value = value;
    }

    /// <summary>获取参数值（浮点型）</summary>
    public double GetDoubleValue()
    {
        return double.Parse(Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>获取参数值（整数型）</summary>
    public int GetIntValue()
    {
        return int.Parse(Value, System.Globalization.CultureInfo.InvariantCulture);
    }
}
