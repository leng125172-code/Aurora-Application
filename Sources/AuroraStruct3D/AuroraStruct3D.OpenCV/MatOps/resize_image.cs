namespace AuroraStruct3D.OpenCV.MatOps;

/// <summary>
/// 工作流算子：2D 图像缩放。
/// <para>
/// 将图像缩放到指定的目标尺寸，支持多种插值算法。
/// 用于降采样时选择较小的目标尺寸即可。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_mat</c>（Mat）— 输入图像矩阵</item>
///   <item>输出 <c>output_mat</c>（Mat）— 缩放后的图像矩阵</item>
/// </list>
/// </para>
/// </summary>
[Guid("6e5f7089-0123-4567-ef01-123456789005")]
[Category("2D预处理")]
[DisplayName("图像缩放")]
[Description("把图缩放到你要的尺寸大小。")]
public class resize_image : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "input_mat", DisplayName = "输入图像" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "output_mat", DisplayName = "输出图像" },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "targetWidth",
                DisplayName = "目标宽度",
                ParameterType = typeof(int),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "targetHeight",
                DisplayName = "目标高度",
                ParameterType = typeof(int),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "scaleX",
                DisplayName = "X 缩放比例",
                ParameterType = typeof(double),
                DefaultValue = "0.5",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "scaleY",
                DisplayName = "Y 缩放比例",
                ParameterType = typeof(double),
                DefaultValue = "0.5",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "interpolation",
                DisplayName = "插值算法",
                ParameterType = typeof(string),
                DefaultValue = "Linear",
                ValueLimit = new[] { "Nearest", "Linear", "Cubic", "Area", "Lanczos4" },
                Required = false,
                ControlType = PortControlType.Select,
            },
        };

    private readonly int _targetWidth;
    private readonly int _targetHeight;
    private readonly double _scaleX;
    private readonly double _scaleY;
    private readonly InterpolationFlags _interpolation;
    private bool _disposed;

    public resize_image(
        int targetWidth = 0,
        int targetHeight = 0,
        double scaleX = 0.5,
        double scaleY = 0.5,
        string interpolation = "Linear"
    )
    {
        _targetWidth = targetWidth;
        _targetHeight = targetHeight;
        _scaleX = scaleX;
        _scaleY = scaleY;
        _interpolation = interpolation switch
        {
            "Nearest" => InterpolationFlags.Nearest,
            "Cubic" => InterpolationFlags.Cubic,
            "Area" => InterpolationFlags.Area,
            "Lanczos4" => InterpolationFlags.Lanczos4,
            _ => InterpolationFlags.Linear,
        };
    }

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Mat inputMat =
            context.Get<Mat>("input_mat")
            ?? throw new InvalidOperationException(
                "上下文变量 'input_mat' 为空，请确认输入绑定已正确设置。"
            );

        if (inputMat.Empty())
            throw new InvalidOperationException("输入矩阵为空，无法执行图像缩放。");

        int w = _targetWidth > 0 ? _targetWidth : (int)(inputMat.Width * _scaleX);
        int h = _targetHeight > 0 ? _targetHeight : (int)(inputMat.Height * _scaleY);

        if (w <= 0 || h <= 0)
            throw new InvalidOperationException("缩放后的尺寸无效，请检查目标尺寸或缩放比例。");

        Mat result = new Mat();
        try
        {
            Cv2.Resize(inputMat, result, new OpenCvSharp.Size(w, h), 0, 0, _interpolation);
            context.Set("output_mat", result.Clone());
        }
        finally
        {
            result.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
