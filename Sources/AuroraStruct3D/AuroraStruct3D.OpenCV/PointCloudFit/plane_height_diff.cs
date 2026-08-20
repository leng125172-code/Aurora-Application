using System.Text.Json;
using AuroraStruct3D.OpenCV.RoiOps;

namespace AuroraStruct3D.OpenCV.PointCloudFit;

/// <summary>
/// 工作流算子：平面高度差计算。
/// <para>
/// 计算目标平面与基准平面之间的高度差（目标平面内点质心到基准平面的有符号距离），
/// 并判断是否落在给定阈值区间内。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>ref_plane_params</c>（Mat）— 基准平面参数 [a,b,c,d]，4×1 CV_64FC1</item>
///   <item>输入 <c>target_plane_params</c>（Mat）— 目标平面参数 [a,b,c,d]，4×1 CV_64FC1</item>
///   <item>输入 <c>target_cloud</c>（PointCloudData）— 目标区域点云，用于计算内点质心</item>
///   <item>输出 <c>signed_diff</c>（double）— 带符号高度差</item>
///   <item>输出 <c>abs_diff</c>（double）— 绝对高度差</item>
///   <item>输出 <c>is_ok</c>（bool）— 是否在阈值范围内</item>
///   <item>输出 <c>result_json</c>（string）— 判定结果 JSON</item>
/// </list>
/// </para>
/// </summary>
[Guid("f1a2b3c4-d5e6-7890-abcd-ef0123456789")]
[Category("3D拟合测量")]
[DisplayName("平面高度差计算")]
[Description("计算目标平面与基准平面之间的高度差，判断是否在阈值区间内。")]
public class plane_height_diff : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new MatImg { ParameterName = "ref_plane_params", DisplayName = "基准平面参数" },
            new MatImg { ParameterName = "target_plane_params", DisplayName = "目标平面参数" },
            new PointCloudData { ParameterName = "ref_cloud", DisplayName = "基准区域点云" },
            new PointCloudData { ParameterName = "target_cloud", DisplayName = "目标区域点云" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        [InspectionResults.Output<PlaneHeightDiffInspectionDetails>("平面高度差结果")];

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "refRegionName",
                DisplayName = "基准区域名称",
                ParameterType = typeof(string),
                DefaultValue = "A",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "targetRegionName",
                DisplayName = "目标区域名称",
                ParameterType = typeof(string),
                DefaultValue = "B",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "minDiff",
                DisplayName = "最小差值",
                ParameterType = typeof(double),
                DefaultValue = "-0.5",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maxDiff",
                DisplayName = "最大差值",
                ParameterType = typeof(double),
                DefaultValue = "0.5",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "refRoiMetadata",
                DisplayName = "基准区域元数据（可选）",
                ParameterType = typeof(string),
                DefaultValue = "",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "targetRoiMetadata",
                DisplayName = "目标区域元数据（可选）",
                ParameterType = typeof(string),
                DefaultValue = "",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly string _refRegionName;
    private readonly string _targetRegionName;
    private readonly double _minDiff;
    private readonly double _maxDiff;
    private readonly string _refRoiMetadata;
    private readonly string _targetRoiMetadata;
    private bool _disposed;

    public plane_height_diff(
        string refRegionName = "A",
        string targetRegionName = "B",
        double minDiff = -0.5,
        double maxDiff = 0.5,
        string refRoiMetadata = "",
        string targetRoiMetadata = ""
    )
    {
        if (!double.IsFinite(minDiff) || !double.IsFinite(maxDiff) || minDiff > maxDiff)
            throw new ArgumentException("高度差阈值必须为有限数值，且最小值不能大于最大值。");
        _refRegionName = refRegionName;
        _targetRegionName = targetRegionName;
        _minDiff = minDiff;
        _maxDiff = maxDiff;
        _refRoiMetadata = refRoiMetadata;
        _targetRoiMetadata = targetRoiMetadata;
    }

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // 读取基准平面参数
        Mat? refPlaneParams = context.Get<Mat>("ref_plane_params");
        if (refPlaneParams is null || refPlaneParams.Empty() || refPlaneParams.Rows < 4)
        {
            throw new InvalidOperationException("基准平面参数为空，无法计算高度差。");
        }

        double aRef = refPlaneParams.Get<double>(0, 0);
        double bRef = refPlaneParams.Get<double>(1, 0);
        double cRef = refPlaneParams.Get<double>(2, 0);
        double dRef = refPlaneParams.Get<double>(3, 0);

        // 基准平面法向量归一化
        double normRef = Math.Sqrt(aRef * aRef + bRef * bRef + cRef * cRef);
        if (normRef > 1e-10)
        {
            aRef /= normRef;
            bRef /= normRef;
            cRef /= normRef;
            dRef /= normRef;
        }

        // 读取目标平面参数
        Mat? targetPlaneParams = context.Get<Mat>("target_plane_params");
        if (targetPlaneParams is null || targetPlaneParams.Empty() || targetPlaneParams.Rows < 4)
        {
            throw new InvalidOperationException("目标平面参数为空，无法计算高度差。");
        }

        double aTgt = targetPlaneParams.Get<double>(0, 0);
        double bTgt = targetPlaneParams.Get<double>(1, 0);
        double cTgt = targetPlaneParams.Get<double>(2, 0);
        double dTgt = targetPlaneParams.Get<double>(3, 0);

        // 目标平面法向量归一化
        double normTgt = Math.Sqrt(aTgt * aTgt + bTgt * bTgt + cTgt * cTgt);
        if (normTgt > 1e-10)
        {
            aTgt /= normTgt;
            bTgt /= normTgt;
            cTgt /= normTgt;
            dTgt /= normTgt;
        }

        // 计算基准点云质心
        PointCloudData refCloud =
            context.Get<PointCloudData>("ref_cloud")
            ?? throw new InvalidOperationException("基准点云为空，无法计算质心。");

        Mat refPoints = refCloud.PointCloud!;
        if (refPoints is null || refPoints.Empty())
        {
            throw new InvalidOperationException("基准点云数据为空，无法计算质心。");
        }

        int refPointCount = refPoints.Rows;
        double refCx = 0,
            refCy = 0,
            refCz = 0;
        for (int i = 0; i < refPointCount; i++)
        {
            refCx += refPoints.Get<float>(i, 0);
            refCy += refPoints.Get<float>(i, 1);
            refCz += refPoints.Get<float>(i, 2);
        }
        refCx /= refPointCount;
        refCy /= refPointCount;
        refCz /= refPointCount;

        // 计算目标点云质心（用于计算高度差）
        PointCloudData targetCloud =
            context.Get<PointCloudData>("target_cloud")
            ?? throw new InvalidOperationException("目标点云为空，无法计算质心。");

        Mat targetPoints = targetCloud.PointCloud!;
        if (targetPoints is null || targetPoints.Empty())
        {
            throw new InvalidOperationException("目标点云数据为空，无法计算质心。");
        }

        int pointCount = targetPoints.Rows;
        double cx = 0,
            cy = 0,
            cz = 0;
        for (int i = 0; i < pointCount; i++)
        {
            cx += targetPoints.Get<float>(i, 0);
            cy += targetPoints.Get<float>(i, 1);
            cz += targetPoints.Get<float>(i, 2);
        }
        cx /= pointCount;
        cy /= pointCount;
        cz /= pointCount;

        // 目标平面质心到基准平面的有符号距离
        double signedDiff = Math.Round(aRef * cx + bRef * cy + cRef * cz + dRef, 6);
        double absDiff = Math.Round(Math.Abs(signedDiff), 6);
        bool isOk = signedDiff >= _minDiff && signedDiff <= _maxDiff;
        string refRegionName = RoiMetadataNameResolver.Resolve(
            _refRoiMetadata,
            _refRegionName,
            "refRoiMetadata"
        );
        string targetRegionName = RoiMetadataNameResolver.Resolve(
            _targetRoiMetadata,
            _targetRegionName,
            "targetRoiMetadata"
        );

        var absDiffResult = new
        {
            refRegion = refRegionName,
            targetRegion = targetRegionName,
            x = Math.Round(cx, 4),
            y = Math.Round(cy, 4),
            heightDiff = absDiff,
        };

        var result = new PlaneHeightDiffInspectionDetails(
            new RegionPointDetails(refRegionName, Math.Round(refCx, 4), Math.Round(refCy, 4), Math.Round(refCz, 4)),
            new RegionPointDetails(targetRegionName, Math.Round(cx, 4), Math.Round(cy, 4), Math.Round(cz, 4)),
            signedDiff,
            absDiff,
            _minDiff,
            _maxDiff,
            isOk);

        context.Set("result", InspectionResults.CreateTyped(true, isOk, result));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
