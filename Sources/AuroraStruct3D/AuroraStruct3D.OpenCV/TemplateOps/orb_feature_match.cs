using System.Text.Json;

namespace AuroraStruct3D.OpenCV.TemplateOps;

/// <summary>
/// 使用 ORB 局部特征和 RANSAC 单应矩阵定位参考图案。
/// 相较灰度模板匹配，可容忍旋转、一定缩放、光照变化和局部遮挡。
/// </summary>
[Guid("4b69aa8e-3d7c-4c82-bba7-6d0b71352c01")]
[Category("2D检测定位")]
[DisplayName("ORB特征匹配")]
[Description("通过 ORB 局部特征和 RANSAC 定位参考图案，支持旋转、缩放和部分遮挡。")]
public sealed class orb_feature_match : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new MatImg { ParameterName = "reference_mat", DisplayName = "参考图像" },
            new MatImg { ParameterName = "input_mat", DisplayName = "待检图像" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg { ParameterName = "output_mat", DisplayName = "匹配结果图" },
            new VisionParameter<bool>
            {
                ParameterName = "is_found",
                DisplayName = "是否找到目标",
                ParameterType = typeof(bool),
            },
            new VisionParameter<string>
            {
                ParameterName = "match_result_json",
                DisplayName = "匹配结果",
                ParameterType = typeof(string),
                JsonSchema = """{"type":"object","properties":{"isFound":{"type":"boolean"},"matchCount":{"type":"integer"},"inlierCount":{"type":"integer"},"inlierRatio":{"type":"number"},"homography":{"type":["array","null"]}}}""",
            },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter { Name = "maxFeatures", DisplayName = "最大特征数", ParameterType = typeof(int), DefaultValue = "1000", Required = false, ControlType = PortControlType.Input },
            new ConfigParameter { Name = "maxMatchDistance", DisplayName = "最大汉明距离", ParameterType = typeof(double), DefaultValue = "64", Required = false, ControlType = PortControlType.Input },
            new ConfigParameter { Name = "minInliers", DisplayName = "最小内点数", ParameterType = typeof(int), DefaultValue = "8", Required = false, ControlType = PortControlType.Input },
            new ConfigParameter { Name = "ransacReprojectionThreshold", DisplayName = "RANSAC 重投影阈值", ParameterType = typeof(double), DefaultValue = "3", Required = false, ControlType = PortControlType.Input },
        };

    private readonly int _maxFeatures;
    private readonly double _maxMatchDistance;
    private readonly int _minInliers;
    private readonly double _ransacReprojectionThreshold;
    private bool _disposed;

    public orb_feature_match(int maxFeatures = 1000, double maxMatchDistance = 64, int minInliers = 8, double ransacReprojectionThreshold = 3)
    {
        if (maxFeatures <= 0 || maxMatchDistance <= 0 || minInliers < 4 || ransacReprojectionThreshold <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxFeatures), "ORB 特征匹配配置无效。");
        _maxFeatures = maxFeatures;
        _maxMatchDistance = maxMatchDistance;
        _minInliers = minInliers;
        _ransacReprojectionThreshold = ransacReprojectionThreshold;
    }

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Mat reference = RequireImage(context, "reference_mat");
        Mat input = RequireImage(context, "input_mat");
        using Mat referenceGray = ToGray(reference);
        using Mat inputGray = ToGray(input);
        using var orb = ORB.Create(_maxFeatures);
        using Mat referenceDescriptors = new();
        using Mat inputDescriptors = new();
        orb.DetectAndCompute(referenceGray, null, out KeyPoint[] referenceKeypoints, referenceDescriptors);
        orb.DetectAndCompute(inputGray, null, out KeyPoint[] inputKeypoints, inputDescriptors);

        if (referenceDescriptors.Empty() || inputDescriptors.Empty())
        {
            SetNotFound(context, input, 0, 0, "任一图像未检测到足够的 ORB 特征。");
            return;
        }

        using var matcher = new BFMatcher(NormTypes.Hamming, crossCheck: true);
        DMatch[] matches = matcher.Match(referenceDescriptors, inputDescriptors)
            .Where(match => match.Distance <= _maxMatchDistance)
            .OrderBy(match => match.Distance)
            .ToArray();
        if (matches.Length < 4)
        {
            SetNotFound(context, input, matches.Length, 0, "有效特征匹配少于计算单应矩阵所需的 4 对。");
            return;
        }

        Point2d[] sourcePoints = matches.Select(match => referenceKeypoints[match.QueryIdx].Pt)
            .Select(point => new Point2d(point.X, point.Y)).ToArray();
        Point2d[] destinationPoints = matches.Select(match => inputKeypoints[match.TrainIdx].Pt)
            .Select(point => new Point2d(point.X, point.Y)).ToArray();
        using Mat inlierMask = new();
        using Mat homography = Cv2.FindHomography(sourcePoints, destinationPoints, HomographyMethods.Ransac, _ransacReprojectionThreshold, inlierMask);
        int inlierCount = inlierMask.Empty() ? 0 : Cv2.CountNonZero(inlierMask);
        bool isFound = !homography.Empty() && inlierCount >= _minInliers;
        using Mat visualized = new();
        Cv2.DrawMatches(reference, referenceKeypoints, input, inputKeypoints, matches, visualized);

        context.Set("output_mat", visualized.Clone());
        context.Set("is_found", isFound);
        context.Set("match_result_json", JsonSerializer.Serialize(new
        {
            isFound,
            matchCount = matches.Length,
            inlierCount,
            inlierRatio = matches.Length == 0 ? 0d : (double)inlierCount / matches.Length,
            homography = homography.Empty() ? null : ReadMatrix(homography),
            message = isFound ? "匹配成功。" : "RANSAC 内点数不足，匹配结果不可信。",
        }));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static Mat RequireImage(IWorkflowContext context, string name) => context.Get<Mat>(name) is { } image && !image.Empty()
        ? image
        : throw new InvalidOperationException($"上下文变量 '{name}' 为空，无法执行 ORB 特征匹配。");

    private static Mat ToGray(Mat image)
    {
        if (image.Channels() == 1) return image.Clone();
        Mat gray = new();
        Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        return gray;
    }

    private static double[][] ReadMatrix(Mat matrix) => Enumerable.Range(0, matrix.Rows)
        .Select(row => Enumerable.Range(0, matrix.Cols).Select(column => matrix.At<double>(row, column)).ToArray())
        .ToArray();

    private static void SetNotFound(IWorkflowContext context, Mat input, int matchCount, int inlierCount, string message)
    {
        context.Set("output_mat", input.Clone());
        context.Set("is_found", false);
        context.Set("match_result_json", JsonSerializer.Serialize(new { isFound = false, matchCount, inlierCount, inlierRatio = 0d, homography = (double[][]?)null, message }));
    }
}
