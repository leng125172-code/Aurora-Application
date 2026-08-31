using System.Text.Json;
using AuroraStruct3D.OpenCV.Common;

namespace AuroraStruct3D.OpenCV.PointCloudRegistration;

/// <summary>
/// 对扫描点云应用已经标定并固化的六自由度姿态。
/// 旋转采用 Z-Y-X 组合，即 R = Rz(rz) * Ry(ry) * Rx(rx)，角度单位为度。
/// </summary>
[Guid("b1a10009-0009-4000-8000-000000000039")]
[Category("3D配准")]
[DisplayName("固定姿态变换")]
[Description("应用标定后固化的旋转和平移；固定工位生产检测无需每次重新做全局配准。")]
public sealed class fixed_pose_transform : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData { ParameterName = "input_point_cloud", DisplayName = "输入点云" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new PointCloudData { ParameterName = "output_point_cloud", DisplayName = "姿态校正点云" },
            new MatImg { ParameterName = "transform_matrix", DisplayName = "固定变换矩阵" },
            new VisionParameter<string>
            {
                ParameterName = "pose_json",
                DisplayName = "固定姿态参数",
                ParameterType = typeof(string),
                ControlType = PortControlType.Download,
            },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            Number("rotationXDeg", "绕X旋转(度)"),
            Number("rotationYDeg", "绕Y旋转(度)"),
            Number("rotationZDeg", "绕Z旋转(度)"),
            Number("translationX", "X平移"),
            Number("translationY", "Y平移"),
            Number("translationZ", "Z平移"),
        };

    private readonly double _rotationXDeg;
    private readonly double _rotationYDeg;
    private readonly double _rotationZDeg;
    private readonly double _translationX;
    private readonly double _translationY;
    private readonly double _translationZ;
    private bool _disposed;

    public fixed_pose_transform(
        double rotationXDeg = 0,
        double rotationYDeg = 0,
        double rotationZDeg = 0,
        double translationX = 0,
        double translationY = 0,
        double translationZ = 0
    )
    {
        double[] values =
        {
            rotationXDeg,
            rotationYDeg,
            rotationZDeg,
            translationX,
            translationY,
            translationZ,
        };
        if (values.Any(value => !double.IsFinite(value)))
            throw new ArgumentOutOfRangeException(nameof(rotationXDeg), "固定姿态参数必须是有限数值。");

        _rotationXDeg = rotationXDeg;
        _rotationYDeg = rotationYDeg;
        _rotationZDeg = rotationZDeg;
        _translationX = translationX;
        _translationY = translationY;
        _translationZ = translationZ;
    }

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        PointCloudData input =
            context.Get<PointCloudData>("input_point_cloud")
            ?? throw new InvalidOperationException("上下文变量 'input_point_cloud' 为空。");
        Mat cloud = input.PointCloud!;
        if (cloud is null || cloud.Empty() || cloud.Cols < 3)
            throw new InvalidOperationException("输入点云为空或不包含 XYZ 坐标。");

        double[] rotation = PointCloudPoseMath.RotationFromEulerZyxDegrees(
            _rotationXDeg,
            _rotationYDeg,
            _rotationZDeg
        );
        double[] translation = { _translationX, _translationY, _translationZ };

        Mat output = PointCloudPoseMath.TransformCloud(cloud, rotation, translation);
        Mat matrix = PointCloudPoseMath.CreateTransformMatrix(rotation, translation);
        context.Set(
            "output_point_cloud",
            PointCloudUtils.BuildCloud(
                output,
                input.HasColors && input.Colors is not null ? input.Colors.Clone() : null
            )
        );
        context.Set("transform_matrix", matrix);
        context.Set(
            "pose_json",
            JsonSerializer.Serialize(
                new
                {
                    rotationXDeg = _rotationXDeg,
                    rotationYDeg = _rotationYDeg,
                    rotationZDeg = _rotationZDeg,
                    translationX = _translationX,
                    translationY = _translationY,
                    translationZ = _translationZ,
                    convention = "Rz*Ry*Rx",
                }
            )
        );
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static ConfigParameter Number(string name, string displayName) =>
        new()
        {
            Name = name,
            DisplayName = displayName,
            ParameterType = typeof(double),
            DefaultValue = "0",
            Required = false,
            ControlType = PortControlType.Input,
        };
}
