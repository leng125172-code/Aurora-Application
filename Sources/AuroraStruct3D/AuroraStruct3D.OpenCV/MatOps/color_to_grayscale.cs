namespace AuroraStruct3D.OpenCV.MatOps;

/// <summary>
/// 工作流算子：彩色转灰度。
/// <para>
/// 支持两种模式：
/// <list type="bullet">
///   <item>2D 图像模式：将彩色图像（如 BGR 3通道）转换为单通道灰度图像</item>
///   <item>3D 点云模式：将点云颜色数据（Mat(N, 3, CV_8UC3)）转换为灰度值（Mat(N, 1, CV_8UC1)）</item>
/// </list>
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_mat</c>（Mat）— 输入矩阵（彩色图像或点云颜色数据）</item>
///   <item>输入 <c>input_point_cloud</c>（Mat）— 点云坐标数据（可选，用于 3D 模式）</item>
///   <item>输出 <c>gray_mat</c>（Mat）— 灰度矩阵</item>
///   <item>输出 <c>output_point_cloud</c>（Mat）— 点云坐标数据（仅 3D 模式）</item>
/// </list>
/// </para>
/// </summary>
[Guid("e8f90123-4567-89ab-cdef-0123456789ab")]
[Category("2D预处理")]
[DisplayName("彩色转灰度")]
[Description("彩图转灰度图，很多检测算子只认灰度图，先转一道。")]
public class color_to_grayscale : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "input_mat", DisplayName = "输入图像" },
            new PointCloudData() { ParameterName = "input_point_cloud", DisplayName = "输入点云" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "gray_mat", DisplayName = "灰度图像" },
            new PointCloudData() { ParameterName = "output_point_cloud", DisplayName = "输出点云" },
        };

    public static List<IConfigParameter>? ConfigParameters => null;

    private bool _disposed;

    public color_to_grayscale() { }

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Mat inputMat =
            context.Get<Mat>("input_mat")
            ?? throw new InvalidOperationException(
                "上下文变量 'input_mat' 为空，请确认 OperatorCallStatement 的输入绑定已正确设置。"
            );

        if (inputMat.Empty())
        {
            throw new InvalidOperationException("输入矩阵为空，无法转换。");
        }

        PointCloudData? inputPointCloud = context.Get<PointCloudData>("input_point_cloud");
        bool is3DMode = inputPointCloud != null && inputPointCloud.PointCloud != null && !inputPointCloud.PointCloud.Empty();

        Mat grayMat;

        if (is3DMode)
        {
            grayMat = ConvertPointCloudColorsToGrayscale(inputMat);

            var outputPointCloud = new PointCloudData();
            outputPointCloud.Value = inputPointCloud!.PointCloud!.Clone();
            outputPointCloud.SetColors(grayMat);
            context.Set("output_point_cloud", outputPointCloud);
        }
        else
        {
            grayMat = ConvertImageToGrayscale(inputMat);
        }

        context.Set("gray_mat", grayMat);
    }

    private Mat ConvertImageToGrayscale(Mat input)
    {
        if (input.Channels() == 1)
            return input.Clone();

        Mat gray = new Mat();
        try
        {
            Cv2.CvtColor(input, gray, ColorConversionCodes.BGR2GRAY);
            return gray.Clone();
        }
        finally
        {
            gray.Dispose();
        }
    }

    private Mat ConvertPointCloudColorsToGrayscale(Mat colors)
    {
        if (colors.Channels() == 1)
            return colors.Clone();

        if (colors.Channels() != 3)
        {
            throw new InvalidOperationException(
                "点云颜色数据必须是 3 通道（BGR）格式。"
            );
        }

        int pointCount = colors.Rows;
        Mat gray = new Mat(pointCount, 1, MatType.CV_8UC1);

        for (int i = 0; i < pointCount; i++)
        {
            byte b = colors.Get<byte>(i, 0);
            byte g = colors.Get<byte>(i, 1);
            byte r = colors.Get<byte>(i, 2);

            byte grayValue = (byte)(0.114 * b + 0.587 * g + 0.299 * r);
            gray.Set(i, 0, grayValue);
        }

        return gray;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}