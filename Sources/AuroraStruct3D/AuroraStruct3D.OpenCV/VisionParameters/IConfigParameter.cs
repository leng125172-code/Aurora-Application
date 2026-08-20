namespace AuroraStruct3D.OpenCV.VisionParameters;

/// <summary>
/// 算子构造函数配置参数接口，描述算子实例化时所需的算法配置项。
/// <para>
/// 与 <see cref="IVisionParameter"/>（工作流运行期变量端口）不同，
/// 配置参数在工作流设计期固定，不在运行期随变量变化。
/// </para>
/// <para>
/// 示例：<c>read_image</c> 的 <c>readMode</c>（读取模式）是配置参数；
/// <c>img_path</c>（图片路径）是运行期输入端口。
/// </para>
/// </summary>
public interface IConfigParameter
{
    /// <summary>
    /// 构造函数参数名，与 C# 构造函数参数名保持一致，区分大小写。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// UI 显示名，可为 null（前端回退到 <see cref="Name"/>）。
    /// </summary>
    string? DisplayName { get; }

    /// <summary>配置参数用途及约束说明。</summary>
    string? Description { get; }

    /// <summary>
    /// 参数的 CLR 类型，用于前端渲染对应的控件（文本框、数字框、枚举下拉等）。
    /// </summary>
    Type ParameterType { get; }

    /// <summary>
    /// 默认值，可为 null（无默认值时前端提示必填）。
    /// </summary>
    object? DefaultValue { get; }

    /// <summary>
    /// 值范围约束，可为 null：
    /// 枚举类型 → 枚举名称字符串数组；
    /// 数值类型 → [Min, Max] 数组；
    /// 无约束时为 null。
    /// </summary>
    object? ValueLimit { get; }

    /// <summary>是否为必填参数（无默认值时应为 true）。</summary>
    bool Required { get; }

    /// <summary>
    /// 前端控件类型，用于决定渲染何种 UI 控件。
    /// 默认 <see cref="PortControlType.Input"/>。
    /// </summary>
    PortControlType ControlType { get; }
}
