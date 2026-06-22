namespace AuroraStruct3D.OpenCV.Registry;

/// <summary>
/// 算子元数据描述，用于工作流节点面板的列表展示与分组。
/// </summary>
public sealed class OperatorDescriptor
{
    /// <summary>
    /// 算子唯一标识，来自类上的 <see cref="GuidAttribute"/>，
    /// 在整个工作流系统中永久唯一，不随重命名改变。
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>一级分组名称，来自类上的 <see cref="CategoryAttribute"/>。</summary>
    public required string Category { get; init; }

    /// <summary>UI 显示名称，来自类上的 <see cref="DisplayNameAttribute"/>。</summary>
    public required string DisplayName { get; init; }

    /// <summary>算子功能描述，来自类上的 <see cref="DescriptionAttribute"/>，可为 null。</summary>
    public string? Description { get; init; }

    /// <summary>
    /// 算子 CLR 类型全名（含命名空间），供引擎按需反射实例化。
    /// </summary>
    public required string TypeFullName { get; init; }
}
