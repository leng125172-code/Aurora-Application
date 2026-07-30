using System.Text.Json;

namespace AuroraStruct3D.OpenCV.GeometryOps;

/// <summary>
/// 将图像按 2×3 仿射矩阵变换。
/// <para>
/// 这是相机标定、模板定位和跨相机 ROI 映射共用的基础算子。矩阵采用行优先
/// JSON 数组 <c>[m00,m01,m02,m10,m11,m12]</c>，像素坐标遵循 OpenCV 的 x/y 约定。
/// </para>
/// </summary>
[Guid("b30e6a79-691e-4c8d-889e-a60eb51b7c01")]
[Category("2D预处理")]
[DisplayName("仿射坐标变换")]
[Description("按 2×3 仿射矩阵旋转、平移、缩放或校正图像，并保留变换矩阵供下游追溯。")]
public sealed class affine_transform : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new MatImg { ParameterName = "input_mat", DisplayName = "输入图像" },
            new VisionParameter<string>
            {
                ParameterName = "transform_json",
                DisplayName = "仿射矩阵 JSON",
                ParameterType = typeof(string),
                JsonSchema = """{"type":"array","minItems":6,"maxItems":6,"items":{"type":"number"}}""",
            },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg { ParameterName = "output_mat", DisplayName = "变换后图像" },
            new VisionParameter<string>
            {
                ParameterName = "applied_transform_json",
                DisplayName = "已应用仿射矩阵",
                ParameterType = typeof(string),
            },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "outputWidth",
                DisplayName = "输出宽度",
                ParameterType = typeof(int),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "outputHeight",
                DisplayName = "输出高度",
                ParameterType = typeof(int),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "interpolation",
                DisplayName = "插值算法",
                ParameterType = typeof(string),
                DefaultValue = "Linear",
                ValueLimit = new[] { "Nearest", "Linear", "Cubic" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "borderValue",
                DisplayName = "边界填充值",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly int _outputWidth;
    private readonly int _outputHeight;
    private readonly InterpolationFlags _interpolation;
    private readonly double _borderValue;
    private bool _disposed;

    public affine_transform(
        int outputWidth = 0,
        int outputHeight = 0,
        string interpolation = "Linear",
        double borderValue = 0
    )
    {
        _outputWidth = outputWidth;
        _outputHeight = outputHeight;
        _borderValue = borderValue;
        _interpolation = interpolation switch
        {
            "Nearest" => InterpolationFlags.Nearest,
            "Cubic" => InterpolationFlags.Cubic,
            _ => InterpolationFlags.Linear,
        };
    }

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Mat input = context.Get<Mat>("input_mat")
            ?? throw new InvalidOperationException("上下文变量 'input_mat' 为空。" );
        if (input.Empty())
            throw new InvalidOperationException("输入矩阵为空，无法执行仿射坐标变换。");

        string matrixJson = context.Get<string>("transform_json")
            ?? throw new InvalidOperationException("上下文变量 'transform_json' 为空。");
        double[] coefficients;
        try
        {
            coefficients = JsonSerializer.Deserialize<double[]>(matrixJson)
                ?? throw new JsonException("矩阵为空。");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("仿射矩阵必须是包含 6 个数值的 JSON 数组。", exception);
        }

        if (coefficients.Length != 6 || coefficients.Any(value => !double.IsFinite(value)))
            throw new InvalidOperationException("仿射矩阵必须恰好包含 6 个有限数值。");

        int width = _outputWidth > 0 ? _outputWidth : input.Width;
        int height = _outputHeight > 0 ? _outputHeight : input.Height;
        using Mat matrix = new(2, 3, MatType.CV_64FC1);
        for (int index = 0; index < coefficients.Length; index++)
        {
            matrix.Set(index / 3, index % 3, coefficients[index]);
        }
        Mat output = new();
        Cv2.WarpAffine(
            input,
            output,
            matrix,
            new Size(width, height),
            _interpolation,
            BorderTypes.Constant,
            new Scalar(_borderValue)
        );

        context.Set("output_mat", output);
        context.Set("applied_transform_json", JsonSerializer.Serialize(coefficients));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
