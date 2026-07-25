using AuroraStruct3D.OpenCV.Common;

namespace AuroraStruct3D.OpenCV.PointCloudOps;

/// <summary>
/// 工作流算子：按 Z 轴高度为点云着色。
/// </summary>
[Guid("de31c1ab-9ef0-43ab-a30f-0dce0e1d8201")]
[Category("3D点云预处理")]
[DisplayName("Z轴着色点云")]
[Description("按 Z 高度给 XYZ 点云上色，方便后面投影成图像做 ROI 和标注。")]
public class z_colorize_point_cloud : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData { ParameterName = "input_point_cloud", DisplayName = "输入点云" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new PointCloudData { ParameterName = "output_point_cloud", DisplayName = "输出点云" },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "colorMap",
                DisplayName = "颜色映射",
                ParameterType = typeof(string),
                DefaultValue = "jet",
                ValueLimit = new[] { "jet", "turbo", "hot", "hsv" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "autoRange",
                DisplayName = "自动范围",
                ParameterType = typeof(bool),
                DefaultValue = "true",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "minZ",
                DisplayName = "最小Z",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maxZ",
                DisplayName = "最大Z",
                ParameterType = typeof(double),
                DefaultValue = "1",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "invert",
                DisplayName = "反转颜色",
                ParameterType = typeof(bool),
                DefaultValue = "false",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly string _colorMap;
    private readonly bool _autoRange;
    private readonly double _minZ;
    private readonly double _maxZ;
    private readonly bool _invert;
    private bool _disposed;

    /// <summary>
    /// 初始化 Z 轴着色点云算子。
    /// </summary>
    public z_colorize_point_cloud(
        string colorMap = "jet",
        bool autoRange = true,
        double minZ = 0,
        double maxZ = 1,
        bool invert = false
    )
    {
        _colorMap = colorMap;
        _autoRange = autoRange;
        _minZ = minZ;
        _maxZ = maxZ;
        _invert = invert;
    }

    /// <inheritdoc/>
    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        PointCloudData input =
            context.Get<PointCloudData>("input_point_cloud")
            ?? throw new InvalidOperationException(
                "上下文变量 'input_point_cloud' 为空，请确认输入绑定已正确设置。"
            );

        Mat pointCloud =
            input.PointCloud
            ?? throw new InvalidOperationException("输入点云为空，无法执行 Z 轴着色。");
        if (pointCloud.Empty())
        {
            throw new InvalidOperationException("输入点云为空，无法执行 Z 轴着色。");
        }

        if (pointCloud.Cols < 3)
        {
            throw new InvalidOperationException("输入点云列数不足 3，无法读取 Z 坐标。") ;
        }

        int pointCount = pointCloud.Rows;
        (double minZ, double maxZ) = ResolveRange(pointCloud, pointCount);
        Mat colors = BuildColors(pointCloud, pointCount, minZ, maxZ);

        PointCloudData output = PointCloudUtils.BuildCloud(pointCloud.Clone(), colors);
        context.Set("output_point_cloud", output);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private (double minZ, double maxZ) ResolveRange(Mat pointCloud, int pointCount)
    {
        if (!_autoRange)
        {
            double manualMax = _maxZ;
            if (manualMax <= _minZ)
            {
                manualMax = _minZ + 1d;
            }

            return (_minZ, manualMax);
        }

        double minZ = double.MaxValue;
        double maxZ = double.MinValue;
        for (int i = 0; i < pointCount; i++)
        {
            double z = pointCloud.Get<float>(i, 2);
            if (z < minZ)
            {
                minZ = z;
            }

            if (z > maxZ)
            {
                maxZ = z;
            }
        }

        if (maxZ <= minZ)
        {
            maxZ = minZ + 1d;
        }

        return (minZ, maxZ);
    }

    private Mat BuildColors(Mat pointCloud, int pointCount, double minZ, double maxZ)
    {
        using Mat normalized = new Mat(pointCount, 1, MatType.CV_8UC1);
        double range = maxZ - minZ;
        for (int i = 0; i < pointCount; i++)
        {
            double z = pointCloud.Get<float>(i, 2);
            double ratio = (z - minZ) / range;
            ratio = Math.Clamp(ratio, 0d, 1d);
            if (_invert)
            {
                ratio = 1d - ratio;
            }

            normalized.Set(i, 0, (byte)Math.Round(ratio * 255d));
        }

        using Mat colorized = new();
        Cv2.ApplyColorMap(normalized, colorized, ResolveColorMap(_colorMap));

        Mat colors = new Mat(pointCount, 3, MatType.CV_8UC1);
        for (int i = 0; i < pointCount; i++)
        {
            Vec3b color = colorized.Get<Vec3b>(i, 0);
            colors.Set(i, 0, color.Item0);
            colors.Set(i, 1, color.Item1);
            colors.Set(i, 2, color.Item2);
        }

        return colors;
    }

    private static ColormapTypes ResolveColorMap(string colorMap)
    {
        return colorMap.Trim().ToLowerInvariant() switch
        {
            "turbo" => ColormapTypes.Turbo,
            "hot" => ColormapTypes.Hot,
            "hsv" => ColormapTypes.Hsv,
            _ => ColormapTypes.Jet,
        };
    }
}
