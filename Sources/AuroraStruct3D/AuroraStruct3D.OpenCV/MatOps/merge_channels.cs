namespace AuroraStruct3D.OpenCV.MatOps;

/// <summary>
/// 工作流算子：通道合并（支持 XY/XZ/YZ/XYZ 合并）。
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>channel_x</c>（Mat）— 通道 A（X）</item>
///   <item>输入 <c>channel_y</c>（Mat）— 通道 B（Y）</item>
///   <item>输入 <c>channel_z</c>（Mat）— 通道 C（Z，可选）</item>
///   <item>输出 <c>merged_mat</c>（Mat）— 合并后的矩阵（2或3通道）</item>
/// </list>
/// </para>
/// <para>
/// 配置参数：
/// <list type="bullet">
///   <item><c>merge_mode</c>（枚举）— 合并模式：XY、XZ、YZ、XYZ</item>
/// </list>
/// </para>
/// </summary>
[Guid("d7e8f901-2345-6789-abcd-ef0123456789")]
[Category("2D预处理")]
[DisplayName("合并通道")]
[Description("把几张单通道图拼回一张多通道彩图。")]
public class merge_channels : IOperator
{
    private readonly MergeMode _mergeMode;

    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "channel_x", DisplayName = "通道X" },
            new MatImg() { ParameterName = "channel_y", DisplayName = "通道Y" },
            new MatImg() { ParameterName = "channel_z", DisplayName = "通道Z" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "merged_mat", DisplayName = "合并矩阵" },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "merge_mode",
                DisplayName = "合并模式",
                ParameterType = typeof(MergeMode),
                DefaultValue = MergeMode.XY.ToString(),
                ValueLimit = Enum.GetNames<MergeMode>(),
                Required = false,
                ControlType = PortControlType.Select,
            },
        };

    private bool _disposed;

    public enum MergeMode
    {
        XY,
        XZ,
        YZ,
        XYZ,
    }

    public merge_channels(MergeMode merge_mode = MergeMode.XY)
    {
        _mergeMode = merge_mode;
    }

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Mat channelX =
            context.Get<Mat>("channel_x")
            ?? throw new InvalidOperationException(
                "上下文变量 'channel_x' 为空，请确认 OperatorCallStatement 的输入绑定已正确设置。"
            );

        Mat channelY =
            context.Get<Mat>("channel_y")
            ?? throw new InvalidOperationException(
                "上下文变量 'channel_y' 为空，请确认 OperatorCallStatement 的输入绑定已正确设置。"
            );

        Mat? channelZ = context.Get<Mat>("channel_z");

        if (channelX.Empty() || channelY.Empty())
        {
            throw new InvalidOperationException("输入通道矩阵为空，无法合并。");
        }

        if (channelX.Size() != channelY.Size())
        {
            throw new InvalidOperationException("输入通道的尺寸不一致，无法合并。");
        }

        if (channelZ != null && !channelZ.Empty() && channelX.Size() != channelZ.Size())
        {
            throw new InvalidOperationException("输入通道的尺寸不一致，无法合并。");
        }

        Mat merged = MergeChannels(channelX, channelY, channelZ, _mergeMode);

        context.Set("merged_mat", merged);
    }

    private Mat MergeChannels(Mat channelX, Mat channelY, Mat? channelZ, MergeMode mode)
    {
        if (channelX.Channels() != 1)
            channelX = channelX.Reshape(1);
        if (channelY.Channels() != 1)
            channelY = channelY.Reshape(1);
        if (channelZ != null && channelZ.Channels() != 1)
            channelZ = channelZ.Reshape(1);

        List<Mat> channelList = [];

        switch (mode)
        {
            case MergeMode.XY:
                channelList.Add(channelX.Clone());
                channelList.Add(channelY.Clone());
                break;
            case MergeMode.XZ:
                channelList.Add(channelX.Clone());
                if (channelZ != null && !channelZ.Empty())
                    channelList.Add(channelZ.Clone());
                else
                    channelList.Add(channelY.Clone());
                break;
            case MergeMode.YZ:
                channelList.Add(channelY.Clone());
                if (channelZ != null && !channelZ.Empty())
                    channelList.Add(channelZ.Clone());
                else
                    channelList.Add(channelX.Clone());
                break;
            case MergeMode.XYZ:
                if (channelZ == null || channelZ.Empty())
                    throw new InvalidOperationException("XYZ 模式需要 channel_z 输入。");
                channelList.Add(channelX.Clone());
                channelList.Add(channelY.Clone());
                channelList.Add(channelZ.Clone());
                break;
        }

        Mat[] channels = channelList.ToArray();
        Mat result = new Mat();

        try
        {
            Cv2.Merge(channels, result);
            return result.Clone();
        }
        finally
        {
            result.Dispose();
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
