namespace AuroraStruct3D.OpenCV.VisionParameters;

/// <summary>
/// 点云数据参数，封装点云数据，
/// 用于在工作流节点之间传递点云数据。
/// <para>
/// 点云坐标数据以 <see cref="Mat"/> 形式存储，每行代表一个点：
/// <list type="bullet">
///   <item><description>第 0 列：X 坐标</description></item>
///   <item><description>第 1 列：Y 坐标</description></item>
///   <item><description>第 2 列：Z 坐标</description></item>
///   <item><description>可选：第 3-5 列：法向量 (Nx, Ny, Nz)</description></item>
/// </list>
/// </para>
/// <para>
/// 颜色数据单独存储在 <see cref="Colors"/> 属性中，类型为 <c>Mat(N, 3, CV_8UC3)</c>，
/// 每行代表一个点的 BGR 颜色值（与 OpenCV 图像通道顺序一致）。
/// </para>
/// </summary>
[DisplayName("点云数据")]
[MatTypeAttribute(PortMatType.PointCloud3D)]
public class PointCloudData : IVisionParameter
{
    private Mat? _pointCloud;
    private Mat? _colors;
    private int _pointCount;

    /// <summary>参数变量名，工作流连线唯一标识符。</summary>
    public string? ParameterName { get; set; }

    /// <summary>端口 UI 显示名（实例级），为 null 时回退到类型上的 [DisplayName] 特性。</summary>
    public string? DisplayName { get; set; }

    /// <summary>参数实际数据类型：<see cref="Mat"/>。</summary>
    public Type ParameterType => typeof(Mat);

    /// <summary>
    /// 获取或设置参数值（<see cref="Mat"/> 实例）。
    /// <para>
    /// 当 <see cref="ErrorCheck"/> 为 <see langword="true"/> 时，
    /// setter 会验证值必须为 <see cref="Mat"/> 类型；
    /// 传入 <see langword="null"/> 表示清空当前点云数据。
    /// </para>
    /// </summary>
    public object? Value
    {
        get => _pointCloud;
        set
        {
            if (ErrorCheck && value != null && !(value is Mat))
            {
                throw new ArgumentException(
                    $"参数 {ParameterName} 的值必须是 {nameof(Mat)} 类型，"
                        + $"实际传入类型：{value.GetType().FullName}"
                );
            }
            _pointCloud = value as Mat;
            _pointCount = _pointCloud?.Rows ?? 0;
        }
    }

    /// <summary>默认值，点云参数无默认值，固定为 <see langword="null"/>。</summary>
    public object? DefaultValue { get; set; } = null;

    /// <summary>值范围约束，点云参数无约束，固定为 <see langword="null"/>。</summary>
    public object? ValueLimit { get; set; } = null;

    /// <summary>是否在赋值时进行类型校验。</summary>
    public bool ErrorCheck { get; set; }

    public PortControlType ControlType => PortControlType.Variable;

    /// <summary>
    /// 判断点云是否包含颜色信息（等效于 <see cref="HasColors"/>）。
    /// 无颜色信息的点云返回 false，有颜色信息的点云返回 true。
    /// </summary>
    public bool IsColor => HasColors;

    /// <summary>
    /// 初始化 <see cref="PointCloudData"/> 实例。
    /// </summary>
    /// <param name="errorCheck">是否对赋值进行类型校验，默认关闭。</param>
    public PointCloudData(bool errorCheck = false)
    {
        ErrorCheck = errorCheck;
    }

    /// <summary>
    /// 获取内部 <see cref="Mat"/> 对象，供下游节点直接读取点云坐标数据。
    /// 若尚未执行或已释放则返回 <see langword="null"/>。
    /// </summary>
    public Mat? PointCloud => _pointCloud;

    /// <summary>
    /// 获取颜色数据 <see cref="Mat"/> 对象，类型为 <c>Mat(N, 3, CV_8UC3)</c>，
    /// 每行代表一个点的 BGR 颜色值。
    /// 若点云无颜色信息则返回 <see langword="null"/>。
    /// </summary>
    public Mat? Colors => _colors;

    /// <summary>
    /// 设置颜色数据，类型必须为 <c>Mat(N, 3, CV_8UC3)</c>。
    /// </summary>
    /// <param name="colors">颜色数据 Mat。</param>
    public void SetColors(Mat? colors)
    {
        _colors?.Dispose();
        _colors = colors;
    }

    /// <summary>
    /// 获取点云中的点数量。
    /// </summary>
    public int PointCount => _pointCount;

    /// <summary>
    /// 获取是否包含颜色信息。
    /// </summary>
    public bool HasColors => _colors != null && !_colors.Empty();

    /// <summary>
    /// 释放内部 <see cref="Mat"/> 的非托管内存并将其置为 <see langword="null"/>。
    /// 应由持有本参数的算子在每次 <c>Execute()</c> 之前或 <c>Dispose()</c> 时调用，
    /// 防止内存泄漏。
    /// </summary>
    public void DisposePointCloud()
    {
        _pointCloud?.Dispose();
        _pointCloud = null;
        _colors?.Dispose();
        _colors = null;
        _pointCount = 0;
    }
}
