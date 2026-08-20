namespace AuroraStruct3D.OpenCV.VisionParameters;

/// <summary>
/// 图像矩阵参数，封装 OpenCvSharp <see cref="Mat"/> 对象，
/// 用于在工作流节点之间传递图像数据。
/// </summary>
[DisplayName("图像矩阵")]
[MatTypeAttribute(PortMatType.Image2D)]
public class MatImg : IVisionParameter
{
    private Mat? _mat;

    /// <summary>参数变量名，工作流连线唯一标识符。</summary>
    public string? ParameterName { get; set; }

    /// <summary>端口 UI 显示名（实例级），为 null 时回退到类型上的 [DisplayName] 特性。</summary>
    public string? DisplayName { get; set; }

    public string? Description { get; set; }

    /// <summary>参数实际数据类型：<see cref="Mat"/>。</summary>
    public Type ParameterType => typeof(Mat);

    /// <summary>
    /// 获取或设置参数值（<see cref="Mat"/> 实例）。
    /// <para>
    /// 当 <see cref="ErrorCheck"/> 为 <see langword="true"/> 时，
    /// setter 会验证值必须为 <see cref="Mat"/> 类型；
    /// 传入 <see langword="null"/> 表示清空当前图像。
    /// </para>
    /// </summary>
    public object? Value
    {
        get => _mat;
        set
        {
            if (ErrorCheck && value != null && !(value is Mat))
            {
                throw new ArgumentException(
                    $"参数 {ParameterName} 的值必须是 {nameof(Mat)} 类型，"
                        + $"实际传入类型：{value.GetType().FullName}"
                );
            }
            _mat = value as Mat;
        }
    }

    /// <summary>默认值，图像参数无默认值，固定为 <see langword="null"/>。</summary>
    public object? DefaultValue { get; set; } = null;

    /// <summary>值范围约束，图像参数无约束，固定为 <see langword="null"/>。</summary>
    public object? ValueLimit { get; set; } = null;

    /// <summary>是否在赋值时进行类型校验。</summary>
    public bool ErrorCheck { get; set; }

    public PortControlType ControlType => PortControlType.Variable;

    /// <summary>
    /// 判断图像是否为彩色图像（通道数 >= 3）。
    /// 灰度图像返回 false，彩色图像返回 true。
    /// </summary>
    public bool IsColor => _mat != null && _mat.Channels() >= 3;

    /// <summary>
    /// 初始化 <see cref="MatImg"/> 实例。
    /// </summary>
    /// <param name="errorCheck">是否对赋值进行类型校验，默认关闭。</param>
    public MatImg(bool errorCheck = false)
    {
        ErrorCheck = errorCheck;
    }

    /// <summary>
    /// 获取内部 <see cref="Mat"/> 对象，供下游节点直接读取图像数据。
    /// 若尚未执行或已释放则返回 <see langword="null"/>。
    /// </summary>
    public Mat? Mat => _mat;

    /// <summary>
    /// 释放内部 <see cref="Mat"/> 的非托管内存并将其置为 <see langword="null"/>。
    /// 应由持有本参数的算子在每次 <c>Execute()</c> 之前或 <c>Dispose()</c> 时调用，
    /// 防止图像内存泄漏。
    /// </summary>
    public void DisposeMat()
    {
        _mat?.Dispose();
        _mat = null;
    }
}
