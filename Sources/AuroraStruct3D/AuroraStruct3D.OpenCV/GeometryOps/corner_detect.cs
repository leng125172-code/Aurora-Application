using System.Text.Json;

namespace AuroraStruct3D.OpenCV.GeometryOps;

[Guid("a1b2c3d4-0016-4000-8000-000000000029")]
[Category("2D检测定位")]
[DisplayName("角点检测")]
[Description("用Harris或Shi-Tomasi算法检测图像角点，输出角点坐标。")]
public class corner_detect : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "input_mat", DisplayName = "输入图像" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg() { ParameterName = "output_mat", DisplayName = "检测图像" },
            new VisionParameter<string>
            {
                ParameterName = "corners_json",
                DisplayName = "角点参数",
                ParameterType = typeof(string),
                ControlType = PortControlType.Download,
            },
            new VisionParameter<int>
            {
                ParameterName = "corner_count",
                DisplayName = "角点数量",
                ParameterType = typeof(int),
                ControlType = PortControlType.Download,
            },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "method",
                DisplayName = "检测方法",
                ParameterType = typeof(string),
                DefaultValue = "ShiTomasi",
                ValueLimit = new[] { "Harris", "ShiTomasi" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "maxCorners",
                DisplayName = "最大角点数",
                ParameterType = typeof(int),
                DefaultValue = "100",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "qualityLevel",
                DisplayName = "角点质量水平",
                ParameterType = typeof(double),
                DefaultValue = "0.01",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "minDistance",
                DisplayName = "角点最小间距",
                ParameterType = typeof(double),
                DefaultValue = "10",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "blockSize",
                DisplayName = "邻域块大小",
                ParameterType = typeof(int),
                DefaultValue = "3",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "k",
                DisplayName = "Harris参数k",
                ParameterType = typeof(double),
                DefaultValue = "0.04",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly string _method;
    private readonly int _maxCorners;
    private readonly double _qualityLevel;
    private readonly double _minDistance;
    private readonly int _blockSize;
    private readonly double _k;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public corner_detect(
        string method = "ShiTomasi",
        int maxCorners = 100,
        double qualityLevel = 0.01,
        double minDistance = 10,
        int blockSize = 3,
        double k = 0.04
    )
    {
        _method = method;
        _maxCorners = maxCorners;
        _qualityLevel = qualityLevel;
        _minDistance = minDistance;
        _blockSize = blockSize;
        _k = k;
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
            throw new InvalidOperationException("输入矩阵为空，无法执行角点检测。");

        Mat gray;
        bool needDisposeGray = false;
        if (inputMat.Channels() > 1)
        {
            gray = new Mat();
            Cv2.CvtColor(inputMat, gray, ColorConversionCodes.BGR2GRAY);
            needDisposeGray = true;
        }
        else
        {
            gray = inputMat;
        }

        try
        {
            Point2f[] corners;

            if (_method == "Harris")
            {
                using Mat dst = new Mat();
                using Mat normDst = new Mat();
                using Mat scaledDst = new Mat();

                Cv2.CornerHarris(gray, dst, _blockSize, 3, _k);
                Cv2.Normalize(dst, normDst, 0, 255, NormTypes.MinMax);
                normDst.ConvertTo(scaledDst, MatType.CV_8UC1);

                var cornerList = new List<Point2f>();
                for (int y = 0; y < scaledDst.Rows; y++)
                {
                    for (int x = 0; x < scaledDst.Cols; x++)
                    {
                        if (scaledDst.Get<byte>(y, x) > _qualityLevel * 255)
                        {
                            bool isLocalMax = true;
                            for (int dy = -1; dy <= 1 && isLocalMax; dy++)
                            {
                                for (int dx = -1; dx <= 1 && isLocalMax; dx++)
                                {
                                    int ny = y + dy;
                                    int nx = x + dx;
                                    if (ny >= 0 && ny < scaledDst.Rows && nx >= 0 && nx < scaledDst.Cols)
                                    {
                                        if (scaledDst.Get<byte>(ny, nx) > scaledDst.Get<byte>(y, x))
                                        {
                                            isLocalMax = false;
                                        }
                                    }
                                }
                            }
                            if (isLocalMax)
                            {
                                cornerList.Add(new Point2f(x, y));
                            }
                        }
                    }
                }

                cornerList.Sort((a, b) =>
                {
                    double valA = dst.Get<float>((int)a.Y, (int)a.X);
                    double valB = dst.Get<float>((int)b.Y, (int)b.X);
                    return valB.CompareTo(valA);
                });

                corners = cornerList.Take(_maxCorners).ToArray();
            }
            else
            {
                corners = Cv2.GoodFeaturesToTrack(
                    src: gray,
                    maxCorners: _maxCorners,
                    qualityLevel: _qualityLevel,
                    minDistance: _minDistance,
                    mask: null,
                    blockSize: _blockSize,
                    useHarrisDetector: false,
                    k: _k
                );
            }

            Mat outputMat;
            if (inputMat.Channels() == 1)
            {
                outputMat = new Mat();
                Cv2.CvtColor(inputMat, outputMat, ColorConversionCodes.GRAY2BGR);
            }
            else
            {
                outputMat = inputMat.Clone();
            }

            var cornerInfos = new List<CornerInfo>();
            foreach (var corner in corners)
            {
                Cv2.Circle(outputMat, new Point((int)corner.X, (int)corner.Y), 3, new Scalar(0, 0, 255), -1);
                cornerInfos.Add(
                    new CornerInfo
                    {
                        X = corner.X,
                        Y = corner.Y,
                    }
                );
            }

            string json = JsonSerializer.Serialize(
                new { CornerCount = corners.Length, Corners = cornerInfos },
                JsonOptions
            );

            context.Set("output_mat", outputMat);
            context.Set("corners_json", json);
            context.Set("corner_count", corners.Length);
        }
        finally
        {
            if (needDisposeGray)
                gray.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public class CornerInfo
    {
        public double X { get; set; }
        public double Y { get; set; }
    }
}