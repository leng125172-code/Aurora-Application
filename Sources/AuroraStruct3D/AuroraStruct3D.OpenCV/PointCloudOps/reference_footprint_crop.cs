using System.Text.Json;
using AuroraStruct3D.OpenCV.Common;

namespace AuroraStruct3D.OpenCV.PointCloudOps;

/// <summary>
/// 按参考数模的 XY 投影足迹过滤已经完成固定姿态变换的扫描点云。
/// 典型用途是排除产品孔洞中可见的工装表面，以及产品轮廓外的背景点。
/// </summary>
[Guid("b1a1000b-000b-4000-8000-00000000003b")]
[Category("3D点云预处理")]
[DisplayName("参考数模轮廓裁剪")]
[Description("按数模顶视投影轮廓保留产品点，排除孔洞下方工装和轮廓外背景。")]
public sealed class reference_footprint_crop : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData { ParameterName = "source_cloud", DisplayName = "已对齐扫描点云" },
            new PointCloudData { ParameterName = "reference_cloud", DisplayName = "参考数模点云" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new PointCloudData { ParameterName = "filtered_cloud", DisplayName = "产品区域点云" },
            new VisionParameter<string>
            {
                ParameterName = "filter_result",
                DisplayName = "轮廓裁剪统计",
                ParameterType = typeof(string),
                ControlType = PortControlType.Variable,
            },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "lateralToleranceMm",
                DisplayName = "轮廓横向容差(mm)",
                ParameterType = typeof(double),
                DefaultValue = "1.0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "minimumKeptRatio",
                DisplayName = "最小产品点保留比例",
                ParameterType = typeof(double),
                DefaultValue = "0.1",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly double _lateralToleranceMm;
    private readonly double _minimumKeptRatio;
    private bool _disposed;

    public reference_footprint_crop(
        double lateralToleranceMm = 1.0,
        double minimumKeptRatio = 0.1
    )
    {
        if (!double.IsFinite(lateralToleranceMm) || lateralToleranceMm <= 0)
            throw new ArgumentOutOfRangeException(nameof(lateralToleranceMm));
        if (!double.IsFinite(minimumKeptRatio) || minimumKeptRatio is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(minimumKeptRatio));
        _lateralToleranceMm = lateralToleranceMm;
        _minimumKeptRatio = minimumKeptRatio;
    }

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        PointCloudData sourceData =
            context.Get<PointCloudData>("source_cloud")
            ?? throw new InvalidOperationException("上下文变量 'source_cloud' 为空。");
        PointCloudData referenceData =
            context.Get<PointCloudData>("reference_cloud")
            ?? throw new InvalidOperationException("上下文变量 'reference_cloud' 为空。");
        Mat source = ValidateCloud(sourceData.PointCloud, "扫描");
        Mat reference = ValidateCloud(referenceData.PointCloud, "参考数模");

        (float[] referenceX, float[] referenceY) = ExtractReferenceFootprint(reference);
        var referenceZ = new float[referenceX.Length];
        var footprintTree = new KdTree3D(referenceX, referenceY, referenceZ);
        int sourceRows = source.Rows;
        int referenceRows = reference.Rows;
        var keep = new bool[sourceRows];
        Parallel.For(
            0,
            sourceRows,
            index =>
            {
                float x = source.Get<float>(index, 0);
                float y = source.Get<float>(index, 1);
                if (!float.IsFinite(x) || !float.IsFinite(y))
                    return;
                keep[index] =
                    footprintTree.FindNearestNeighborWithin(
                        x,
                        y,
                        0,
                        _lateralToleranceMm,
                        out _
                    ) >= 0;
            }
        );

        int keptCount = keep.Count(value => value);
        double keptRatio = sourceRows > 0 ? (double)keptCount / sourceRows : 0;
        if (keptCount < 3 || keptRatio < _minimumKeptRatio)
            throw new InvalidOperationException(
                $"参考数模轮廓裁剪后仅保留 {keptCount}/{sourceRows} 点（{keptRatio:P2}），"
                    + $"低于要求 {_minimumKeptRatio:P2}。请检查固定姿态或轮廓容差。"
            );

        Mat filtered = CopyRows(source, keep, keptCount);
        Mat? filteredColors =
            sourceData.HasColors && sourceData.Colors is not null
                ? CopyRows(sourceData.Colors, keep, keptCount)
                : null;
        context.Set("filtered_cloud", PointCloudUtils.BuildCloud(filtered, filteredColors));
        context.Set(
            "filter_result",
            JsonSerializer.Serialize(
                new FilterResult
                {
                    SourcePointCount = sourceRows,
                    ReferencePointCount = referenceRows,
                    KeptPointCount = keptCount,
                    IgnoredPointCount = sourceRows - keptCount,
                    KeptRatio = keptRatio,
                    LateralToleranceMm = _lateralToleranceMm,
                },
                JsonOptions
            )
        );
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static Mat ValidateCloud(Mat? cloud, string name)
    {
        if (cloud is null || cloud.Empty() || cloud.Rows < 3 || cloud.Cols < 3)
            throw new InvalidOperationException($"{name}点云为空或有效点不足 3 个。");
        return cloud;
    }

    private static (float[] x, float[] y) ExtractReferenceFootprint(Mat reference)
    {
        int rows = reference.Rows;
        var x = new List<float>(rows);
        var y = new List<float>(rows);
        for (int index = 0; index < rows; index++)
        {
            float px = reference.Get<float>(index, 0);
            float py = reference.Get<float>(index, 1);
            if (!float.IsFinite(px) || !float.IsFinite(py))
                continue;
            x.Add(px);
            y.Add(py);
        }
        if (x.Count < 3)
            throw new InvalidOperationException("参考数模没有足够的有限 XY 坐标。");
        return (x.ToArray(), y.ToArray());
    }

    private static unsafe Mat CopyRows(Mat source, bool[] keep, int keptCount)
    {
        int rows = source.Rows;
        int rowBytes = checked(source.Cols * source.ElemSize());
        long sourceStep = source.Step();
        Mat result = new(keptCount, source.Cols, source.Type());
        long resultStep = result.Step();
        byte* sourceBase = source.DataPointer;
        byte* resultBase = result.DataPointer;
        int outputRow = 0;
        for (int sourceRow = 0; sourceRow < rows; sourceRow++)
        {
            if (!keep[sourceRow])
                continue;
            Buffer.MemoryCopy(
                sourceBase + sourceRow * sourceStep,
                resultBase + outputRow * resultStep,
                rowBytes,
                rowBytes
            );
            outputRow++;
        }
        return result;
    }

    private sealed class FilterResult
    {
        public int SourcePointCount { get; init; }
        public int ReferencePointCount { get; init; }
        public int KeptPointCount { get; init; }
        public int IgnoredPointCount { get; init; }
        public double KeptRatio { get; init; }
        public double LateralToleranceMm { get; init; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
