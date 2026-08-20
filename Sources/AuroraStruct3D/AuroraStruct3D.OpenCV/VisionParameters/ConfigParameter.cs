namespace AuroraStruct3D.OpenCV.VisionParameters;

/// <summary>
/// <see cref="IConfigParameter"/> 的通用实现，支持直接初始化和枚举快捷工厂方法。
/// </summary>
public sealed class ConfigParameter : IConfigParameter
{
    /// <inheritdoc/>
    public required string Name { get; init; }

    /// <inheritdoc/>
    public string? DisplayName { get; init; }

    /// <inheritdoc/>
    public string? Description { get; init; }

    /// <inheritdoc/>
    public required Type ParameterType { get; init; }

    /// <inheritdoc/>
    public object? DefaultValue { get; init; }

    /// <inheritdoc/>
    public object? ValueLimit { get; init; }

    /// <inheritdoc/>
    public bool Required { get; init; }

    /// <inheritdoc/>
    public PortControlType ControlType { get; init; } = PortControlType.Input;

    /// <summary>
    /// 创建枚举类型的配置参数，自动提取枚举成员名称列表作为 ValueLimit。
    /// </summary>
    /// <typeparam name="T">枚举类型。</typeparam>
    /// <param name="name">构造函数参数名。</param>
    /// <param name="displayName">UI 显示名，可为 null。</param>
    /// <param name="defaultValue">默认枚举值，可为 null（表示必填）。</param>
    /// <param name="required">是否必填，有默认值时建议为 false。</param>
    /// <returns>配置参数实例。</returns>
    public static ConfigParameter ForEnum<T>(
        string name,
        string? displayName = null,
        T? defaultValue = default,
        bool required = false
    )
        where T : struct, Enum
    {
        return new ConfigParameter
        {
            Name = name,
            DisplayName = displayName,
            ParameterType = typeof(T),
            DefaultValue = defaultValue?.ToString(),
            ValueLimit = Enum.GetNames<T>(),
            Required = required,
            ControlType = PortControlType.Select,
        };
    }
}
