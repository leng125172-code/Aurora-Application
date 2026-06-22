namespace AuroraStruct3D.OpenCV.MatOps;

/// <summary>
/// 工作流算子：拆分 2D 图像的 XY 通道（双通道拆分）。
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_mat</c>（Mat）— 2D 图像矩阵</item>
///   <item>输出 <c>channel_x</c>（Mat）— X 通道（第一通道）</item>
///   <item>输出 <c>channel_y</c>（Mat）— Y 通道（第二通道）</item>
/// </list>
/// </para>
/// </summary>
[Guid("c6d7e8f9-0123-4567-89ab-cdef01234567")]
[Category("矩阵操作")]
[DisplayName("拆分XY通道")]
[Description("拆分 2D 图像的 XY 通道，输出两个独立的通道矩阵。")]
public class split_xy : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new() { new MatImg() { ParameterName = "input_mat" } };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "channel_x" },
            new MatImg() { ParameterName = "channel_y" },
        };

    public static List<IConfigParameter>? ConfigParameters => null;

    private bool _disposed;

    public split_xy() { }

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
            yChannel;

        Split2DImage(inputMat, out xChannel, out yChannel);

        context.Set("channel_x", xChannel);
        context.Set("channel_y", yChannel);
    }

    private void Split2DImage(Mat input, out Mat x, out Mat y)
    {
        x = new Mat();
        y = new Mat();

        if (input.Channels() == 1)
        {
            input.CopyTo(x);
            input.CopyTo(y);
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
        }
        finally
        {
            foreach (var ch in channels)
                ch.Dispose();
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
