using System.Text.Json;
using AuroraStruct3D.OpenCV.Common;

namespace AuroraStruct3D.OpenCV.PointCloudDistance;

[Guid("a1b2c3d4-0003-4000-8000-000000000015")]
[Category("3D拟合测量")]
[DisplayName("3D比较")]
[Description("拿实际产品点云和合格品数模比，算高度差和位置差，检缺陷用。")]
public class cloud_compare : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "source_cloud", DisplayName = "源点云（待检测）" },
            new PointCloudData() { ParameterName = "target_cloud", DisplayName = "目标点云（参考）" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new PointCloudData() { ParameterName = "aligned_cloud", DisplayName = "配准点云" },
            new MatImg() { ParameterName = "transform_matrix", DisplayName = "变换矩阵" },
            new MatImg() { ParameterName = "distance_mat", DisplayName = "距离矩阵" },
            new MatImg() { ParameterName = "distance_image", DisplayName = "距离图像" },
            InspectionResults.Output<CloudCompareResult>("点云比较结果"),
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "useCoarseRegistration",
                DisplayName = "启用粗配准",
                ParameterType = typeof(bool),
                DefaultValue = "true",
                Required = false,
                ControlType = PortControlType.Switch,
            },
            new ConfigParameter
            {
                Name = "registrationMethod",
                DisplayName = "精配准方法",
                ParameterType = typeof(string),
                ValueLimit = new[] { "icp", "point_to_plane", "ndt" },
                DefaultValue = "point_to_plane",
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "maxIterations",
                DisplayName = "最大迭代次数",
                ParameterType = typeof(int),
                DefaultValue = "50",
                ValueLimit = new[] { "10", "20", "50", "100", "200" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "maxCorrespondenceDistance",
                DisplayName = "最大对应点距离(mm，0=自动)",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "distanceThreshold",
                DisplayName = "缺陷阈值",
                ParameterType = typeof(double),
                DefaultValue = null,
                Required = true,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "imageResolution",
                DisplayName = "投影分辨率",
                ParameterType = typeof(int),
                DefaultValue = "512",
                ValueLimit = new[] { "256", "512", "1024", "2048" },
                Required = false,
                ControlType = PortControlType.Select,
            },
            new ConfigParameter
            {
                Name = "voxelSize",
                DisplayName = "体素下采样尺寸",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maxDefectRatio",
                DisplayName = "最大超差点比例",
                ParameterType = typeof(double),
                DefaultValue = null,
                Required = true,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maxMeanDistance",
                DisplayName = "最大平均偏差",
                ParameterType = typeof(double),
                DefaultValue = null,
                Required = true,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maxMissingRatio",
                DisplayName = "最大缺失点比例",
                ParameterType = typeof(double),
                DefaultValue = null,
                Required = true,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "minCoarseInlierRatio",
                DisplayName = "粗配准最小内点比例",
                ParameterType = typeof(double),
                DefaultValue = "0.1",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "minFineCorrespondenceRatio",
                DisplayName = "精配准最小对应比例",
                ParameterType = typeof(double),
                DefaultValue = "0.15",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "maxRegistrationRmse",
                DisplayName = "最大配准RMSE(mm，0=自动)",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly bool _useCoarseRegistration;
    private readonly string _registrationMethod;
    private readonly int _maxIterations;
    private readonly double _maxCorrespondenceDistance;
    private readonly double _distanceThreshold;
    private readonly int _imageResolution;
    private readonly double _voxelSize;
    private readonly double _maxDefectRatio;
    private readonly double _maxMeanDistance;
    private readonly double _maxMissingRatio;
    private readonly double _minCoarseInlierRatio;
    private readonly double _minFineCorrespondenceRatio;
    private readonly double _maxRegistrationRmse;
    private bool _disposed;

    private const int RegistrationPointLimit = 2_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public cloud_compare(
        bool useCoarseRegistration = true,
        string registrationMethod = "point_to_plane",
        int maxIterations = 50,
        double maxCorrespondenceDistance = 0,
        double distanceThreshold = 0.05,
        int imageResolution = 512,
        double voxelSize = 0,
        double maxDefectRatio = 0,
        double maxMeanDistance = double.MaxValue,
        double maxMissingRatio = 0,
        double minCoarseInlierRatio = 0.1,
        double minFineCorrespondenceRatio = 0.15,
        double maxRegistrationRmse = 0
    )
    {
        _useCoarseRegistration = useCoarseRegistration;
        _registrationMethod = registrationMethod;
        _maxIterations = maxIterations;
        _maxCorrespondenceDistance = maxCorrespondenceDistance;
        _distanceThreshold = distanceThreshold;
        _imageResolution = imageResolution;
        _voxelSize = voxelSize;
        if (
            maxIterations <= 0
            || !double.IsFinite(maxCorrespondenceDistance)
            || maxCorrespondenceDistance < 0
            || !double.IsFinite(voxelSize)
            || voxelSize < 0
            || !double.IsFinite(distanceThreshold)
            || distanceThreshold <= 0
            || maxDefectRatio < 0
            || maxDefectRatio > 1
            || maxMissingRatio < 0
            || maxMissingRatio > 1
            || double.IsNaN(maxMeanDistance)
            || maxMeanDistance <= 0
            || minCoarseInlierRatio < 0
            || minCoarseInlierRatio > 1
            || minFineCorrespondenceRatio <= 0
            || minFineCorrespondenceRatio > 1
            || !double.IsFinite(maxRegistrationRmse)
            || maxRegistrationRmse < 0
        )
            throw new ArgumentException("3D比较阈值配置无效。");
        if (!new[] { "icp", "point_to_plane", "ndt" }.Contains(registrationMethod, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("3D比较精配准方法无效。", nameof(registrationMethod));
        if (imageResolution <= 0)
            throw new ArgumentOutOfRangeException(nameof(imageResolution));
        _maxDefectRatio = maxDefectRatio;
        _maxMeanDistance = maxMeanDistance;
        _maxMissingRatio = maxMissingRatio;
        _minCoarseInlierRatio = minCoarseInlierRatio;
        _minFineCorrespondenceRatio = minFineCorrespondenceRatio;
        _maxRegistrationRmse = maxRegistrationRmse;
    }

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        PointCloudData sourceData =
            context.Get<PointCloudData>("source_cloud")
            ?? throw Failure(
                "POINT_CLOUD_SOURCE_NOT_AVAILABLE",
                "上下文变量 'source_cloud' 为空，请确认输入绑定已正确设置。"
            );

        PointCloudData targetData =
            context.Get<PointCloudData>("target_cloud")
            ?? throw Failure(
                "PRODUCT_MODEL_UNAVAILABLE",
                "上下文变量 'target_cloud' 为空，请确认输入绑定已正确设置。"
            );

        Mat sourceCloud = ValidateCloud(sourceData.PointCloud, "源", "POINT_CLOUD_SOURCE_NOT_AVAILABLE");
        Mat targetCloud = ValidateCloud(targetData.PointCloud, "目标", "PRODUCT_MODEL_UNAVAILABLE");
        int sourceCount = sourceCloud.Rows;
        int targetCount = targetCloud.Rows;

        double sourceDiagonal = BoundingBoxDiagonal(sourceCloud);
        double targetDiagonal = BoundingBoxDiagonal(targetCloud);
        double scaleRatio = sourceDiagonal / targetDiagonal;
        if (!double.IsFinite(scaleRatio) || scaleRatio < 0.01 || scaleRatio > 100)
            throw Failure(
                "POINT_CLOUD_UNIT_MISMATCH",
                $"源点云与目标点云尺寸相差过大（对角线 {sourceDiagonal:F4} mm / {targetDiagonal:F4} mm），请检查模型单位。"
            );

        double spacing = EstimatePointSpacing(targetCloud, targetDiagonal);
        double effectiveVoxelSize = _voxelSize > 0
            ? _voxelSize
            : Math.Clamp(2 * spacing, targetDiagonal / 1000.0, targetDiagonal / 100.0);
        double featureRadius = Math.Min(
            Math.Max(5 * effectiveVoxelSize, targetDiagonal * 0.03),
            targetDiagonal * 0.10
        );
        double coarseDistanceThreshold = Math.Min(
            Math.Max(2 * effectiveVoxelSize, targetDiagonal * 0.01),
            targetDiagonal * 0.05
        );
        double effectiveMaxCorrespondenceDistance = _maxCorrespondenceDistance > 0
            ? _maxCorrespondenceDistance
            : Math.Min(
                Math.Max(3 * effectiveVoxelSize, targetDiagonal * 0.02),
                targetDiagonal * 0.10
            );
        double effectiveMaxRegistrationRmse = _maxRegistrationRmse > 0
            ? _maxRegistrationRmse
            : Math.Max(_distanceThreshold, effectiveMaxCorrespondenceDistance * 0.5);

        using Mat workingSource = CreateRegistrationCloud(
            sourceCloud,
            effectiveVoxelSize,
            RegistrationPointLimit
        );
        using Mat workingTarget = CreateRegistrationCloud(
            targetCloud,
            effectiveVoxelSize,
            RegistrationPointLimit
        );
        if (workingSource.Rows < 3 || workingTarget.Rows < 3)
            throw Failure(
                "POINT_CLOUD_INVALID",
                "点云经配准降采样后点数不足，请减小体素尺寸。"
            );

        (float[] srcX, float[] srcY, float[] srcZ) = ExtractCoordinates(workingSource);
        (float[] tgtX, float[] tgtY, float[] tgtZ) = ExtractCoordinates(workingTarget);

        double[] coarseR = IdentityRotation();
        double[] coarseT = new double[3];
        CoarseRegistrationOutcome coarseOutcome = CoarseRegistrationOutcome.NotUsed;
        if (_useCoarseRegistration)
        {
            coarseOutcome = CoarseRegistration(
                srcX,
                srcY,
                srcZ,
                tgtX,
                tgtY,
                tgtZ,
                featureRadius,
                coarseDistanceThreshold
            );
            if (
                coarseOutcome.CorrespondenceCount < 3
                || coarseOutcome.InlierCount < 3
                || coarseOutcome.InlierRatio < _minCoarseInlierRatio
            )
                throw Failure(
                    "CLOUD_COMPARE_COARSE_REGISTRATION_FAILED",
                    $"粗配准质量不足：内点 {coarseOutcome.InlierCount}/{coarseOutcome.CorrespondenceCount}，"
                        + $"比例 {coarseOutcome.InlierRatio:P2}，要求至少 {_minCoarseInlierRatio:P2}。"
                );

            coarseR = coarseOutcome.Rotation;
            coarseT = coarseOutcome.Translation;
            ApplyTransform(srcX, srcY, srcZ, coarseR, coarseT);
        }

        RegistrationOutcome fineOutcome = _registrationMethod.ToLowerInvariant() switch
        {
            "icp" => IcpRegistration(
                srcX,
                srcY,
                srcZ,
                tgtX,
                tgtY,
                tgtZ,
                effectiveMaxCorrespondenceDistance,
                _maxIterations
            ),
            "point_to_plane" => PointToPlaneIcp(
                srcX,
                srcY,
                srcZ,
                tgtX,
                tgtY,
                tgtZ,
                effectiveMaxCorrespondenceDistance,
                _maxIterations
            ),
            "ndt" => NdtRegistration(
                srcX,
                srcY,
                srcZ,
                tgtX,
                tgtY,
                tgtZ,
                effectiveMaxCorrespondenceDistance,
                _maxIterations
            ),
            _ => throw new InvalidOperationException($"未知的配准方法: {_registrationMethod}"),
        };

        double fineCorrespondenceRatio =
            srcX.Length > 0 ? (double)fineOutcome.CorrespondenceCount / srcX.Length : 0;
        if (
            fineOutcome.CorrespondenceCount < 3
            || fineCorrespondenceRatio < _minFineCorrespondenceRatio
            || !double.IsFinite(fineOutcome.Rmse)
            || fineOutcome.Rmse > effectiveMaxRegistrationRmse
        )
            throw Failure(
                "CLOUD_COMPARE_FINE_REGISTRATION_FAILED",
                $"精配准质量不足：对应点 {fineOutcome.CorrespondenceCount}/{srcX.Length}，"
                    + $"比例 {fineCorrespondenceRatio:P2}，RMSE {fineOutcome.Rmse:F4} mm；"
                    + $"要求比例至少 {_minFineCorrespondenceRatio:P2}、RMSE 不超过 {effectiveMaxRegistrationRmse:F4} mm。"
            );

        double[] accumR = fineOutcome.Rotation;
        double[] accumT = fineOutcome.Translation;
        if (_useCoarseRegistration)
            (accumR, accumT) = ComposeTransform(accumR, accumT, coarseR, coarseT);

        Mat alignedCloud = TransformPointCloud(sourceCloud, accumR, accumT);

        Mat transformMatrix = Mat.Eye(4, 4, MatType.CV_64FC1);
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
                transformMatrix.Set(r, c, accumR[r * 3 + c]);
            transformMatrix.Set(r, 3, accumT[r]);
        }

        (float[] alignedX, float[] alignedY, float[] alignedZ) = ExtractCoordinates(alignedCloud);
        (float[] fullTargetX, float[] fullTargetY, float[] fullTargetZ) =
            ExtractCoordinates(targetCloud);
        DistanceOutcome forwardDistance = ComputeCloudToCloudDistance(
            alignedX,
            alignedY,
            alignedZ,
            fullTargetX,
            fullTargetY,
            fullTargetZ,
            effectiveMaxCorrespondenceDistance,
            _distanceThreshold
        );
        DistanceOutcome reverseDistance = ComputeCloudToCloudDistance(
            fullTargetX,
            fullTargetY,
            fullTargetZ,
            alignedX,
            alignedY,
            alignedZ,
            effectiveMaxCorrespondenceDistance,
            _distanceThreshold
        );
        reverseDistance.Distances.Dispose();

        if (forwardDistance.MatchedCount == 0)
        {
            forwardDistance.Distances.Dispose();
            alignedCloud.Dispose();
            transformMatrix.Dispose();
            throw Failure(
                "CLOUD_COMPARE_FINE_REGISTRATION_FAILED",
                "配准后完整源点云没有有效目标对应点。"
            );
        }

        Mat distanceImage = GenerateDistanceHeatmap(
            alignedCloud,
            forwardDistance.Distances,
            _imageResolution,
            Math.Max(forwardDistance.MaxDistance, _distanceThreshold)
        );

        double rotationAngle = ComputeRotationAngle(accumR);
        double translationMagnitude = Math.Sqrt(
            accumT[0] * accumT[0] + accumT[1] * accumT[1] + accumT[2] * accumT[2]
        );

        int defectCount = forwardDistance.OverThresholdCount + forwardDistance.UnmatchedCount;
        double defectRatio = sourceCount > 0 ? (double)defectCount / sourceCount : 1;
        int missingCount = reverseDistance.OverThresholdCount + reverseDistance.UnmatchedCount;
        double missingRatio = targetCount > 0
            ? (double)missingCount / targetCount
            : 1;
        bool isOk =
            defectRatio <= _maxDefectRatio
            && missingRatio <= _maxMissingRatio
            && forwardDistance.MeanDistance <= _maxMeanDistance;

        List<string> reasons = [];
        if (defectRatio > _maxDefectRatio)
            reasons.Add($"超差点比例 {defectRatio:P2} > {_maxDefectRatio:P2}");
        if (missingRatio > _maxMissingRatio)
            reasons.Add($"缺失点比例 {missingRatio:P2} > {_maxMissingRatio:P2}");
        if (forwardDistance.MeanDistance > _maxMeanDistance)
            reasons.Add(
                $"平均偏差 {forwardDistance.MeanDistance:F4} mm > {_maxMeanDistance:F4} mm"
            );

        var result = new CloudCompareResult
        {
            SourcePointCount = sourceCount,
            TargetPointCount = targetCount,
            HeightDifference = new HeightDifferenceStats
            {
                MaxDistance = forwardDistance.MaxDistance,
                MeanDistance = forwardDistance.MeanDistance,
                StdDev = forwardDistance.StdDev,
                MinDistance = forwardDistance.MinDistance,
                P95Distance = forwardDistance.P95Distance,
                DefectCount = defectCount,
                DefectRatio = defectRatio,
                Threshold = _distanceThreshold,
                MatchedPointCount = forwardDistance.MatchedCount,
                UnmatchedPointCount = forwardDistance.UnmatchedCount,
            },
            PositionDifference = new PositionDifferenceStats
            {
                TranslationX = accumT[0],
                TranslationY = accumT[1],
                TranslationZ = accumT[2],
                TranslationMagnitude = translationMagnitude,
                RotationAngleDeg = rotationAngle * 180.0 / Math.PI,
                RotationMatrix = accumR,
            },
            RegistrationMethod = _registrationMethod,
            UsedCoarseRegistration = _useCoarseRegistration,
            IsOk = isOk,
            MaxDefectRatio = _maxDefectRatio,
            MaxMeanDistance = _maxMeanDistance,
            MissingRatio = missingRatio,
            MaxMissingRatio = _maxMissingRatio,
            EffectiveVoxelSizeMm = effectiveVoxelSize,
            EffectiveMaxCorrespondenceDistanceMm = effectiveMaxCorrespondenceDistance,
            SourceRegistrationPointCount = workingSource.Rows,
            TargetRegistrationPointCount = workingTarget.Rows,
            CoarseCorrespondenceCount = coarseOutcome.CorrespondenceCount,
            CoarseInlierCount = coarseOutcome.InlierCount,
            CoarseInlierRatio = coarseOutcome.InlierRatio,
            FineIterations = fineOutcome.Iterations,
            FineConverged = fineOutcome.Converged,
            FineCorrespondenceCount = fineOutcome.CorrespondenceCount,
            FineCorrespondenceRatio = fineCorrespondenceRatio,
            RegistrationRmseMm = fineOutcome.Rmse,
            MatchedSourcePointCount = forwardDistance.MatchedCount,
            UnmatchedSourcePointCount = forwardDistance.UnmatchedCount,
            MatchedTargetPointCount = reverseDistance.MatchedCount,
            UnmatchedTargetPointCount = reverseDistance.UnmatchedCount,
            MissingTargetPointCount = missingCount,
        };

        var outputCloud = new PointCloudData();
        outputCloud.Value = alignedCloud;
        if (sourceData.HasColors && sourceData.Colors != null)
            outputCloud.SetColors(sourceData.Colors.Clone());

        context.Set("aligned_cloud", outputCloud);
        context.Set("transform_matrix", transformMatrix);
        context.Set("distance_mat", forwardDistance.Distances);
        context.Set("distance_image", distanceImage);
        context.Set(
            "result",
            new InspectionResult<CloudCompareResult>
            {
                isValid = true,
                isOk = isOk,
                resultCode = isOk ? InspectionResultCode.OK : InspectionResultCode.NG,
                message = isOk ? "OK" : "3D比较超出质量阈值。",
                reasons = reasons,
                details = result,
            }
        );
    }

    private static InvalidOperationException Failure(string code, string message) =>
        new($"[{code}] {message}");

    private static Mat ValidateCloud(Mat? cloud, string displayName, string missingCode)
    {
        if (cloud is null || cloud.Empty())
            throw Failure(missingCode, $"{displayName}点云为空，无法执行 3D 比较。");
        if (cloud.Type() != MatType.CV_32FC1 || cloud.Cols < 3)
            throw Failure(
                "POINT_CLOUD_INVALID",
                $"{displayName}点云必须是至少三列的 CV_32FC1 矩阵。"
            );
        if (cloud.Rows < 3)
            throw Failure(
                "POINT_CLOUD_INVALID",
                $"{displayName}点云点数不足（{cloud.Rows} < 3），无法执行比较。"
            );

        for (int row = 0; row < cloud.Rows; row++)
        for (int col = 0; col < 3; col++)
            if (!float.IsFinite(cloud.Get<float>(row, col)))
                throw Failure(
                    "POINT_CLOUD_INVALID",
                    $"{displayName}点云包含 NaN 或无穷坐标（第 {row + 1} 点）。"
                );
        return cloud;
    }

    private static double BoundingBoxDiagonal(Mat cloud)
    {
        double minX = double.PositiveInfinity,
            minY = double.PositiveInfinity,
            minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity,
            maxY = double.NegativeInfinity,
            maxZ = double.NegativeInfinity;
        for (int row = 0; row < cloud.Rows; row++)
        {
            double x = cloud.Get<float>(row, 0);
            double y = cloud.Get<float>(row, 1);
            double z = cloud.Get<float>(row, 2);
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            minZ = Math.Min(minZ, z);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
            maxZ = Math.Max(maxZ, z);
        }
        double dx = maxX - minX,
            dy = maxY - minY,
            dz = maxZ - minZ;
        double diagonal = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (!double.IsFinite(diagonal) || diagonal <= 1e-9)
            throw Failure("POINT_CLOUD_INVALID", "点云包围盒退化，无法估计配准尺度。");
        return diagonal;
    }

    private static double EstimatePointSpacing(Mat cloud, double diagonal)
    {
        int comparisonCount = Math.Min(cloud.Rows, 2_000);
        int queryCount = Math.Min(comparisonCount, 256);
        int[] comparisonIndices = UniformSample(cloud.Rows, comparisonCount);
        int[] queryIndices = UniformSample(cloud.Rows, queryCount);
        List<double> nearestDistances = new(queryCount);

        foreach (int queryIndex in queryIndices)
        {
            double qx = cloud.Get<float>(queryIndex, 0);
            double qy = cloud.Get<float>(queryIndex, 1);
            double qz = cloud.Get<float>(queryIndex, 2);
            double bestSq = double.PositiveInfinity;
            foreach (int candidateIndex in comparisonIndices)
            {
                double dx = cloud.Get<float>(candidateIndex, 0) - qx;
                double dy = cloud.Get<float>(candidateIndex, 1) - qy;
                double dz = cloud.Get<float>(candidateIndex, 2) - qz;
                double distanceSq = dx * dx + dy * dy + dz * dz;
                if (distanceSq > 1e-18 && distanceSq < bestSq)
                    bestSq = distanceSq;
            }
            if (double.IsFinite(bestSq))
                nearestDistances.Add(Math.Sqrt(bestSq));
        }

        if (nearestDistances.Count == 0)
            return diagonal / 500.0;
        nearestDistances.Sort();
        double median = nearestDistances[nearestDistances.Count / 2];
        return double.IsFinite(median) && median > 0 ? median : diagonal / 500.0;
    }

    private static Mat CreateRegistrationCloud(Mat cloud, double voxelSize, int pointLimit)
    {
        Mat voxelized = VoxelDownsample(cloud, voxelSize);
        if (voxelized.Rows <= pointLimit)
            return voxelized;

        Mat limited = UniformDownsampleCloud(voxelized, pointLimit);
        voxelized.Dispose();
        return limited;
    }

    private static Mat UniformDownsampleCloud(Mat cloud, int maxPoints)
    {
        int count = Math.Min(cloud.Rows, maxPoints);
        int[] indices = UniformSample(cloud.Rows, count);
        Mat result = new(count, 3, MatType.CV_32FC1);
        for (int row = 0; row < indices.Length; row++)
        for (int col = 0; col < 3; col++)
            result.Set(row, col, cloud.Get<float>(indices[row], col));
        return result;
    }

    private static (float[] X, float[] Y, float[] Z) ExtractCoordinates(Mat cloud)
    {
        float[] x = new float[cloud.Rows];
        float[] y = new float[cloud.Rows];
        float[] z = new float[cloud.Rows];
        for (int row = 0; row < cloud.Rows; row++)
        {
            x[row] = cloud.Get<float>(row, 0);
            y[row] = cloud.Get<float>(row, 1);
            z[row] = cloud.Get<float>(row, 2);
        }
        return (x, y, z);
    }

    private static double[] IdentityRotation() =>
        [1, 0, 0, 0, 1, 0, 0, 0, 1];

    private static void ApplyTransform(
        float[] x,
        float[] y,
        float[] z,
        double[] rotation,
        double[] translation
    )
    {
        for (int i = 0; i < x.Length; i++)
        {
            double px = x[i],
                py = y[i],
                pz = z[i];
            x[i] = (float)(
                rotation[0] * px + rotation[1] * py + rotation[2] * pz + translation[0]
            );
            y[i] = (float)(
                rotation[3] * px + rotation[4] * py + rotation[5] * pz + translation[1]
            );
            z[i] = (float)(
                rotation[6] * px + rotation[7] * py + rotation[8] * pz + translation[2]
            );
        }
    }

    private static Mat TransformPointCloud(Mat source, double[] rotation, double[] translation)
    {
        Mat result = new(source.Rows, source.Cols, MatType.CV_32FC1);
        for (int row = 0; row < source.Rows; row++)
        {
            double x = source.Get<float>(row, 0);
            double y = source.Get<float>(row, 1);
            double z = source.Get<float>(row, 2);
            result.Set(
                row,
                0,
                (float)(rotation[0] * x + rotation[1] * y + rotation[2] * z + translation[0])
            );
            result.Set(
                row,
                1,
                (float)(rotation[3] * x + rotation[4] * y + rotation[5] * z + translation[1])
            );
            result.Set(
                row,
                2,
                (float)(rotation[6] * x + rotation[7] * y + rotation[8] * z + translation[2])
            );

            int copyStart = 3;
            if (source.Cols >= 6)
            {
                double nx = source.Get<float>(row, 3);
                double ny = source.Get<float>(row, 4);
                double nz = source.Get<float>(row, 5);
                double rnx = rotation[0] * nx + rotation[1] * ny + rotation[2] * nz;
                double rny = rotation[3] * nx + rotation[4] * ny + rotation[5] * nz;
                double rnz = rotation[6] * nx + rotation[7] * ny + rotation[8] * nz;
                double length = Math.Sqrt(rnx * rnx + rny * rny + rnz * rnz);
                if (length > 1e-12)
                {
                    rnx /= length;
                    rny /= length;
                    rnz /= length;
                }
                result.Set(row, 3, (float)rnx);
                result.Set(row, 4, (float)rny);
                result.Set(row, 5, (float)rnz);
                copyStart = 6;
            }

            for (int col = copyStart; col < source.Cols; col++)
                result.Set(row, col, source.Get<float>(row, col));
        }
        return result;
    }

    private static CorrespondenceQuality EvaluateCorrespondences(
        float[] srcX,
        float[] srcY,
        float[] srcZ,
        float[] tgtX,
        float[] tgtY,
        float[] tgtZ,
        double maxCorrespondenceDistance
    )
    {
        var grid = new SpatialHashGrid(
            tgtX,
            tgtY,
            tgtZ,
            maxCorrespondenceDistance,
            tgtX.Length
        );
        int count = 0;
        double sumSq = 0;
        for (int i = 0; i < srcX.Length; i++)
        {
            int nearest = grid.FindNearestNeighborWithin(
                srcX[i],
                srcY[i],
                srcZ[i],
                maxCorrespondenceDistance,
                out double distanceSq
            );
            if (nearest < 0)
                continue;
            count++;
            sumSq += distanceSq;
        }
        return new CorrespondenceQuality(
            count,
            count > 0 ? Math.Sqrt(sumSq / count) : double.PositiveInfinity
        );
    }

    private sealed record CoarseRegistrationOutcome(
        double[] Rotation,
        double[] Translation,
        int CorrespondenceCount,
        int InlierCount
    )
    {
        public static CoarseRegistrationOutcome NotUsed { get; } =
            new(IdentityRotation(), new double[3], 0, 0);
        public double InlierRatio =>
            CorrespondenceCount > 0 ? (double)InlierCount / CorrespondenceCount : 0;
    }

    private sealed record RegistrationOutcome(
        double[] Rotation,
        double[] Translation,
        int Iterations,
        int CorrespondenceCount,
        double Rmse,
        bool Converged
    );

    private readonly record struct CorrespondenceQuality(int Count, double Rmse);

    private sealed record DistanceOutcome(
        Mat Distances,
        int MatchedCount,
        int UnmatchedCount,
        int OverThresholdCount,
        double MinDistance,
        double MaxDistance,
        double MeanDistance,
        double StdDev,
        double P95Distance
    );

    private static (double[] R, double[] T) ComposeTransform(
        double[] fineR,
        double[] fineT,
        double[] coarseR,
        double[] coarseT
    )
    {
        double[] resultR = new double[9];
        for (int r = 0; r < 3; r++)
        for (int c = 0; c < 3; c++)
            resultR[r * 3 + c] =
                fineR[r * 3] * coarseR[c]
                + fineR[r * 3 + 1] * coarseR[3 + c]
                + fineR[r * 3 + 2] * coarseR[6 + c];
        double[] resultT =
        [
            fineR[0] * coarseT[0] + fineR[1] * coarseT[1] + fineR[2] * coarseT[2] + fineT[0],
            fineR[3] * coarseT[0] + fineR[4] * coarseT[1] + fineR[5] * coarseT[2] + fineT[1],
            fineR[6] * coarseT[0] + fineR[7] * coarseT[1] + fineR[8] * coarseT[2] + fineT[2],
        ];
        return (resultR, resultT);
    }

    private static Mat VoxelDownsample(Mat pointCloud, double voxelSize)
    {
        int pointCount = pointCloud.Rows;
        var grid = new Dictionary<(int, int, int), List<int>>();

        for (int i = 0; i < pointCount; i++)
        {
            int cx = (int)Math.Floor(pointCloud.Get<float>(i, 0) / voxelSize);
            int cy = (int)Math.Floor(pointCloud.Get<float>(i, 1) / voxelSize);
            int cz = (int)Math.Floor(pointCloud.Get<float>(i, 2) / voxelSize);
            var key = (cx, cy, cz);
            if (!grid.ContainsKey(key))
                grid[key] = new List<int>();
            grid[key].Add(i);
        }

        List<float[]> resultPoints = new();
        foreach (var cell in grid.Values)
        {
            float x = 0, y = 0, z = 0;
            foreach (int idx in cell)
            {
                x += pointCloud.Get<float>(idx, 0);
                y += pointCloud.Get<float>(idx, 1);
                z += pointCloud.Get<float>(idx, 2);
            }
            int count = cell.Count;
            resultPoints.Add(new[] { x / count, y / count, z / count });
        }

        Mat result = new Mat(resultPoints.Count, 3, MatType.CV_32FC1);
        for (int i = 0; i < resultPoints.Count; i++)
        {
            result.Set(i, 0, resultPoints[i][0]);
            result.Set(i, 1, resultPoints[i][1]);
            result.Set(i, 2, resultPoints[i][2]);
        }
        return result;
    }

    private static CoarseRegistrationOutcome CoarseRegistration(
        float[] srcX, float[] srcY, float[] srcZ,
        float[] tgtX, float[] tgtY, float[] tgtZ,
        double featureRadius,
        double distanceThreshold
    )
    {
        int srcCount = srcX.Length;
        int tgtCount = tgtX.Length;
        int sampleCount = Math.Min(300, Math.Min(srcCount, tgtCount));

        int[] srcSamples = UniformSample(srcCount, sampleCount);
        int[] tgtSamples = UniformSample(tgtCount, sampleCount);

        var srcGrid = new SpatialHashGrid(srcX, srcY, srcZ, featureRadius, srcCount);
        var tgtGrid = new SpatialHashGrid(tgtX, tgtY, tgtZ, featureRadius, tgtCount);

        var srcFeat = ComputeFpfhFeatures(
            srcX,
            srcY,
            srcZ,
            srcGrid,
            srcSamples,
            featureRadius
        );
        var tgtFeat = ComputeFpfhFeatures(
            tgtX,
            tgtY,
            tgtZ,
            tgtGrid,
            tgtSamples,
            featureRadius
        );

        var correspondences = new List<(int, int)>();
        for (int i = 0; i < srcSamples.Length; i++)
        {
            int bestJ = -1;
            double bestDist = double.MaxValue;
            for (int j = 0; j < tgtSamples.Length; j++)
            {
                double d = L2Sq(srcFeat[i], tgtFeat[j]);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestJ = j;
                }
            }
            if (bestJ >= 0)
                correspondences.Add((srcSamples[i], tgtSamples[bestJ]));
        }

        if (correspondences.Count < 3)
            return new CoarseRegistrationOutcome(
                IdentityRotation(),
                new double[3],
                correspondences.Count,
                0
            );

        Random rng = new(0);
        double[] bestR = IdentityRotation();
        double[] bestT = new double[3];
        int bestInliers = -1;
        double threshSq = distanceThreshold * distanceThreshold;

        for (int iter = 0; iter < 1000; iter++)
        {
            int[] pick = SampleDistinct(correspondences.Count, 3, rng);
            var sPts = new List<(double, double, double)>(3);
            var tPts = new List<(double, double, double)>(3);
            foreach (int p in pick)
            {
                var (si, ti) = correspondences[p];
                sPts.Add((srcX[si], srcY[si], srcZ[si]));
                tPts.Add((tgtX[ti], tgtY[ti], tgtZ[ti]));
            }

            try
            {
                var (R, t) = Math3D.ComputeRigidTransform(sPts, tPts);
                int inliers = 0;
                foreach (var (si, ti) in correspondences)
                {
                    double tx = R[0] * srcX[si] + R[1] * srcY[si] + R[2] * srcZ[si] + t[0];
                    double ty = R[3] * srcX[si] + R[4] * srcY[si] + R[5] * srcZ[si] + t[1];
                    double tz = R[6] * srcX[si] + R[7] * srcY[si] + R[8] * srcZ[si] + t[2];
                    double dx = tx - tgtX[ti], dy = ty - tgtY[ti], dz = tz - tgtZ[ti];
                    if (dx * dx + dy * dy + dz * dz <= threshSq)
                        inliers++;
                }
                if (inliers > bestInliers)
                {
                    bestInliers = inliers;
                    bestR = R;
                    bestT = t;
                }
            }
            catch
            {
                continue;
            }
        }

        return new CoarseRegistrationOutcome(
            bestR,
            bestT,
            correspondences.Count,
            Math.Max(bestInliers, 0)
        );
    }

    private static RegistrationOutcome IcpRegistration(
        float[] srcX, float[] srcY, float[] srcZ,
        float[] tgtX, float[] tgtY, float[] tgtZ,
        double maxCorrespondenceDistance, int maxIterations
    )
    {
        int srcCount = srcX.Length;
        int tgtCount = tgtX.Length;

        var grid = new SpatialHashGrid(tgtX, tgtY, tgtZ, (float)maxCorrespondenceDistance, tgtCount);
        double[] R = { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        double[] t = { 0, 0, 0 };

        double maxCorrDistSq = maxCorrespondenceDistance * maxCorrespondenceDistance;
        double prevRmse = double.MaxValue;
        double finalRmse = double.PositiveInfinity;
        int finalCorrespondenceCount = 0;
        int actualIterations = 0;
        bool converged = false;

        for (int iter = 0; iter < maxIterations; iter++)
        {
            actualIterations = iter + 1;
            var correspondences = new List<(int, int)>();
            for (int i = 0; i < srcCount; i++)
            {
                int nearest = grid.FindNearestNeighborWithin(
                    srcX[i],
                    srcY[i],
                    srcZ[i],
                    maxCorrespondenceDistance,
                    out _
                );
                if (nearest < 0) continue;
                float dx = srcX[i] - tgtX[nearest];
                float dy = srcY[i] - tgtY[nearest];
                float dz = srcZ[i] - tgtZ[nearest];
                double distSq = dx * dx + dy * dy + dz * dz;
                if (distSq <= maxCorrDistSq)
                    correspondences.Add((i, nearest));
            }

            finalCorrespondenceCount = correspondences.Count;
            if (correspondences.Count < 3)
                break;

            double srcCx = 0, srcCy = 0, srcCz = 0;
            double tgtCx = 0, tgtCy = 0, tgtCz = 0;
            foreach (var (si, ti) in correspondences)
            {
                srcCx += srcX[si]; srcCy += srcY[si]; srcCz += srcZ[si];
                tgtCx += tgtX[ti]; tgtCy += tgtY[ti]; tgtCz += tgtZ[ti];
            }
            int corrCount = correspondences.Count;
            srcCx /= corrCount; srcCy /= corrCount; srcCz /= corrCount;
            tgtCx /= corrCount; tgtCy /= corrCount; tgtCz /= corrCount;

            double h00 = 0, h01 = 0, h02 = 0, h10 = 0, h11 = 0, h12 = 0, h20 = 0, h21 = 0, h22 = 0;
            foreach (var (si, ti) in correspondences)
            {
                double sdx = srcX[si] - srcCx, sdy = srcY[si] - srcCy, sdz = srcZ[si] - srcCz;
                double tdx = tgtX[ti] - tgtCx, tdy = tgtY[ti] - tgtCy, tdz = tgtZ[ti] - tgtCz;
                h00 += sdx * tdx; h01 += sdx * tdy; h02 += sdx * tdz;
                h10 += sdy * tdx; h11 += sdy * tdy; h12 += sdy * tdz;
                h20 += sdz * tdx; h21 += sdz * tdy; h22 += sdz * tdz;
            }

            double[] dR = Math3D.RotationFromCrossCovariance(h00, h01, h02, h10, h11, h12, h20, h21, h22);
            double[] dt = {
                tgtCx - (dR[0] * srcCx + dR[1] * srcCy + dR[2] * srcCz),
                tgtCy - (dR[3] * srcCx + dR[4] * srcCy + dR[5] * srcCz),
                tgtCz - (dR[6] * srcCx + dR[7] * srcCy + dR[8] * srcCz),
            };

            for (int i = 0; i < srcCount; i++)
            {
                double x = srcX[i], y = srcY[i], z = srcZ[i];
                srcX[i] = (float)(dR[0] * x + dR[1] * y + dR[2] * z + dt[0]);
                srcY[i] = (float)(dR[3] * x + dR[4] * y + dR[5] * z + dt[1]);
                srcZ[i] = (float)(dR[6] * x + dR[7] * y + dR[8] * z + dt[2]);
            }

            double[] newR = new double[9];
            for (int rr = 0; rr < 3; rr++)
                for (int cc = 0; cc < 3; cc++)
                    newR[rr * 3 + cc] = dR[rr * 3 + 0] * R[0 * 3 + cc] + dR[rr * 3 + 1] * R[1 * 3 + cc] + dR[rr * 3 + 2] * R[2 * 3 + cc];
            double[] newT = {
                dR[0] * t[0] + dR[1] * t[1] + dR[2] * t[2] + dt[0],
                dR[3] * t[0] + dR[4] * t[1] + dR[5] * t[2] + dt[1],
                dR[6] * t[0] + dR[7] * t[1] + dR[8] * t[2] + dt[2],
            };
            R = newR;
            t = newT;

            double rmse = 0;
            foreach (var (si, ti) in correspondences)
            {
                double dx = srcX[si] - tgtX[ti], dy = srcY[si] - tgtY[ti], dz = srcZ[si] - tgtZ[ti];
                rmse += dx * dx + dy * dy + dz * dz;
            }
            rmse = Math.Sqrt(rmse / corrCount);
            finalRmse = rmse;

            if (Math.Abs(prevRmse - rmse) < 1e-6)
            {
                converged = true;
                break;
            }
            prevRmse = rmse;
        }

        CorrespondenceQuality quality = EvaluateCorrespondences(
            srcX,
            srcY,
            srcZ,
            tgtX,
            tgtY,
            tgtZ,
            maxCorrespondenceDistance
        );
        if (quality.Count > 0)
        {
            finalCorrespondenceCount = quality.Count;
            finalRmse = quality.Rmse;
        }
        return new RegistrationOutcome(
            R,
            t,
            actualIterations,
            finalCorrespondenceCount,
            finalRmse,
            converged
        );
    }

    private static RegistrationOutcome PointToPlaneIcp(
        float[] srcX, float[] srcY, float[] srcZ,
        float[] tgtX, float[] tgtY, float[] tgtZ,
        double maxCorrespondenceDistance, int maxIterations
    )
    {
        int srcCount = srcX.Length;
        int tgtCount = tgtX.Length;

        double[] tgtNx = new double[tgtCount], tgtNy = new double[tgtCount], tgtNz = new double[tgtCount];
        using (Mat tgtMat = new Mat(tgtCount, 3, MatType.CV_32FC1))
        {
            for (int i = 0; i < tgtCount; i++)
            {
                tgtMat.Set(i, 0, tgtX[i]);
                tgtMat.Set(i, 1, tgtY[i]);
                tgtMat.Set(i, 2, tgtZ[i]);
            }
            using Mat normals = PointCloudUtils.EstimateNormals(tgtMat, 20);
            for (int i = 0; i < tgtCount; i++)
            {
                tgtNx[i] = normals.Get<float>(i, 0);
                tgtNy[i] = normals.Get<float>(i, 1);
                tgtNz[i] = normals.Get<float>(i, 2);
            }
        }

        var grid = new SpatialHashGrid(tgtX, tgtY, tgtZ, (float)maxCorrespondenceDistance, tgtCount);
        double[] R = { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        double[] t = { 0, 0, 0 };

        double maxCorrDistSq = maxCorrespondenceDistance * maxCorrespondenceDistance;
        int actualIterations = 0;
        int finalCorrespondenceCount = 0;
        bool converged = false;

        for (int iter = 0; iter < maxIterations; iter++)
        {
            actualIterations = iter + 1;
            double[,] A = new double[6, 6];
            double[] b = new double[6];
            int corrCount = 0;

            for (int i = 0; i < srcCount; i++)
            {
                int nn = grid.FindNearestNeighborWithin(
                    srcX[i],
                    srcY[i],
                    srcZ[i],
                    maxCorrespondenceDistance,
                    out double distSq
                );
                if (nn < 0 || distSq > maxCorrDistSq) continue;

                corrCount++;
                double sx = srcX[i], sy = srcY[i], sz = srcZ[i];
                double qx = tgtX[nn], qy = tgtY[nn], qz = tgtZ[nn];
                double nx = tgtNx[nn], ny = tgtNy[nn], nz = tgtNz[nn];

                double cx = sy * nz - sz * ny;
                double cy = sz * nx - sx * nz;
                double cz = sx * ny - sy * nx;
                double[] a = { cx, cy, cz, nx, ny, nz };
                double e = (sx - qx) * nx + (sy - qy) * ny + (sz - qz) * nz;

                for (int r = 0; r < 6; r++)
                {
                    for (int c = 0; c < 6; c++)
                        A[r, c] += a[r] * a[c];
                    b[r] += -a[r] * e;
                }
            }

            finalCorrespondenceCount = corrCount;
            if (corrCount < 6)
                break;

            double[] x = Solve6(A, b);
            double[] dR = RodriguesToMatrix(x[0], x[1], x[2]);
            double[] dt = { x[3], x[4], x[5] };

            for (int i = 0; i < srcCount; i++)
            {
                double px = srcX[i], py = srcY[i], pz = srcZ[i];
                srcX[i] = (float)(dR[0] * px + dR[1] * py + dR[2] * pz + dt[0]);
                srcY[i] = (float)(dR[3] * px + dR[4] * py + dR[5] * pz + dt[1]);
                srcZ[i] = (float)(dR[6] * px + dR[7] * py + dR[8] * pz + dt[2]);
            }

            double[] newR = new double[9];
            for (int rr = 0; rr < 3; rr++)
                for (int cc = 0; cc < 3; cc++)
                    newR[rr * 3 + cc] = dR[rr * 3 + 0] * R[0 * 3 + cc] + dR[rr * 3 + 1] * R[1 * 3 + cc] + dR[rr * 3 + 2] * R[2 * 3 + cc];
            double[] newT = {
                dR[0] * t[0] + dR[1] * t[1] + dR[2] * t[2] + dt[0],
                dR[3] * t[0] + dR[4] * t[1] + dR[5] * t[2] + dt[1],
                dR[6] * t[0] + dR[7] * t[1] + dR[8] * t[2] + dt[2],
            };
            R = newR;
            t = newT;

            double transformChange = Math.Abs(dt[0]) + Math.Abs(dt[1]) + Math.Abs(dt[2]) + Math.Abs(x[0]) + Math.Abs(x[1]) + Math.Abs(x[2]);
            if (transformChange < 1e-8)
            {
                converged = true;
                break;
            }
        }

        CorrespondenceQuality quality = EvaluateCorrespondences(
            srcX,
            srcY,
            srcZ,
            tgtX,
            tgtY,
            tgtZ,
            maxCorrespondenceDistance
        );
        return new RegistrationOutcome(
            R,
            t,
            actualIterations,
            quality.Count > 0 ? quality.Count : finalCorrespondenceCount,
            quality.Rmse,
            converged
        );
    }

    private static RegistrationOutcome NdtRegistration(
        float[] srcX, float[] srcY, float[] srcZ,
        float[] tgtX, float[] tgtY, float[] tgtZ,
        double maxCorrespondenceDistance,
        int maxIterations
    )
    {
        int srcCount = srcX.Length;
        int tgtCount = tgtX.Length;

        double resolution = maxCorrespondenceDistance;
        var voxels = BuildNdtVoxels(tgtX, tgtY, tgtZ, resolution);

        double[] p = new double[6];
        double[] hStep = { 1e-3, 1e-3, 1e-3, 1e-4, 1e-4, 1e-4 };
        double prevScore = NdtScore(p, srcX, srcY, srcZ, voxels, resolution);

        int actualIterations = 0;
        bool converged = false;
        for (int iter = 0; iter < maxIterations; iter++)
        {
            actualIterations = iter + 1;
            double[] g = NumericalGradient(p, hStep, srcX, srcY, srcZ, voxels, resolution);
            double[,] H = NumericalHessian(p, hStep, srcX, srcY, srcZ, voxels, resolution);

            double[] delta = SolveNewtonStep(H, g);
            if (delta is null) delta = g;

            double dnorm = Math.Sqrt(delta.Sum(v => v * v));
            if (dnorm > 0.1)
                for (int k = 0; k < 6; k++)
                    delta[k] *= 0.1 / dnorm;

            double bestScore = prevScore;
            double[] bestP = (double[])p.Clone();
            double scale = 1.0;

            for (int ls = 0; ls < 10; ls++)
            {
                double[] trial = new double[6];
                for (int k = 0; k < 6; k++)
                    trial[k] = p[k] + scale * delta[k];
                double sc = NdtScore(trial, srcX, srcY, srcZ, voxels, resolution);
                if (sc > bestScore)
                {
                    bestScore = sc;
                    bestP = trial;
                    break;
                }
                scale *= 0.5;
            }

            double change = 0;
            for (int k = 0; k < 6; k++)
                change += Math.Abs(bestP[k] - p[k]);

            p = bestP;
            prevScore = bestScore;

            if (change < 1e-8)
            {
                converged = true;
                break;
            }
        }

        double[] R = RodriguesToMatrix(p[3], p[4], p[5]);
        double[] t = { p[0], p[1], p[2] };
        ApplyTransform(srcX, srcY, srcZ, R, t);
        CorrespondenceQuality quality = EvaluateCorrespondences(
            srcX,
            srcY,
            srcZ,
            tgtX,
            tgtY,
            tgtZ,
            maxCorrespondenceDistance
        );
        return new RegistrationOutcome(
            R,
            t,
            actualIterations,
            quality.Count,
            quality.Rmse,
            converged
        );
    }

    private static DistanceOutcome ComputeCloudToCloudDistance(
        float[] srcX, float[] srcY, float[] srcZ,
        float[] tgtX, float[] tgtY, float[] tgtZ,
        double maxCorrespondenceDistance, double distanceThreshold
    )
    {
        int srcCount = srcX.Length;
        int tgtCount = tgtX.Length;

        var grid = new SpatialHashGrid(tgtX, tgtY, tgtZ, (float)maxCorrespondenceDistance, tgtCount);

        Mat distances = new Mat(srcCount, 1, MatType.CV_64FC1);
        double maxDist = 0;
        double minDist = double.PositiveInfinity;
        double sumDist = 0;
        int overThresholdCount = 0;
        int matchedCount = 0;
        int unmatchedCount = 0;
        List<double> validDistances = new(srcCount);

        for (int i = 0; i < srcCount; i++)
        {
            int nearestIdx = grid.FindNearestNeighborWithin(
                srcX[i],
                srcY[i],
                srcZ[i],
                maxCorrespondenceDistance,
                out double distSq
            );
            if (nearestIdx >= 0)
            {
                double dist = Math.Sqrt(distSq);
                distances.Set(i, 0, dist);
                sumDist += dist;
                matchedCount++;
                validDistances.Add(dist);
                if (dist > maxDist)
                    maxDist = dist;
                if (dist < minDist)
                    minDist = dist;
                if (dist > distanceThreshold)
                    overThresholdCount++;
            }
            else
            {
                distances.Set(i, 0, double.MaxValue);
                unmatchedCount++;
            }
        }

        double meanDist = matchedCount > 0 ? sumDist / matchedCount : double.PositiveInfinity;

        double sumSq = 0;
        foreach (double distance in validDistances)
        {
            double delta = distance - meanDist;
            sumSq += delta * delta;
        }
        double stdDev = matchedCount > 1 ? Math.Sqrt(sumSq / matchedCount) : 0;
        validDistances.Sort();
        double p95 = validDistances.Count > 0
            ? validDistances[Math.Clamp((int)Math.Ceiling(validDistances.Count * 0.95) - 1, 0, validDistances.Count - 1)]
            : double.PositiveInfinity;

        return new DistanceOutcome(
            distances,
            matchedCount,
            unmatchedCount,
            overThresholdCount,
            matchedCount > 0 ? minDist : double.PositiveInfinity,
            maxDist,
            meanDist,
            stdDev,
            p95
        );
    }

    private static Mat GenerateDistanceHeatmap(Mat pointCloud, Mat distances, int resolution, double maxDist)
    {
        int pointCount = pointCloud.Rows;

        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;
        for (int i = 0; i < pointCount; i++)
        {
            float x = pointCloud.Get<float>(i, 0);
            float y = pointCloud.Get<float>(i, 1);
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }

        float rangeX = maxX - minX;
        float rangeY = maxY - minY;
        if (rangeX < 1e-6f) rangeX = 1;
        if (rangeY < 1e-6f) rangeY = 1;

        using Mat accumImage = Mat.Zeros(resolution, resolution, MatType.CV_64FC1);
        using Mat countImage = Mat.Zeros(resolution, resolution, MatType.CV_32SC1);

        for (int i = 0; i < pointCount; i++)
        {
            double d = distances.Get<double>(i, 0);
            if (d < 0 || d >= double.MaxValue) continue;

            int px = (int)((pointCloud.Get<float>(i, 0) - minX) / rangeX * (resolution - 1));
            int py = (int)((pointCloud.Get<float>(i, 1) - minY) / rangeY * (resolution - 1));
            px = Math.Clamp(px, 0, resolution - 1);
            py = Math.Clamp(py, 0, resolution - 1);

            accumImage.Set(py, px, accumImage.Get<double>(py, px) + d);
            countImage.Set(py, px, countImage.Get<int>(py, px) + 1);
        }

        using Mat normalizedMat = new Mat(resolution, resolution, MatType.CV_8UC1);
        double effectiveMax = maxDist > 0 ? maxDist : 1.0;
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int count = countImage.Get<int>(y, x);
                if (count > 0)
                {
                    double avg = accumImage.Get<double>(y, x) / count;
                    byte val = (byte)Math.Clamp(avg / effectiveMax * 255, 0, 255);
                    normalizedMat.Set(y, x, val);
                }
            }
        }

        Mat colorMap = new Mat();
        Cv2.ApplyColorMap(normalizedMat, colorMap, ColormapTypes.Jet);
        return colorMap;
    }

    private static double ComputeRotationAngle(double[] R)
    {
        double trace = R[0] + R[4] + R[8];
        double cosTheta = (trace - 1) / 2.0;
        cosTheta = Math.Clamp(cosTheta, -1.0, 1.0);
        return Math.Acos(cosTheta);
    }

    private static double[][] ComputeFpfhFeatures(
        float[] x, float[] y, float[] z,
        SpatialHashGrid grid, int[] samples, double radius
    )
    {
        int binCount = 11;
        int descLen = binCount * 3;
        double radiusSq = radius * radius;

        double[][] features = new double[samples.Length][];
        for (int s = 0; s < samples.Length; s++)
        {
            int i = samples[s];
            double[] hist = new double[descLen];

            var candidates = grid.CollectCandidates(x[i], y[i], z[i], 1);
            foreach (int j in candidates)
            {
                if (j == i) continue;
                double dx = x[j] - x[i], dy = y[j] - y[i], dz = z[j] - z[i];
                double dSq = dx * dx + dy * dy + dz * dz;
                if (dSq > radiusSq || dSq < 1e-18) continue;

                double dist = Math.Sqrt(dSq);
                double nx = dx / dist, ny = dy / dist, nz = dz / dist;

                double f1 = nx, f2 = ny, f3 = nz;

                hist[Bin(f1, -1, 1, binCount)]++;
                hist[binCount + Bin(f2, -1, 1, binCount)]++;
                hist[2 * binCount + Bin(f3, -1, 1, binCount)]++;
            }

            double sum = hist.Sum();
            if (sum > 0)
                for (int b = 0; b < descLen; b++)
                    hist[b] /= sum;

            features[s] = hist;
        }
        return features;
    }

    private static int Bin(double value, double min, double max, int bins)
    {
        double t = (value - min) / (max - min);
        int b = (int)(t * bins);
        return Math.Clamp(b, 0, bins - 1);
    }

    private static double L2Sq(double[] a, double[] b)
    {
        double sum = 0;
        for (int i = 0; i < a.Length; i++)
        {
            double d = a[i] - b[i];
            sum += d * d;
        }
        return sum;
    }

    private static int[] UniformSample(int n, int count)
    {
        if (count >= n) return Enumerable.Range(0, n).ToArray();
        int[] result = new int[count];
        double step = (double)n / count;
        for (int i = 0; i < count; i++)
            result[i] = Math.Min(n - 1, (int)(i * step));
        return result;
    }

    private static int[] SampleDistinct(int n, int k, Random rng)
    {
        var set = new HashSet<int>();
        while (set.Count < k)
            set.Add(rng.Next(n));
        return set.ToArray();
    }

    private static double[] Solve6(double[,] A, double[] b)
    {
        using Mat matA = new Mat(6, 6, MatType.CV_64FC1);
        using Mat matB = new Mat(6, 1, MatType.CV_64FC1);
        for (int r = 0; r < 6; r++)
        {
            for (int c = 0; c < 6; c++)
                matA.Set(r, c, A[r, c]);
            matB.Set(r, 0, b[r]);
        }

        using Mat sol = new Mat();
        if (!Cv2.Solve(matA, matB, sol, DecompTypes.Cholesky))
            Cv2.Solve(matA, matB, sol, DecompTypes.SVD);

        double[] x = new double[6];
        for (int i = 0; i < 6; i++)
            x[i] = sol.Get<double>(i, 0);
        return x;
    }

    private static double[] RodriguesToMatrix(double rx, double ry, double rz)
    {
        using Mat rvec = new Mat(3, 1, MatType.CV_64FC1);
        rvec.Set(0, 0, rx);
        rvec.Set(1, 0, ry);
        rvec.Set(2, 0, rz);
        using Mat rmat = new Mat();
        Cv2.Rodrigues(rvec, rmat);

        double[] R = new double[9];
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                R[r * 3 + c] = rmat.Get<double>(r, c);
        return R;
    }

    private readonly struct NdtVoxel
    {
        public readonly double Mx, My, Mz;
        public readonly double[] InvCov;

        public NdtVoxel(double mx, double my, double mz, double[] invCov)
        {
            Mx = mx; My = my; Mz = mz; InvCov = invCov;
        }
    }

    private static Dictionary<(int, int, int), NdtVoxel> BuildNdtVoxels(
        float[] x, float[] y, float[] z, double resolution
    )
    {
        int n = x.Length;
        double inv = 1.0 / resolution;

        var acc = new Dictionary<(int, int, int), NdtAccum>();
        for (int i = 0; i < n; i++)
        {
            var key = ((int)Math.Floor(x[i] * inv), (int)Math.Floor(y[i] * inv), (int)Math.Floor(z[i] * inv));
            if (!acc.TryGetValue(key, out var a))
            {
                a = new NdtAccum();
                acc[key] = a;
            }
            a.Add(x[i], y[i], z[i]);
        }

        var result = new Dictionary<(int, int, int), NdtVoxel>();
        foreach (var (key, a) in acc)
        {
            if (a.Count < 5) continue;
            if (a.TryFinalize(out double mx, out double my, out double mz, out double[] invCov))
                result[key] = new NdtVoxel(mx, my, mz, invCov);
        }
        return result;
    }

    private sealed class NdtAccum
    {
        public int Count;
        private double _sx, _sy, _sz;
        private double _sxx, _sxy, _sxz, _syy, _syz, _szz;

        public void Add(double x, double y, double z)
        {
            Count++;
            _sx += x; _sy += y; _sz += z;
            _sxx += x * x; _sxy += x * y; _sxz += x * z;
            _syy += y * y; _syz += y * z; _szz += z * z;
        }

        public bool TryFinalize(out double mx, out double my, out double mz, out double[] invCov)
        {
            mx = _sx / Count; my = _sy / Count; mz = _sz / Count;

            double c00 = _sxx / Count - mx * mx;
            double c01 = _sxy / Count - mx * my;
            double c02 = _sxz / Count - mx * mz;
            double c11 = _syy / Count - my * my;
            double c12 = _syz / Count - my * mz;
            double c22 = _szz / Count - mz * mz;

            double scale = (c00 + c11 + c22) / 3.0;
            double reg = Math.Max(scale * 1e-3, 1e-9);
            c00 += reg; c11 += reg; c22 += reg;

            invCov = Inverse3x3(c00, c01, c02, c11, c12, c22);
            return invCov is not null;
        }
    }

    private static double[] Inverse3x3(double a, double b, double c, double d, double e, double f)
    {
        double det = a * (d * f - e * e) - b * (b * f - e * c) + c * (b * e - d * c);
        if (Math.Abs(det) < 1e-18) return null;
        double invDet = 1.0 / det;

        return new[] {
            (d * f - e * e) * invDet, (c * e - b * f) * invDet, (b * e - c * d) * invDet,
            (c * e - b * f) * invDet, (a * f - c * c) * invDet, (b * c - a * e) * invDet,
            (b * e - c * d) * invDet, (b * c - a * e) * invDet, (a * d - b * b) * invDet,
        };
    }

    private static double NdtScore(
        double[] p, float[] srcX, float[] srcY, float[] srcZ,
        Dictionary<(int, int, int), NdtVoxel> voxels, double resolution
    )
    {
        double[] R = RodriguesToMatrix(p[3], p[4], p[5]);
        double tx = p[0], ty = p[1], tz = p[2];
        double inv = 1.0 / resolution;

        double score = 0;
        int maxScorePoints = Math.Min(2000, srcX.Length);
        int[] idx = UniformSample(srcX.Length, maxScorePoints);

        foreach (int i in idx)
        {
            double x = srcX[i], y = srcY[i], z = srcZ[i];
            double px = R[0] * x + R[1] * y + R[2] * z + tx;
            double py = R[3] * x + R[4] * y + R[5] * z + ty;
            double pz = R[6] * x + R[7] * y + R[8] * z + tz;

            var key = ((int)Math.Floor(px * inv), (int)Math.Floor(py * inv), (int)Math.Floor(pz * inv));
            if (!voxels.TryGetValue(key, out var v)) continue;

            double dx = px - v.Mx, dy = py - v.My, dz = pz - v.Mz;
            double[] m = v.InvCov;
            double q = dx * (m[0] * dx + m[1] * dy + m[2] * dz)
                     + dy * (m[3] * dx + m[4] * dy + m[5] * dz)
                     + dz * (m[6] * dx + m[7] * dy + m[8] * dz);
            score += Math.Exp(-0.5 * q);
        }
        return score;
    }

    private static double[] NumericalGradient(
        double[] p, double[] h, float[] srcX, float[] srcY, float[] srcZ,
        Dictionary<(int, int, int), NdtVoxel> voxels, double resolution
    )
    {
        double[] g = new double[6];
        for (int k = 0; k < 6; k++)
        {
            double[] pp = (double[])p.Clone();
            double[] pm = (double[])p.Clone();
            pp[k] += h[k];
            pm[k] -= h[k];
            g[k] = (NdtScore(pp, srcX, srcY, srcZ, voxels, resolution)
                  - NdtScore(pm, srcX, srcY, srcZ, voxels, resolution)) / (2 * h[k]);
        }
        return g;
    }

    private static double[,] NumericalHessian(
        double[] p, double[] h, float[] srcX, float[] srcY, float[] srcZ,
        Dictionary<(int, int, int), NdtVoxel> voxels, double resolution
    )
    {
        double[,] H = new double[6, 6];
        double f0 = NdtScore(p, srcX, srcY, srcZ, voxels, resolution);

        for (int j = 0; j < 6; j++)
        {
            for (int k = j; k < 6; k++)
            {
                double val;
                if (j == k)
                {
                    double[] pp = (double[])p.Clone();
                    double[] pm = (double[])p.Clone();
                    pp[j] += h[j];
                    pm[j] -= h[j];
                    val = (NdtScore(pp, srcX, srcY, srcZ, voxels, resolution)
                         - 2 * f0 + NdtScore(pm, srcX, srcY, srcZ, voxels, resolution)) / (h[j] * h[j]);
                }
                else
                {
                    double[] ppp = (double[])p.Clone();
                    double[] ppm = (double[])p.Clone();
                    double[] pmp = (double[])p.Clone();
                    double[] pmm = (double[])p.Clone();
                    ppp[j] += h[j]; ppp[k] += h[k];
                    ppm[j] += h[j]; ppm[k] -= h[k];
                    pmp[j] -= h[j]; pmp[k] += h[k];
                    pmm[j] -= h[j]; pmm[k] -= h[k];
                    val = (NdtScore(ppp, srcX, srcY, srcZ, voxels, resolution)
                         - NdtScore(ppm, srcX, srcY, srcZ, voxels, resolution)
                         - NdtScore(pmp, srcX, srcY, srcZ, voxels, resolution)
                         + NdtScore(pmm, srcX, srcY, srcZ, voxels, resolution)) / (4 * h[j] * h[k]);
                }
                H[j, k] = val;
                H[k, j] = val;
            }
        }
        return H;
    }

    private static double[] SolveNewtonStep(double[,] H, double[] g)
    {
        double mu = 1e-3;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            using Mat A = new Mat(6, 6, MatType.CV_64FC1);
            using Mat b = new Mat(6, 1, MatType.CV_64FC1);
            for (int r = 0; r < 6; r++)
            {
                for (int c = 0; c < 6; c++)
                    A.Set(r, c, -H[r, c] + (r == c ? mu : 0));
                b.Set(r, 0, g[r]);
            }

            using Mat sol = new Mat();
            if (Cv2.Solve(A, b, sol, DecompTypes.Cholesky))
            {
                double[] delta = new double[6];
                for (int i = 0; i < 6; i++)
                    delta[i] = sol.Get<double>(i, 0);
                double dot = 0;
                for (int i = 0; i < 6; i++)
                    dot += g[i] * delta[i];
                if (dot > 0)
                    return delta;
            }
            mu *= 10;
        }
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public class CloudCompareResult
    {
        public int SourcePointCount { get; set; }
        public int TargetPointCount { get; set; }
        public HeightDifferenceStats HeightDifference { get; set; } = new();
        public PositionDifferenceStats PositionDifference { get; set; } = new();
        public string RegistrationMethod { get; set; } = string.Empty;
        public bool UsedCoarseRegistration { get; set; }
        public bool IsOk { get; set; }
        public double MaxDefectRatio { get; set; }
        public double MaxMeanDistance { get; set; }
        public double MissingRatio { get; set; }
        public double MaxMissingRatio { get; set; }
        public double EffectiveVoxelSizeMm { get; set; }
        public double EffectiveMaxCorrespondenceDistanceMm { get; set; }
        public int SourceRegistrationPointCount { get; set; }
        public int TargetRegistrationPointCount { get; set; }
        public int CoarseCorrespondenceCount { get; set; }
        public int CoarseInlierCount { get; set; }
        public double CoarseInlierRatio { get; set; }
        public int FineIterations { get; set; }
        public bool FineConverged { get; set; }
        public int FineCorrespondenceCount { get; set; }
        public double FineCorrespondenceRatio { get; set; }
        public double RegistrationRmseMm { get; set; }
        public int MatchedSourcePointCount { get; set; }
        public int UnmatchedSourcePointCount { get; set; }
        public int MatchedTargetPointCount { get; set; }
        public int UnmatchedTargetPointCount { get; set; }
        public int MissingTargetPointCount { get; set; }
    }

    public class HeightDifferenceStats
    {
        public double MaxDistance { get; set; }
        public double MeanDistance { get; set; }
        public double StdDev { get; set; }
        public double MinDistance { get; set; }
        public double P95Distance { get; set; }
        public int DefectCount { get; set; }
        public double DefectRatio { get; set; }
        public double Threshold { get; set; }
        public int MatchedPointCount { get; set; }
        public int UnmatchedPointCount { get; set; }
    }

    public class PositionDifferenceStats
    {
        public double TranslationX { get; set; }
        public double TranslationY { get; set; }
        public double TranslationZ { get; set; }
        public double TranslationMagnitude { get; set; }
        public double RotationAngleDeg { get; set; }
        public double[] RotationMatrix { get; set; } = Array.Empty<double>();
    }
}
