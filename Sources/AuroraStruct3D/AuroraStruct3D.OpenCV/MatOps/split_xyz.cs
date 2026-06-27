namespace AuroraStruct3D.OpenCV.MatOps;

/// <summary>
/// 工作流算子：拆分 2D/3D Mat 的 XYZ 通道。
/// <para>
/// 支持两种输入类型：
/// <list type="bullet">
///   <item><description>2D 图像 Mat：3 通道图像（如 BGR）拆分为 3 个单通道 Mat</description></item>
///   <item><description>3D 点云坐标 Mat：N×3 矩阵拆分为 3 个 N×1 的单列 Mat</description></item>
/// </list>
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_mat</c>（Mat）— 输入矩阵，2D 图像或 3D 点云坐标</item>
///   <item>输出 <c>channel_x</c>（Mat）— X 通道（第一列/第一通道）</item>
///   <item>输出 <c>channel_y</c>（Mat）— Y 通道（第二列/第二通道）</item>
///   <item>输出 <c>channel_z</c>（Mat）— Z 通道（第三列/第三通道）</item>
/// </list>
/// </para>
/// </summary>
[Guid("b5c6d7e8-f901-2345-6789-abcdef012345")]
[Category("2D预处理")]
[DisplayName("拆分XYZ")]
[Description("把三通道数据拆成 X、Y、Z 三张图，分开处理。")]
public class split_xyz : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "input_mat", DisplayName = "输入矩阵" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "channel_x", DisplayName = "通道X" },
            new MatImg() { ParameterName = "channel_y", DisplayName = "通道Y" },
            new MatImg() { ParameterName = "channel_z", DisplayName = "通道Z" },
        };

    public static List<IConfigParameter>? ConfigParameters => null;

    private bool _disposed;

    public split_xyz() { }

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
            throw new InvalidOperationException("输入矩阵为空，无法拆分通道。");
        }

        Mat xChannel,
            yChannel,
            zChannel;

        if (inputMat.Channels() > 1)
        {
            Split2DImage(inputMat, out xChannel, out yChannel, out zChannel);
        }
        else
        {
            Split3DCoordinates(inputMat, out xChannel, out yChannel, out zChannel);
        }

        context.Set("channel_x", xChannel);
        context.Set("channel_y", yChannel);
        context.Set("channel_z", zChannel);
    }

    private void Split2DImage(Mat input, out Mat x, out Mat y, out Mat z)
    {
        x = new Mat();
        y = new Mat();
        z = new Mat();

        if (input.Channels() == 1)
        {
            input.CopyTo(x);
            input.CopyTo(y);
            input.CopyTo(z);
            return;
        }

        Mat[] channels = Cv2.Split(input);

        try
        {
            channels[0].CopyTo(x);
            if (channels.Length > 1)
                channels[1].CopyTo(y);
            else
                channels[0].CopyTo(y);

            if (channels.Length > 2)
                channels[2].CopyTo(z);
            else if (channels.Length > 1)
                channels[1].CopyTo(z);
            else
                channels[0].CopyTo(z);
        }
        finally
        {
            foreach (var ch in channels)
                ch.Dispose();
        }
    }

    private void Split3DCoordinates(Mat input, out Mat x, out Mat y, out Mat z)
    {
        int cols = input.Cols;

        using var col0 = input.Col(0);
        x = cols >= 1 ? col0.Clone() : new Mat(input.Rows, 1, input.Type());

        using var col1 = input.Col(1);
        y = cols >= 2 ? col1.Clone() : new Mat(input.Rows, 1, input.Type());

        using var col2 = input.Col(2);
        z = cols >= 3 ? col2.Clone() : new Mat(input.Rows, 1, input.Type());
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
