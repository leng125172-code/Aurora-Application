namespace AuroraStruct3D.OpenCV.VisionParameters;

/// <summary>
/// 泛型视觉参数，用于在工作流节点之间传递任意类型的值。
/// <para>
/// 适用于非矩阵类型的参数（如字符串、数值等），
/// 输出端口类型为 <see cref="PortControlType.Download"/> 时，前端展示为下载链接。
/// </para>
/// </summary>
/// <typeparam name="T">参数值的 CLR 类型。</typeparam>
public class VisionParameter<T> : IVisionParameter
{
    private T? _value;

    /// <summary>参数变量名，工作流连线唯一标识符。</summary>
    public string? ParameterName { get; set; }

    /// <summary>端口 UI 显示名（实例级），为 null 时回退到类型名。</summary>
    public string? DisplayName { get; set; }

    /// <summary>参数实际数据类型。</summary>
    public Type ParameterType { get; set; } = typeof(T);

    /// <summary>
    /// 获取或设置参数值。
    /// </summary>
    public object? Value
    {
        get => _value;
        set
        {
            if (value is T t)
                _value = t;
            else if (value is null)
                _value = default;
            else
                throw new ArgumentException(
                    $"参数 {ParameterName} 的值必须是 {typeof(T).FullName} 类型，"
                        + $"实际传入类型：{value.GetType().FullName}"
                );
        }
    }

    /// <summary>默认值，固定为 null。</summary>
    public object? DefaultValue { get; set; }

    /// <summary>值范围约束，无约束时为 null。</summary>
    public object? ValueLimit { get; set; }

    /// <summary>是否在赋值时进行类型校验。</summary>
    public bool ErrorCheck { get; set; }

    /// <summary>前端控件类型。</summary>
    public PortControlType ControlType { get; set; } = PortControlType.Download;
}