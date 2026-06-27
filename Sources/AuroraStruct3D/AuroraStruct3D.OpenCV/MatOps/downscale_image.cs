namespace AuroraStruct3D.OpenCV.MatOps;

/// <summary>
/// 工作流算子：2D 降分辨率。
/// <para>
/// 使用高斯金字塔将图像分辨率降低，每次迭代将图像尺寸缩小一半。
/// 相比普通缩放，降分辨率能更好地保留图像特征。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_mat</c>（Mat）— 输入图像矩阵</item>
///   <item>输出 <c>output_mat</c>（Mat）— 降分辨率后的图像矩阵</item>
/// </list>
/// </para>
/// </summary>
[Guid("7f607089-1234-5678-f012-123456789006")]
[Category("2D预处理")]
[DisplayName("降分辨率")]
[Description("图太大跑得慢，先缩小一圈提提速，调试看效果够用。")]
public class downscale_image : IOperator
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
                Name = "iterations",
                DisplayName = "迭代次数",
                ParameterType = typeof(int),
                DefaultValue = "1",
                ValueLimit = new[] { "1", "2", "3", "4" },
                Required = false,
                ControlType = PortControlType.Select,
            },
        };

    private readonly int _iterations;
    private bool _disposed;

    public downscale_image(int iterations = 1)
    {
        _iterations = iterations;
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
            throw new InvalidOperationException("输入矩阵为空，无法执行降分辨率。");

        Mat current = inputMat;
        Mat down = new Mat();
        try
        {
            for (int i = 0; i < _iterations; i++)
            {
                Cv2.PyrDown(current, down);
                if (i < _iterations - 1)
                {
                    current.Dispose();
                    current = down.Clone();
                    down.Dispose();
                    down = new Mat();
                }
            }

            context.Set("output_mat", down.Clone());
        }
        finally
        {
            down.Dispose();
            if (current != inputMat)
                current.Dispose();
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
