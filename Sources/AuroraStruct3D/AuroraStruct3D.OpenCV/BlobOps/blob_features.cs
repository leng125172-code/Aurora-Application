using System.Text.Json;

namespace AuroraStruct3D.OpenCV.BlobOps;

/// <summary>
/// 工作流算子：斑点特征提取。
/// <para>
/// 从二值图像中提取所有斑点（连通域），计算每个斑点的缺陷检测相关特征，
/// 包括面积、周长、圆形度、紧凑度、纵横比、填充率、凸度、惯性比等。
/// 输出特征矩阵（每行一个斑点）和 JSON 格式的特征列表，可直接用于缺陷判定。
/// </para>
/// <para>
/// 特征说明：
/// <list type="bullet">
///   <item><b>面积</b>：斑点像素数</item>
///   <item><b>周长</b>：斑点轮廓周长</item>
///   <item><b>圆形度</b>：4π·面积/周长²，圆=1，越不规则越接近0</item>
///   <item><b>紧凑度</b>：周长²/(4π·面积)，圆=1，形状越复杂值越大</item>
///   <item><b>纵横比</b>：外接矩形宽/高</item>
///   <item><b>填充率</b>：面积/外接矩形面积</item>
///   <item><b>凸度</b>：面积/凸包面积</item>
///   <item><b>惯性比</b>：椭圆拟合长轴/短轴</item>
///   <item><b>灰度均值</b>：原图中对应区域的平均灰度值（可选）</item>
///   <item><b>灰度标准差</b>：原图中对应区域的灰度标准差（可选）</item>
/// </list>
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_mat</c>（Mat）— 二值图像（斑点掩膜）</item>
///   <item>输入 <c>source_mat</c>（Mat，可选）— 原始灰度图，用于计算灰度特征</item>
///   <item>输出 <c>features_mat</c>（Mat）— 特征矩阵 N×11，CV_64FC1</item>
///   <item>输出 <c>features_json</c>（string）— JSON 格式的特征列表</item>
///   <item>输出 <c>blob_count</c>（int）— 斑点数量</item>
/// </list>
/// </para>
/// </summary>
[Guid("a1b2c3d4-0002-4000-8000-000000000015")]
[Category("2D检测定位")]
[DisplayName("斑点特征")]
[Description("找一团团的斑点，量它们的大小、位置等特征，数颗粒/找瑕疵用。")]
public class blob_features : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "input_mat", DisplayName = "输入图像" },
            new MatImg() { ParameterName = "source_mat", DisplayName = "源图像" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "features_mat", DisplayName = "特征矩阵" },
            new VisionParameter<string>
            {
                ParameterName = "features_json",
                DisplayName = "特征JSON",
                ParameterType = typeof(string),
                ControlType = PortControlType.Download,
            },
            new VisionParameter<int>
            {
                ParameterName = "blob_count",
                DisplayName = "斑点数",
                ParameterType = typeof(int),
                ControlType = PortControlType.Download,
            },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "minArea",
                DisplayName = "最小面积",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maxArea",
                DisplayName = "最大面积",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "computeGrayFeatures",
                DisplayName = "计算灰度特征",
                ParameterType = typeof(bool),
                DefaultValue = "false",
                Required = false,
                ControlType = PortControlType.Select,
            },
        };

    /// <summary>特征矩阵列索引。</summary>
    private enum FeatureCol
    {
        Area = 0,
        Perimeter = 1,
        Circularity = 2,
        Compactness = 3,
        AspectRatio = 4,
        FillRatio = 5,
        Convexity = 6,
        InertiaRatio = 7,
        CentroidX = 8,
        CentroidY = 9,
        GrayMean = 10,
        GrayStdDev = 11,
    }

    private readonly double _minArea;
    private readonly double _maxArea;
    private readonly bool _computeGrayFeatures;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// 初始化斑点特征提取算子。
    /// </summary>
    /// <param name="minArea">最小面积阈值。</param>
    /// <param name="maxArea">最大面积阈值（0=不限）。</param>
    /// <param name="computeGrayFeatures">是否基于 source_mat 计算灰度均值和标准差。</param>
    public blob_features(double minArea = 0, double maxArea = 0, bool computeGrayFeatures = false)
    {
        _minArea = minArea;
        _maxArea = maxArea;
        _computeGrayFeatures = computeGrayFeatures;
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
            throw new InvalidOperationException("输入矩阵为空，无法提取斑点特征。");

        // 获取原始灰度图（可选）
        Mat? sourceMat = context.Get<Mat>("source_mat");

        // 确保输入为二值图
        Mat binaryMat;
        bool needDisposeBinary = false;
        if (inputMat.Channels() > 1)
        {
            binaryMat = new Mat();
            Cv2.CvtColor(inputMat, binaryMat, ColorConversionCodes.BGR2GRAY);
            Cv2.Threshold(binaryMat, binaryMat, 127, 255, ThresholdTypes.Binary);
            needDisposeBinary = true;
        }
        else if (inputMat.Type() != MatType.CV_8UC1)
        {
            binaryMat = new Mat();
            inputMat.ConvertTo(binaryMat, MatType.CV_8UC1);
            needDisposeBinary = true;
        }
        else
        {
            binaryMat = inputMat;
        }

        // 确保灰度源图为单通道
        Mat? graySource = null;
        bool needDisposeGray = false;
        if (_computeGrayFeatures && sourceMat is not null && !sourceMat.Empty())
        {
            if (sourceMat.Channels() > 1)
            {
                graySource = new Mat();
                Cv2.CvtColor(sourceMat, graySource, ColorConversionCodes.BGR2GRAY);
                needDisposeGray = true;
            }
            else
            {
                graySource = sourceMat;
            }
        }

        try
        {
            // 提取轮廓
            Cv2.FindContours(
                binaryMat,
                out Point[][] contours,
                out HierarchyIndex[] _,
                RetrievalModes.External,
                ContourApproximationModes.ApproxSimple
            );

            List<BlobFeature> features = new(contours.Length);
            int featureCount = _computeGrayFeatures ? 12 : 10;

            foreach (Point[] contour in contours)
            {
                double area = Cv2.ContourArea(contour);
                if (_minArea > 0 && area < _minArea)
                    continue;
                if (_maxArea > 0 && area > _maxArea)
                    continue;

                double perimeter = Cv2.ArcLength(contour, closed: true);

                // 圆形度
                double circularity =
                    perimeter > 0 ? (4 * Math.PI * area) / (perimeter * perimeter) : 0;

                // 紧凑度 = 1 / 圆形度
                double compactness = circularity > 0 ? 1.0 / circularity : double.MaxValue;

                // 质心
                Moments moments = Cv2.Moments(contour);
                double cx = moments.M10 / (moments.M00 + 1e-10);
                double cy = moments.M01 / (moments.M00 + 1e-10);

                // 外接矩形
                Rect boundingRect = Cv2.BoundingRect(contour);
                double aspectRatio =
                    boundingRect.Height > 0 ? (double)boundingRect.Width / boundingRect.Height : 0;
                double fillRatio =
                    boundingRect.Width * boundingRect.Height > 0
                        ? area / (boundingRect.Width * boundingRect.Height)
                        : 0;

                // 凸包与凸度
                Point[] hull = Cv2.ConvexHull(contour);
                double hullArea = Cv2.ContourArea(hull);
                double convexity = hullArea > 0 ? area / hullArea : 0;

                // 椭圆拟合 → 惯性比（长轴/短轴）
                double inertiaRatio = 1.0;
                if (contour.Length >= 5)
                {
                    RotatedRect ellipse = Cv2.FitEllipse(contour);
                    double major = Math.Max(ellipse.Size.Width, ellipse.Size.Height);
                    double minor = Math.Min(ellipse.Size.Width, ellipse.Size.Height);
                    inertiaRatio = minor > 0 ? major / minor : 1.0;
                }

                // 灰度特征（可选）
                double grayMean = 0;
                double grayStdDev = 0;
                if (_computeGrayFeatures && graySource is not null)
                {
                    using Mat mask = Mat.Zeros(binaryMat.Rows, binaryMat.Cols, MatType.CV_8UC1);
                    Cv2.DrawContours(
                        mask,
                        new[] { contour },
                        -1,
                        Scalar.White,
                        -1 // 填充
                    );
                    Cv2.MeanStdDev(graySource, out Scalar mean, out Scalar stddev, mask);
                    grayMean = mean.Val0;
                    grayStdDev = stddev.Val0;
                }

                features.Add(
                    new BlobFeature
                    {
                        Area = area,
                        Perimeter = perimeter,
                        Circularity = circularity,
                        Compactness = compactness,
                        AspectRatio = aspectRatio,
                        FillRatio = fillRatio,
                        Convexity = convexity,
                        InertiaRatio = inertiaRatio,
                        CentroidX = cx,
                        CentroidY = cy,
                        GrayMean = grayMean,
                        GrayStdDev = grayStdDev,
                    }
                );
            }

            // 构建特征矩阵
            Mat featuresMat = new Mat(features.Count, featureCount, MatType.CV_64FC1);
            for (int i = 0; i < features.Count; i++)
            {
                BlobFeature f = features[i];
                featuresMat.Set(i, (int)FeatureCol.Area, f.Area);
                featuresMat.Set(i, (int)FeatureCol.Perimeter, f.Perimeter);
                featuresMat.Set(i, (int)FeatureCol.Circularity, f.Circularity);
                featuresMat.Set(i, (int)FeatureCol.Compactness, f.Compactness);
                featuresMat.Set(i, (int)FeatureCol.AspectRatio, f.AspectRatio);
                featuresMat.Set(i, (int)FeatureCol.FillRatio, f.FillRatio);
                featuresMat.Set(i, (int)FeatureCol.Convexity, f.Convexity);
                featuresMat.Set(i, (int)FeatureCol.InertiaRatio, f.InertiaRatio);
                featuresMat.Set(i, (int)FeatureCol.CentroidX, f.CentroidX);
                featuresMat.Set(i, (int)FeatureCol.CentroidY, f.CentroidY);
                if (_computeGrayFeatures)
                {
                    featuresMat.Set(i, (int)FeatureCol.GrayMean, f.GrayMean);
                    featuresMat.Set(i, (int)FeatureCol.GrayStdDev, f.GrayStdDev);
                }
            }

            string json = JsonSerializer.Serialize(features, JsonOptions);

            context.Set("features_mat", featuresMat);
            context.Set("features_json", json);
            context.Set("blob_count", features.Count);
        }
        finally
        {
            if (needDisposeBinary)
                binaryMat.Dispose();
            if (needDisposeGray && graySource is not null)
                graySource.Dispose();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    // ── 输出 DTO ────────────────────────────────────────────────────────────

    /// <summary>单个斑点的特征数据。</summary>
    public class BlobFeature
    {
        public double Area { get; set; }
        public double Perimeter { get; set; }
        public double Circularity { get; set; }
        public double Compactness { get; set; }
        public double AspectRatio { get; set; }
        public double FillRatio { get; set; }
        public double Convexity { get; set; }
        public double InertiaRatio { get; set; }
        public double CentroidX { get; set; }
        public double CentroidY { get; set; }
        public double GrayMean { get; set; }
        public double GrayStdDev { get; set; }
    }
}
