namespace AuroraStruct3D.OpenCV.ConnectedOps;

/// <summary>
/// 工作流算子：连通域标记与分析。
/// <para>
/// 对二值图像中的连通区域进行标记，提取每个连通域的面积、质心、外接矩形等特征。
/// 输入为二值图像（背景 0，前景非 0），输出为标记后的连通域信息和统计元数据。
/// </para>
/// <para>
/// 标记原理：使用 OpenCV 的 <c>ConnectedComponentsWithStats</c> 函数，
/// 基于 4 邻域或 8 邻域搜索连通像素，为每个连通域分配唯一标签。
/// 标签 0 固定为背景，标签 1~N 为各连通域。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_mat</c>（Mat）— 二值输入图像（背景=0，前景=非0）</item>
///   <item>输出 <c>labels_mat</c>（Mat）— 标签图像（CV_32SC1，每个像素值为所属连通域标签）</item>
///   <item>输出 <c>stats_mat</c>（Mat）— 统计信息矩阵（N×5，CV_32SC1，每行：[x, y, w, h, area]）</item>
///   <item>输出 <c>centroids_mat</c>（Mat）— 质心坐标矩阵（N×2，CV_64FC1，每行：[cx, cy]）</item>
///   <item>输出 <c>component_count</c>（int）— 连通域数量（不含背景 0）</item>
/// </list>
/// </para>
/// </summary>
[Guid("a1b2c3d4-0002-4000-8000-000000000013")]
[Category("2D检测定位")]
[DisplayName("连通域标记")]
[Description("把二值图里连成片的区域一块块标号分出来。")]
public class connected_components : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "input_mat", DisplayName = "输入图像" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "labels_mat", DisplayName = "标签图像" },
            new MatImg() { ParameterName = "stats_mat", DisplayName = "统计信息" },
            new MatImg() { ParameterName = "centroids_mat", DisplayName = "质心坐标" },
            new VisionParameter<int>
            {
                ParameterName = "component_count",
                DisplayName = "连通域数",
                ParameterType = typeof(int),
                ControlType = PortControlType.Download,
            },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "connectivity",
                DisplayName = "连通方式",
                ParameterType = typeof(string),
                DefaultValue = "8",
                ValueLimit = new[] { "4", "8" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "minArea",
                DisplayName = "最小面积",
                ParameterType = typeof(int),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly string _connectivity;
    private readonly int _minArea;
    private bool _disposed;

    /// <summary>
    /// 初始化连通域标记算子。
    /// </summary>
    /// <param name="connectivity">连通方式：4 或 8。</param>
    /// <param name="minArea">最小面积阈值，面积小于此值的连通域被过滤（0 表示不过滤）。</param>
    public connected_components(string connectivity = "8", int minArea = 0)
    {
        _connectivity = connectivity;
        _minArea = minArea;
    }

    /// <inheritdoc/>
    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Mat inputMat =
            context.Get<Mat>("input_mat")
            ?? throw new InvalidOperationException(
                "上下文变量 'input_mat' 为空，请确认输入绑定已正确设置。"
            );

        if (inputMat.Empty())
            throw new InvalidOperationException("输入矩阵为空，无法执行连通域分析。");

        // 确保输入为二值图（灰度单通道）
        Mat binaryMat;
        bool needDispose = false;
        if (inputMat.Channels() > 1)
        {
            binaryMat = new Mat();
            Cv2.CvtColor(inputMat, binaryMat, ColorConversionCodes.BGR2GRAY);
            // 如果输入不是严格的 0/255 二值，做一次阈值化
            Cv2.Threshold(binaryMat, binaryMat, 127, 255, ThresholdTypes.Binary);
            needDispose = true;
        }
        else if (inputMat.Type() != MatType.CV_8UC1)
        {
            binaryMat = new Mat();
            inputMat.ConvertTo(binaryMat, MatType.CV_8UC1);
            Cv2.Threshold(binaryMat, binaryMat, 127, 255, ThresholdTypes.Binary);
            needDispose = true;
        }
        else
        {
            binaryMat = inputMat;
        }

        Mat labels = new Mat();
        Mat stats = new Mat();
        Mat centroids = new Mat();

        try
        {
            PixelConnectivity connectivity = _connectivity switch
            {
                "4" => PixelConnectivity.Connectivity4,
                _ => PixelConnectivity.Connectivity8,
            };

            int labelCount = Cv2.ConnectedComponentsWithStats(
                binaryMat,
                labels,
                stats,
                centroids,
                connectivity,
                MatType.CV_32S
            );

            // 背景(标签0)不算在连通域数量中
            int componentCount = labelCount - 1;

            if (_minArea > 0)
            {
                // 过滤面积小于阈值的连通域
                componentCount = FilterSmallComponents(
                    labels,
                    stats,
                    centroids,
                    labelCount,
                    _minArea
                );
            }

            context.Set("labels_mat", labels.Clone());
            context.Set("stats_mat", stats.Clone());
            context.Set("centroids_mat", centroids.Clone());
            context.Set("component_count", componentCount);
        }
        finally
        {
            labels.Dispose();
            stats.Dispose();
            centroids.Dispose();
            if (needDispose)
                binaryMat.Dispose();
        }
    }

    /// <summary>
    /// 过滤面积小于阈值的连通域，将其标签合并到背景中。
    /// </summary>
    /// <returns>过滤后剩余连通域数量。</returns>
    private static int FilterSmallComponents(
        Mat labels,
        Mat stats,
        Mat centroids,
        int labelCount,
        int minArea
    )
    {
        int keptCount = 0;
        for (int i = 1; i < labelCount; i++)
        {
            int area = stats.Get<int>(i, (int)ConnectedComponentsTypes.Area);
            if (area < minArea)
            {
                // 将该标签的像素置为 0（背景）
                using Mat mask = new Mat();
                Cv2.Compare(labels, Scalar.All(i), mask, CmpTypes.EQ);
                labels.SetTo(Scalar.All(0), mask);
                // 清零 stats 和 centroids 对应行
                using Mat statRow = stats.Row(i);
                statRow.SetTo(Scalar.All(0));
                using Mat centRow = centroids.Row(i);
                centRow.SetTo(Scalar.All(0));
            }
            else
            {
                keptCount++;
            }
        }
        return keptCount;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
