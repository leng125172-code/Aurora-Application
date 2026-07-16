namespace AuroraStruct3D.OpenCV.PointCloudOps;

using System.Text.Json;
using AuroraStruct3D.OpenCV.RoiOps;

/// <summary>
/// 工作流算子：3D 点云裁剪。
/// <para>
/// 支持四种裁剪模式：
/// <list type="bullet">
///   <item><b>box</b> — 轴对齐包围盒裁剪（min/max X/Y/Z）</item>
///   <item><b>sphere</b> — 球体裁剪（中心点 + 半径）</item>
///   <item><b>plane</b> — 平面裁剪（保留平面一侧的点，上方/下方）</item>
///   <item><b>mask</b> — 2D 掩膜裁剪（将 3D 点投影到 XY 平面，保留掩膜白色区域内的点）</item>
/// </list>
/// 掩膜模式可与 <see cref="roi_partition"/> 算子配合，用 2D ROI 分区掩膜裁剪 3D 点云区域。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_point_cloud</c>（PointCloudData）— 输入点云</item>
///   <item>输入 <c>roi_mask</c>（Mat）— 2D ROI 掩膜（仅 mask 模式使用，8UC1）</item>
///   <item>输出 <c>output_point_cloud</c>（PointCloudData）— 裁剪后的点云</item>
/// </list>
/// </para>
/// </summary>
[Guid("b1c2d3e4-0001-4000-8000-000000000101")]
[Category("3D点云预处理")]
[DisplayName("点云裁剪")]
[Description("用包围盒裁出关心的那块点云，切掉多余部分。")]
public class point_cloud_crop : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "input_point_cloud", DisplayName = "输入点云" },
            new MatImg() { ParameterName = "roi_mask", DisplayName = "ROI掩膜" },
            new VisionParameter<string>
            {
                ParameterName = "roi_metadata",
                ParameterType = typeof(string),
                DisplayName = "ROI元数据",
                ControlType = PortControlType.Variable,
            },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "output_point_cloud", DisplayName = "输出点云" },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "cropMode",
                DisplayName = "裁剪模式",
                ParameterType = typeof(string),
                DefaultValue = "box",
                ValueLimit = new[] { "box", "sphere", "plane", "mask" },
                Required = true,
                ControlType = PortControlType.Select,
            },
            // ── box 模式参数 ──
            new ConfigParameter
            {
                Name = "minX",
                DisplayName = "最小X",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maxX",
                DisplayName = "最大X",
                ParameterType = typeof(double),
                DefaultValue = "100",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "minY",
                DisplayName = "最小Y",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maxY",
                DisplayName = "最大Y",
                ParameterType = typeof(double),
                DefaultValue = "100",
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
                DefaultValue = "100",
                Required = false,
                ControlType = PortControlType.Input,
            },
            // ── sphere 模式参数 ──
            new ConfigParameter
            {
                Name = "centerX",
                DisplayName = "球心X",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "centerY",
                DisplayName = "球心Y",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "centerZ",
                DisplayName = "球心Z",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "radius",
                DisplayName = "球半径",
                ParameterType = typeof(double),
                DefaultValue = "10",
                Required = false,
                ControlType = PortControlType.Input,
            },
            // ── plane 模式参数 ──
            new ConfigParameter
            {
                Name = "planeA",
                DisplayName = "法向A",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "planeB",
                DisplayName = "法向B",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "planeC",
                DisplayName = "法向C",
                ParameterType = typeof(double),
                DefaultValue = "1",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "planeD",
                DisplayName = "截距D",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "keepAbove",
                DisplayName = "保留上方",
                ParameterType = typeof(bool),
                DefaultValue = "true",
                Required = false,
                ControlType = PortControlType.Input,
            },
            // ── mask 模式参数 ──
            new ConfigParameter
            {
                Name = "maskWorldMinX",
                DisplayName = "世界最小X",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maskWorldMaxX",
                DisplayName = "世界最大X",
                ParameterType = typeof(double),
                DefaultValue = "100",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maskWorldMinY",
                DisplayName = "世界最小Y",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maskWorldMaxY",
                DisplayName = "世界最大Y",
                ParameterType = typeof(double),
                DefaultValue = "100",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maskThreshold",
                DisplayName = "掩膜阈值",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly string _cropMode;
    private readonly double _minX,
        _maxX,
        _minY,
        _maxY,
        _minZ,
        _maxZ;
    private readonly double _centerX,
        _centerY,
        _centerZ,
        _radius;
    private readonly double _planeA,
        _planeB,
        _planeC,
        _planeD;
    private readonly bool _keepAbove;
    private readonly double _maskWorldMinX,
        _maskWorldMaxX,
        _maskWorldMinY,
        _maskWorldMaxY,
        _maskThreshold;
    private bool _disposed;

    public point_cloud_crop(
        string cropMode = "box",
        double minX = 0,
        double maxX = 100,
        double minY = 0,
        double maxY = 100,
        double minZ = 0,
        double maxZ = 100,
        double centerX = 0,
        double centerY = 0,
        double centerZ = 0,
        double radius = 10,
        double planeA = 0,
        double planeB = 0,
        double planeC = 1,
        double planeD = 0,
        bool keepAbove = true,
        double maskWorldMinX = 0,
        double maskWorldMaxX = 100,
        double maskWorldMinY = 0,
        double maskWorldMaxY = 100,
        double maskThreshold = 0
    )
    {
        _cropMode = cropMode;
        _minX = minX;
        _maxX = maxX;
        _minY = minY;
        _maxY = maxY;
        _minZ = minZ;
        _maxZ = maxZ;
        _centerX = centerX;
        _centerY = centerY;
        _centerZ = centerZ;
        _radius = radius;
        _planeA = planeA;
        _planeB = planeB;
        _planeC = planeC;
        _planeD = planeD;
        _keepAbove = keepAbove;
        _maskWorldMinX = maskWorldMinX;
        _maskWorldMaxX = maskWorldMaxX;
        _maskWorldMinY = maskWorldMinY;
        _maskWorldMaxY = maskWorldMaxY;
        _maskThreshold = maskThreshold;
    }

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        PointCloudData input =
            context.Get<PointCloudData>("input_point_cloud")
            ?? throw new InvalidOperationException(
                "上下文变量 'input_point_cloud' 为空，请确认输入绑定已正确设置。"
            );

        Mat pointCloud = input.PointCloud!;
        if (pointCloud is null || pointCloud.Empty())
            throw new InvalidOperationException("输入点云为空，无法执行裁剪。");

        int pointCount = pointCloud.Rows;
        int colCount = pointCloud.Cols;

        List<int> inlierIndices = _cropMode switch
        {
            "box" => CropByBox(pointCloud, pointCount),
            "sphere" => CropBySphere(pointCloud, pointCount),
            "plane" => CropByPlane(pointCloud, pointCount),
            "mask" => CropByMask(context, pointCloud, pointCount),
            _ => throw new InvalidOperationException(
                $"不支持的裁剪模式：{_cropMode}。支持的模式：box、sphere、plane、mask。"
            ),
        };

        if (inlierIndices.Count == 0)
            throw new InvalidOperationException(
                $"裁剪后点云为空（模式：{_cropMode}），请调整裁剪参数。"
            );

        // 构建裁剪后的点云
        Mat result = new Mat(inlierIndices.Count, colCount, MatType.CV_32FC1);
        for (int i = 0; i < inlierIndices.Count; i++)
        for (int c = 0; c < colCount; c++)
            result.Set(i, c, pointCloud.Get<float>(inlierIndices[i], c));

        var output = new PointCloudData();
        output.Value = result;

        // 保留颜色信息
        if (input.HasColors && input.Colors != null)
        {
            Mat colors = new Mat(inlierIndices.Count, 3, MatType.CV_8UC3);
            for (int i = 0; i < inlierIndices.Count; i++)
            for (int c = 0; c < 3; c++)
                colors.Set<byte>(i, c, input.Colors.Get<byte>(inlierIndices[i], c));
            output.SetColors(colors);
        }

        context.Set("output_point_cloud", output);
    }

    /// <summary>包围盒裁剪：保留在 [min, max] 范围内的点。</summary>
    private List<int> CropByBox(Mat cloud, int count)
    {
        List<int> indices = new();
        for (int i = 0; i < count; i++)
        {
            float x = cloud.Get<float>(i, 0);
            float y = cloud.Get<float>(i, 1);
            float z = cloud.Get<float>(i, 2);
            if (x >= _minX && x <= _maxX && y >= _minY && y <= _maxY && z >= _minZ && z <= _maxZ)
                indices.Add(i);
        }
        return indices;
    }

    /// <summary>球体裁剪：保留到球心距离 ≤ 半径的点。</summary>
    private List<int> CropBySphere(Mat cloud, int count)
    {
        List<int> indices = new();
        double r2 = _radius * _radius;
        for (int i = 0; i < count; i++)
        {
            double dx = cloud.Get<float>(i, 0) - _centerX;
            double dy = cloud.Get<float>(i, 1) - _centerY;
            double dz = cloud.Get<float>(i, 2) - _centerZ;
            if (dx * dx + dy * dy + dz * dz <= r2)
                indices.Add(i);
        }
        return indices;
    }

    /// <summary>平面裁剪：保留平面一侧的点（上方/下方由 keepAbove 控制）。</summary>
    private List<int> CropByPlane(Mat cloud, int count)
    {
        List<int> indices = new();
        for (int i = 0; i < count; i++)
        {
            double signedDist =
                _planeA * cloud.Get<float>(i, 0)
                + _planeB * cloud.Get<float>(i, 1)
                + _planeC * cloud.Get<float>(i, 2)
                + _planeD;
            bool inside = _keepAbove ? signedDist >= 0 : signedDist <= 0;
            if (inside)
                indices.Add(i);
        }
        return indices;
    }

    /// <summary>
    /// 2D 掩膜裁剪：将 3D 点投影到 XY 平面，保留掩膜白色区域内的点。
    /// <para>
    /// 世界坐标到像素坐标的映射使用线性变换：
    /// pixelX = (worldX - worldMinX) / (worldMaxX - worldMinX) × maskWidth
    /// pixelY = (worldY - worldMinY) / (worldMaxY - worldMinY) × maskHeight
    /// </para>
    /// </summary>
    private List<int> CropByMask(IWorkflowContext context, Mat cloud, int count)
    {
        Mat? mask = context.Get<Mat>("roi_mask");
        if (mask is null || mask.Empty())
            throw new InvalidOperationException(
                "掩膜裁剪模式下，必须提供 'roi_mask' 输入（8UC1 二值掩膜）。"
            );

        int maskWidth = mask.Width;
        int maskHeight = mask.Height;

        (
            double worldMinX,
            double worldMaxX,
            double worldMinY,
            double worldMaxY,
            int axisX,
            int axisY
        ) = ResolveMaskMapping(context, cloud, count, maskWidth, maskHeight);

        double worldRangeX = worldMaxX - worldMinX;
        double worldRangeY = worldMaxY - worldMinY;
        if (Math.Abs(worldRangeX) < 1e-10 || Math.Abs(worldRangeY) < 1e-10)
            throw new InvalidOperationException(
                "掩膜裁剪的世界坐标范围无效，请设置 maskWorldMinX/MaxX/MinY/MaxY。"
            );

        List<int> indices = new();
        for (int i = 0; i < count; i++)
        {
            float worldX = cloud.Get<float>(i, axisX);
            float worldY = cloud.Get<float>(i, axisY);

            // 世界坐标 → 像素坐标
            int px = (int)((worldX - worldMinX) / worldRangeX * (maskWidth - 1));
            int py = (int)((worldY - worldMinY) / worldRangeY * (maskHeight - 1));

            // 边界检查
            if (px < 0 || px >= maskWidth || py < 0 || py >= maskHeight)
                continue;

            // 检查掩膜像素值（白色 = 255 = 在 ROI 内）
            if (mask.Get<byte>(py, px) > _maskThreshold)
                indices.Add(i);
        }
        return indices;
    }

    private (
        double worldMinX,
        double worldMaxX,
        double worldMinY,
        double worldMaxY,
        int axisX,
        int axisY
    ) ResolveMaskMapping(
        IWorkflowContext context,
        Mat cloud,
        int cloudPointCount,
        int maskWidth,
        int maskHeight
    )
    {
        string? metadataJson = context.Get<string>("roi_metadata");
        RoiProjectionMapping? mapping = null;
        int axisX = 0,
            axisY = 1;

        if (!string.IsNullOrWhiteSpace(metadataJson))
        {
            try
            {
                RoiPartitionMetadata? metadata = JsonSerializer.Deserialize<RoiPartitionMetadata>(
                    metadataJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );
                mapping = metadata?.ProjectionMapping;
                if (mapping is not null)
                {
                    (axisX, axisY) = ResolveAxesFromViewLabel(mapping.ViewLabel);

                    if (mapping.ImageWidth > 0 && mapping.ImageWidth != maskWidth)
                    {
                        throw new InvalidOperationException(
                            $"ROI 映射宽度 {mapping.ImageWidth} 与 roi_mask 宽度 {maskWidth} 不一致。"
                        );
                    }

                    if (mapping.ImageHeight > 0 && mapping.ImageHeight != maskHeight)
                    {
                        throw new InvalidOperationException(
                            $"ROI 映射高度 {mapping.ImageHeight} 与 roi_mask 高度 {maskHeight} 不一致。"
                        );
                    }
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"roi_metadata 解析失败，无法建立 ROI 到模型映射：{ex.Message}"
                );
            }
        }

        // 优先使用 ROI 元数据中的映射范围，否则使用配置参数，最后回退到点云实际范围
        if (
            mapping is not null
            && mapping.WorldMaxX > mapping.WorldMinX
            && mapping.WorldMaxY > mapping.WorldMinY
        )
        {
            return (
                mapping.WorldMinX,
                mapping.WorldMaxX,
                mapping.WorldMinY,
                mapping.WorldMaxY,
                axisX,
                axisY
            );
        }

        if (_maskWorldMaxX > _maskWorldMinX && _maskWorldMaxY > _maskWorldMinY)
        {
            // 检查配置参数是否与点云实际范围匹配，如果差异过大则回退到自动计算
            (double cloudMinX, double cloudMaxX, double cloudMinY, double cloudMaxY, _, _) =
                ComputeCloudRange(cloud, cloudPointCount, axisX, axisY);

            double configRangeX = _maskWorldMaxX - _maskWorldMinX;
            double configRangeY = _maskWorldMaxY - _maskWorldMinY;
            double cloudRangeX = cloudMaxX - cloudMinX;
            double cloudRangeY = cloudMaxY - cloudMinY;

            // 如果配置参数范围与点云实际范围差异超过 10 倍，说明配置参数可能是默认值，回退到自动计算
            if (
                cloudRangeX > 0
                && cloudRangeY > 0
                && (configRangeX / cloudRangeX > 10 || configRangeY / cloudRangeY > 10)
            )
            {
                return (cloudMinX, cloudMaxX, cloudMinY, cloudMaxY, axisX, axisY);
            }

            return (_maskWorldMinX, _maskWorldMaxX, _maskWorldMinY, _maskWorldMaxY, axisX, axisY);
        }

        return ComputeCloudRange(cloud, cloudPointCount, axisX, axisY);
    }

    /// <summary>
    /// 从点云数据中自动计算指定轴的实际范围，作为掩膜映射的回退方案。
    /// </summary>
    private static (
        double worldMinX,
        double worldMaxX,
        double worldMinY,
        double worldMaxY,
        int axisX,
        int axisY
    ) ComputeCloudRange(Mat cloud, int count, int axisX, int axisY)
    {
        float minX = float.MaxValue,
            maxX = float.MinValue;
        float minY = float.MaxValue,
            maxY = float.MinValue;

        for (int i = 0; i < count; i++)
        {
            float vx = cloud.Get<float>(i, axisX);
            float vy = cloud.Get<float>(i, axisY);
            if (vx < minX)
                minX = vx;
            if (vx > maxX)
                maxX = vx;
            if (vy < minY)
                minY = vy;
            if (vy > maxY)
                maxY = vy;
        }

        // 稍微扩展边界，避免边界点因取整被裁剪
        const double margin = 0.01;
        return (minX - margin, maxX + margin, minY - margin, maxY + margin, axisX, axisY);
    }

    private static (int axisX, int axisY) ResolveAxesFromViewLabel(string? viewLabel)
    {
        string normalized = (viewLabel ?? "XY").Trim().ToUpperInvariant();
        return normalized switch
        {
            "XY" => (0, 1),
            "XZ" => (0, 2),
            "YZ" => (1, 2),
            _ => throw new InvalidOperationException(
                $"不支持的 ROI 视图映射标签：{viewLabel}，仅支持 XY/XZ/YZ。"
            ),
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
