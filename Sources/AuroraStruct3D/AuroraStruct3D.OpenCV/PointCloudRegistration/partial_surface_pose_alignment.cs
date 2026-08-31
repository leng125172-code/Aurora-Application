using System.Text.Json;
using AuroraStruct3D.OpenCV.Common;

namespace AuroraStruct3D.OpenCV.PointCloudRegistration;

/// <summary>
/// 面向“局部扫描点云到完整数模”的一次性姿态标定算子。
/// 先枚举 PCA 主轴的 24 个合法刚体方向，再以裁剪 ICP 精修并按局部表面重合度选择最优姿态。
/// </summary>
[Guid("b1a1000a-000a-4000-8000-00000000003a")]
[Category("3D配准")]
[DisplayName("局部表面姿态标定")]
[Description("自动识别局部扫描相对完整数模的姿态；标定成功后把输出角度和平移填入“固定姿态变换”。")]
public sealed class partial_surface_pose_alignment : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData { ParameterName = "source_cloud", DisplayName = "扫描点云" },
            new PointCloudData { ParameterName = "target_cloud", DisplayName = "数模点云" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new PointCloudData { ParameterName = "aligned_cloud", DisplayName = "姿态对齐点云" },
            new MatImg { ParameterName = "transform_matrix", DisplayName = "标定变换矩阵" },
            new VisionParameter<string>
            {
                ParameterName = "pose_json",
                DisplayName = "姿态标定结果",
                ParameterType = typeof(string),
                ControlType = PortControlType.Download,
            },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            Number("sourceSampleCount", "扫描采样点数", "2000"),
            Number("targetSampleCount", "数模采样点数", "5000"),
            Number("maxIterations", "每个姿态最大迭代", "30"),
            Number("trimFraction", "保留最佳对应比例", "0.7"),
            Number("inlierDistance", "内点距离", "1.0"),
            Number("minimumInlierRatio", "最小内点比例", "0.15"),
            new ConfigParameter
            {
                Name = "preserveUpDirection",
                DisplayName = "保持顶面朝向",
                ParameterType = typeof(bool),
                DefaultValue = "true",
                Required = false,
                ControlType = PortControlType.Switch,
            },
            Number("yawSearchStepDeg", "平面旋转搜索步长(度)", "5"),
            Number("yawCenterDeg", "工装标定角度中心(度)", "0"),
            Number("yawSearchRangeDeg", "工装角度允许偏差(度)", "180"),
        };

    private readonly int _sourceSampleCount;
    private readonly int _targetSampleCount;
    private readonly int _maxIterations;
    private readonly double _trimFraction;
    private readonly double _inlierDistance;
    private readonly double _minimumInlierRatio;
    private readonly bool _preserveUpDirection;
    private readonly double _yawSearchStepDeg;
    private readonly double _yawCenterDeg;
    private readonly double _yawSearchRangeDeg;
    private bool _disposed;

    public partial_surface_pose_alignment(
        int sourceSampleCount = 2000,
        int targetSampleCount = 5000,
        int maxIterations = 30,
        double trimFraction = 0.7,
        double inlierDistance = 1.0,
        double minimumInlierRatio = 0.15,
        bool preserveUpDirection = true,
        double yawSearchStepDeg = 5,
        double yawCenterDeg = 0,
        double yawSearchRangeDeg = 180
    )
    {
        if (sourceSampleCount is < 100 or > 20_000)
            throw new ArgumentOutOfRangeException(nameof(sourceSampleCount));
        if (targetSampleCount is < 100 or > 50_000)
            throw new ArgumentOutOfRangeException(nameof(targetSampleCount));
        if (maxIterations is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(maxIterations));
        if (!double.IsFinite(trimFraction) || trimFraction is < 0.1 or > 1.0)
            throw new ArgumentOutOfRangeException(nameof(trimFraction));
        if (!double.IsFinite(inlierDistance) || inlierDistance <= 0)
            throw new ArgumentOutOfRangeException(nameof(inlierDistance));
        if (!double.IsFinite(minimumInlierRatio) || minimumInlierRatio is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(minimumInlierRatio));
        if (!double.IsFinite(yawSearchStepDeg) || yawSearchStepDeg is < 1 or > 45)
            throw new ArgumentOutOfRangeException(nameof(yawSearchStepDeg));
        if (!double.IsFinite(yawCenterDeg))
            throw new ArgumentOutOfRangeException(nameof(yawCenterDeg));
        if (!double.IsFinite(yawSearchRangeDeg) || yawSearchRangeDeg is < 0 or > 180)
            throw new ArgumentOutOfRangeException(nameof(yawSearchRangeDeg));

        _sourceSampleCount = sourceSampleCount;
        _targetSampleCount = targetSampleCount;
        _maxIterations = maxIterations;
        _trimFraction = trimFraction;
        _inlierDistance = inlierDistance;
        _minimumInlierRatio = minimumInlierRatio;
        _preserveUpDirection = preserveUpDirection;
        _yawSearchStepDeg = yawSearchStepDeg;
        _yawCenterDeg = NormalizeAngleDeg(yawCenterDeg);
        _yawSearchRangeDeg = yawSearchRangeDeg;
    }

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        PointCloudData sourceData =
            context.Get<PointCloudData>("source_cloud")
            ?? throw new InvalidOperationException("上下文变量 'source_cloud' 为空。");
        PointCloudData targetData =
            context.Get<PointCloudData>("target_cloud")
            ?? throw new InvalidOperationException("上下文变量 'target_cloud' 为空。");
        Mat sourceCloud = ValidateCloud(sourceData.PointCloud, "扫描");
        Mat targetCloud = ValidateCloud(targetData.PointCloud, "数模");

        CloudSample source = _preserveUpDirection
            ? CloudSample.CreateUpperEnvelope(sourceCloud, _sourceSampleCount)
            : CloudSample.Create(sourceCloud, _sourceSampleCount);
        CloudSample target = _preserveUpDirection
            ? CloudSample.CreateUpperEnvelope(targetCloud, _targetSampleCount)
            : CloudSample.Create(targetCloud, _targetSampleCount);
        var targetTree = new KdTree3D(target.X, target.Y, target.Z);
        PcaFrame sourceFrame = PcaFrame.Create(source);
        PcaFrame targetFrame = PcaFrame.Create(target);
        double searchDistance = Math.Max(target.Diagonal * 2.0, _inlierDistance * 10.0);

        var candidates = new List<Candidate>(24);
        foreach (
            double[] orientation in EnumeratePrincipalAxisRotations(
                sourceFrame,
                targetFrame,
                _preserveUpDirection,
                _yawSearchStepDeg,
                _yawCenterDeg,
                _yawSearchRangeDeg
            )
        )
        {
            double[] translation = CenterTranslation(
                orientation,
                sourceFrame.Centroid,
                targetFrame.Centroid
            );
            Candidate candidate = RefineCandidate(
                source,
                target,
                targetTree,
                orientation,
                translation,
                searchDistance
            );
            if (
                !_preserveUpDirection
                || _yawSearchRangeDeg >= 180
                || IsYawWithinPrior(candidate.Rotation, _yawCenterDeg, _yawSearchRangeDeg)
            )
                candidates.Add(candidate);
        }

        if (candidates.Count == 0)
            throw new InvalidOperationException(
                $"姿态标定结果超出固定工装角度范围：中心 {_yawCenterDeg:F3}°，"
                    + $"允许偏差 ±{_yawSearchRangeDeg:F3}°。请检查工装、扫描件或标定基准。"
            );

        candidates.Sort((left, right) => left.TrimmedRmse.CompareTo(right.TrimmedRmse));
        Candidate best = candidates[0];
        double secondBestRmse = candidates.Count > 1 ? candidates[1].TrimmedRmse : double.NaN;
        if (best.InlierRatio < _minimumInlierRatio)
        {
            throw new InvalidOperationException(
                $"姿态标定可信度不足：内点比例 {best.InlierRatio:P2}，要求至少 {_minimumInlierRatio:P2}；"
                    + $"裁剪RMSE={best.TrimmedRmse:F4}。请检查扫描区域、数模或内点距离。"
            );
        }

        Mat aligned = PointCloudPoseMath.TransformCloud(sourceCloud, best.Rotation, best.Translation);
        Mat matrix = PointCloudPoseMath.CreateTransformMatrix(best.Rotation, best.Translation);
        var euler = PointCloudPoseMath.EulerZyxDegreesFromRotation(best.Rotation);
        double confidenceGap =
            double.IsFinite(secondBestRmse) && secondBestRmse > 1e-12
                ? (secondBestRmse - best.TrimmedRmse) / secondBestRmse
                : 0;
        var result = new PoseResult
        {
            RotationXDeg = euler.rxDeg,
            RotationYDeg = euler.ryDeg,
            RotationZDeg = euler.rzDeg,
            TranslationX = best.Translation[0],
            TranslationY = best.Translation[1],
            TranslationZ = best.Translation[2],
            RotationMatrix = best.Rotation,
            TransformMatrix =
            [
                best.Rotation[0], best.Rotation[1], best.Rotation[2], best.Translation[0],
                best.Rotation[3], best.Rotation[4], best.Rotation[5], best.Translation[1],
                best.Rotation[6], best.Rotation[7], best.Rotation[8], best.Translation[2],
                0, 0, 0, 1,
            ],
            InlierRatio = best.InlierRatio,
            TrimmedRmse = best.TrimmedRmse,
            SecondBestTrimmedRmse = secondBestRmse,
            ConfidenceGap = confidenceGap,
            Iterations = best.Iterations,
            HypothesisCount = candidates.Count,
            SourceSampleCount = source.Count,
            TargetSampleCount = target.Count,
            EulerConvention = "Rz*Ry*Rx",
            PreserveUpDirection = _preserveUpDirection,
            YawCenterDeg = _yawCenterDeg,
            YawSearchRangeDeg = _yawSearchRangeDeg,
            YawOffsetFromCenterDeg = NormalizeAngleDeg(euler.rzDeg - _yawCenterDeg),
        };

        context.Set(
            "aligned_cloud",
            PointCloudUtils.BuildCloud(
                aligned,
                sourceData.HasColors && sourceData.Colors is not null
                    ? sourceData.Colors.Clone()
                    : null
            )
        );
        context.Set("transform_matrix", matrix);
        context.Set("pose_json", JsonSerializer.Serialize(result, JsonOptions));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private Candidate RefineCandidate(
        CloudSample source,
        CloudSample target,
        KdTree3D targetTree,
        double[] initialRotation,
        double[] initialTranslation,
        double searchDistance
    )
    {
        double[] rotation = (double[])initialRotation.Clone();
        double[] translation = (double[])initialTranslation.Clone();
        int iterations = 0;
        int keepCount = Math.Max(3, (int)Math.Ceiling(source.Count * _trimFraction));

        for (int iteration = 0; iteration < _maxIterations; iteration++)
        {
            var pairs = FindPairs(source, target, targetTree, rotation, translation, searchDistance);
            if (pairs.Count < 3)
                break;
            pairs.Sort((left, right) => left.DistanceSquared.CompareTo(right.DistanceSquared));
            int used = Math.Min(keepCount, pairs.Count);
            var transformed = new List<(double x, double y, double z)>(used);
            var matched = new List<(double x, double y, double z)>(used);
            for (int index = 0; index < used; index++)
            {
                Pair pair = pairs[index];
                transformed.Add((pair.X, pair.Y, pair.Z));
                matched.Add((target.X[pair.TargetIndex], target.Y[pair.TargetIndex], target.Z[pair.TargetIndex]));
            }

            (double[] deltaRotation, double[] deltaTranslation) = _preserveUpDirection
                ? ComputeYawTranslationTransform(transformed, matched)
                : Math3D.ComputeRigidTransform(transformed, matched);
            rotation = PointCloudPoseMath.MultiplyRotation(deltaRotation, rotation);
            translation = PointCloudPoseMath.ComposeTranslation(
                deltaRotation,
                deltaTranslation,
                translation
            );
            iterations = iteration + 1;

            double angleChange = Math.Acos(
                Math.Clamp(
                    (deltaRotation[0] + deltaRotation[4] + deltaRotation[8] - 1) / 2,
                    -1,
                    1
                )
            );
            double translationChange = Math.Sqrt(
                deltaTranslation[0] * deltaTranslation[0]
                    + deltaTranslation[1] * deltaTranslation[1]
                    + deltaTranslation[2] * deltaTranslation[2]
            );
            if (angleChange < 1e-6 && translationChange < 1e-5)
                break;
        }

        List<Pair> finalPairs = FindPairs(
            source,
            target,
            targetTree,
            rotation,
            translation,
            searchDistance
        );
        finalPairs.Sort((left, right) => left.DistanceSquared.CompareTo(right.DistanceSquared));
        int finalKeep = Math.Min(keepCount, finalPairs.Count);
        double sum = 0;
        int inliers = 0;
        double inlierDistanceSquared = _inlierDistance * _inlierDistance;
        for (int index = 0; index < finalPairs.Count; index++)
        {
            if (index < finalKeep)
                sum += finalPairs[index].DistanceSquared;
            if (finalPairs[index].DistanceSquared <= inlierDistanceSquared)
                inliers++;
        }

        return new Candidate
        {
            Rotation = rotation,
            Translation = translation,
            TrimmedRmse = finalKeep > 0 ? Math.Sqrt(sum / finalKeep) : double.MaxValue,
            InlierRatio = source.Count > 0 ? (double)inliers / source.Count : 0,
            Iterations = iterations,
        };
    }

    private static List<Pair> FindPairs(
        CloudSample source,
        CloudSample target,
        KdTree3D targetTree,
        double[] rotation,
        double[] translation,
        double searchDistance
    )
    {
        var pairs = new List<Pair>(source.Count);
        for (int index = 0; index < source.Count; index++)
        {
            double x = rotation[0] * source.X[index] + rotation[1] * source.Y[index] + rotation[2] * source.Z[index] + translation[0];
            double y = rotation[3] * source.X[index] + rotation[4] * source.Y[index] + rotation[5] * source.Z[index] + translation[1];
            double z = rotation[6] * source.X[index] + rotation[7] * source.Y[index] + rotation[8] * source.Z[index] + translation[2];
            int nearest = targetTree.FindNearestNeighborWithin(
                (float)x,
                (float)y,
                (float)z,
                searchDistance,
                out double distanceSquared
            );
            if (nearest >= 0)
                pairs.Add(new Pair(x, y, z, nearest, distanceSquared));
        }
        return pairs;
    }

    private static IEnumerable<double[]> EnumeratePrincipalAxisRotations(
        PcaFrame source,
        PcaFrame target,
        bool preserveUpDirection,
        double yawSearchStepDeg,
        double yawCenterDeg,
        double yawSearchRangeDeg
    )
    {
        if (preserveUpDirection)
        {
            if (yawSearchRangeDeg >= 180)
            {
                int hypothesisCount = (int)Math.Ceiling(360.0 / yawSearchStepDeg);
                for (int hypothesis = 0; hypothesis < hypothesisCount; hypothesis++)
                {
                    double yaw = hypothesis * 2 * Math.PI / hypothesisCount;
                    double c = Math.Cos(yaw);
                    double s = Math.Sin(yaw);
                    yield return [c, -s, 0, s, c, 0, 0, 0, 1];
                }
                yield break;
            }

            var yawCandidates = new SortedSet<double>();
            yawCandidates.Add(yawCenterDeg - yawSearchRangeDeg);
            yawCandidates.Add(yawCenterDeg);
            yawCandidates.Add(yawCenterDeg + yawSearchRangeDeg);
            for (
                double yaw = yawCenterDeg - yawSearchRangeDeg;
                yaw <= yawCenterDeg + yawSearchRangeDeg + 1e-9;
                yaw += yawSearchStepDeg
            )
                yawCandidates.Add(yaw);
            foreach (double yawDeg in yawCandidates)
            {
                double yaw = yawDeg * Math.PI / 180.0;
                double c = Math.Cos(yaw);
                double s = Math.Sin(yaw);
                yield return [c, -s, 0, s, c, 0, 0, 0, 1];
            }
            yield break;
        }

        int[][] permutations =
        [
            [0, 1, 2], [0, 2, 1], [1, 0, 2],
            [1, 2, 0], [2, 0, 1], [2, 1, 0],
        ];
        foreach (int[] permutation in permutations)
        {
            int parity = PermutationParity(permutation);
            foreach (int sx in new[] { -1, 1 })
                foreach (int sy in new[] { -1, 1 })
                    foreach (int sz in new[] { -1, 1 })
                    {
                        if (parity * sx * sy * sz != 1)
                            continue;
                        int[] signs = { sx, sy, sz };
                        var rotation = new double[9];
                        for (int row = 0; row < 3; row++)
                            for (int col = 0; col < 3; col++)
                            {
                                double value = 0;
                                for (int axis = 0; axis < 3; axis++)
                                    value +=
                                        target.Basis[row, permutation[axis]]
                                        * signs[axis]
                                        * source.Basis[col, axis];
                                rotation[row * 3 + col] = value;
                            }
                        yield return rotation;
                    }
        }
    }

    private static (double[] rotation, double[] translation) ComputeYawTranslationTransform(
        IReadOnlyList<(double x, double y, double z)> source,
        IReadOnlyList<(double x, double y, double z)> target
    )
    {
        int count = source.Count;
        double sx = 0, sy = 0, sz = 0, tx = 0, ty = 0, tz = 0;
        for (int index = 0; index < count; index++)
        {
            sx += source[index].x; sy += source[index].y; sz += source[index].z;
            tx += target[index].x; ty += target[index].y; tz += target[index].z;
        }
        sx /= count; sy /= count; sz /= count;
        tx /= count; ty /= count; tz /= count;

        double dot = 0;
        double cross = 0;
        for (int index = 0; index < count; index++)
        {
            double sourceX = source[index].x - sx;
            double sourceY = source[index].y - sy;
            double targetX = target[index].x - tx;
            double targetY = target[index].y - ty;
            dot += sourceX * targetX + sourceY * targetY;
            cross += sourceX * targetY - sourceY * targetX;
        }
        double angle = Math.Atan2(cross, dot);
        double c = Math.Cos(angle);
        double s = Math.Sin(angle);
        double[] rotation = [c, -s, 0, s, c, 0, 0, 0, 1];
        double[] translation = [tx - c * sx + s * sy, ty - s * sx - c * sy, tz - sz];
        return (rotation, translation);
    }

    private static int PermutationParity(int[] permutation)
    {
        int inversions = 0;
        for (int i = 0; i < permutation.Length; i++)
            for (int j = i + 1; j < permutation.Length; j++)
                if (permutation[i] > permutation[j])
                    inversions++;
        return inversions % 2 == 0 ? 1 : -1;
    }

    private static bool IsYawWithinPrior(
        double[] rotation,
        double centerDeg,
        double rangeDeg
    )
    {
        double yawDeg = Math.Atan2(rotation[3], rotation[0]) * 180.0 / Math.PI;
        return Math.Abs(NormalizeAngleDeg(yawDeg - centerDeg)) <= rangeDeg + 1e-6;
    }

    private static double NormalizeAngleDeg(double angleDeg)
    {
        double normalized = angleDeg % 360.0;
        if (normalized <= -180)
            normalized += 360;
        else if (normalized > 180)
            normalized -= 360;
        return normalized;
    }

    private static double[] CenterTranslation(double[] rotation, double[] source, double[] target) =>
    [
        target[0] - rotation[0] * source[0] - rotation[1] * source[1] - rotation[2] * source[2],
        target[1] - rotation[3] * source[0] - rotation[4] * source[1] - rotation[5] * source[2],
        target[2] - rotation[6] * source[0] - rotation[7] * source[1] - rotation[8] * source[2],
    ];

    private static Mat ValidateCloud(Mat? cloud, string name)
    {
        if (cloud is null || cloud.Empty() || cloud.Rows < 3 || cloud.Cols < 3)
            throw new InvalidOperationException($"{name}点云为空或有效点不足 3 个。");
        return cloud;
    }

    private static ConfigParameter Number(string name, string displayName, string defaultValue) =>
        new()
        {
            Name = name,
            DisplayName = displayName,
            ParameterType = name.Contains("Count") || name == "maxIterations" ? typeof(int) : typeof(double),
            DefaultValue = defaultValue,
            Required = false,
            ControlType = PortControlType.Input,
        };

    private sealed class CloudSample
    {
        public required float[] X { get; init; }
        public required float[] Y { get; init; }
        public required float[] Z { get; init; }
        public int Count => X.Length;
        public double Diagonal { get; init; }

        public static CloudSample Create(Mat cloud, int maximumCount)
        {
            int count = Math.Min(maximumCount, cloud.Rows);
            var x = new float[count];
            var y = new float[count];
            var z = new float[count];
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            for (int index = 0; index < count; index++)
            {
                int sourceIndex = count == cloud.Rows
                    ? index
                    : (int)((long)index * cloud.Rows / count);
                x[index] = cloud.Get<float>(sourceIndex, 0);
                y[index] = cloud.Get<float>(sourceIndex, 1);
                z[index] = cloud.Get<float>(sourceIndex, 2);
                minX = Math.Min(minX, x[index]); maxX = Math.Max(maxX, x[index]);
                minY = Math.Min(minY, y[index]); maxY = Math.Max(maxY, y[index]);
                minZ = Math.Min(minZ, z[index]); maxZ = Math.Max(maxZ, z[index]);
            }
            double diagonal = Math.Sqrt(
                (maxX - minX) * (maxX - minX)
                    + (maxY - minY) * (maxY - minY)
                    + (maxZ - minZ) * (maxZ - minZ)
            );
            return new CloudSample { X = x, Y = y, Z = z, Diagonal = diagonal };
        }

        public static CloudSample CreateUpperEnvelope(Mat cloud, int maximumCount)
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            for (int index = 0; index < cloud.Rows; index++)
            {
                float x = cloud.Get<float>(index, 0);
                float y = cloud.Get<float>(index, 1);
                if (!float.IsFinite(x) || !float.IsFinite(y))
                    continue;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
            double span = Math.Max(maxX - minX, maxY - minY);
            double cellSize = Math.Max(span / Math.Sqrt(maximumCount * 0.8), 1e-6);
            var upperByCell = new Dictionary<(int x, int y), (float x, float y, float z)>();
            for (int index = 0; index < cloud.Rows; index++)
            {
                float x = cloud.Get<float>(index, 0);
                float y = cloud.Get<float>(index, 1);
                float z = cloud.Get<float>(index, 2);
                if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
                    continue;
                var key = ((int)Math.Floor((x - minX) / cellSize), (int)Math.Floor((y - minY) / cellSize));
                if (!upperByCell.TryGetValue(key, out var current) || z > current.z)
                    upperByCell[key] = (x, y, z);
            }

            // 极少量飞点会明显扭曲 PCA；用 XY 的 1%–99% 分位裁掉外围孤立点。
            List<(float x, float y, float z)> envelope = upperByCell.Values.ToList();
            float[] sortedX = envelope.Select(point => point.x).Order().ToArray();
            float[] sortedY = envelope.Select(point => point.y).Order().ToArray();
            float lowX = Percentile(sortedX, 0.01), highX = Percentile(sortedX, 0.99);
            float lowY = Percentile(sortedY, 0.01), highY = Percentile(sortedY, 0.99);
            envelope = envelope
                .Where(point => point.x >= lowX && point.x <= highX && point.y >= lowY && point.y <= highY)
                .ToList();
            if (envelope.Count < 100)
                return Create(cloud, maximumCount);

            int count = Math.Min(maximumCount, envelope.Count);
            var xResult = new float[count];
            var yResult = new float[count];
            var zResult = new float[count];
            double outMinX = double.MaxValue, outMinY = double.MaxValue, outMinZ = double.MaxValue;
            double outMaxX = double.MinValue, outMaxY = double.MinValue, outMaxZ = double.MinValue;
            for (int index = 0; index < count; index++)
            {
                int sourceIndex = count == envelope.Count
                    ? index
                    : (int)((long)index * envelope.Count / count);
                var point = envelope[sourceIndex];
                xResult[index] = point.x; yResult[index] = point.y; zResult[index] = point.z;
                outMinX = Math.Min(outMinX, point.x); outMaxX = Math.Max(outMaxX, point.x);
                outMinY = Math.Min(outMinY, point.y); outMaxY = Math.Max(outMaxY, point.y);
                outMinZ = Math.Min(outMinZ, point.z); outMaxZ = Math.Max(outMaxZ, point.z);
            }
            double diagonal = Math.Sqrt(
                (outMaxX - outMinX) * (outMaxX - outMinX)
                    + (outMaxY - outMinY) * (outMaxY - outMinY)
                    + (outMaxZ - outMinZ) * (outMaxZ - outMinZ)
            );
            return new CloudSample { X = xResult, Y = yResult, Z = zResult, Diagonal = diagonal };
        }

        private static float Percentile(float[] sorted, double fraction)
        {
            if (sorted.Length == 0)
                return 0;
            double position = (sorted.Length - 1) * fraction;
            int lower = (int)Math.Floor(position);
            int upper = Math.Min(lower + 1, sorted.Length - 1);
            double weight = position - lower;
            return (float)(sorted[lower] * (1 - weight) + sorted[upper] * weight);
        }
    }

    private sealed class PcaFrame
    {
        public required double[] Centroid { get; init; }
        public required double[,] Basis { get; init; }

        public static PcaFrame Create(CloudSample cloud)
        {
            double cx = cloud.X.Average(value => (double)value);
            double cy = cloud.Y.Average(value => (double)value);
            double cz = cloud.Z.Average(value => (double)value);
            double c00 = 0, c01 = 0, c02 = 0, c11 = 0, c12 = 0, c22 = 0;
            for (int index = 0; index < cloud.Count; index++)
            {
                double x = cloud.X[index] - cx;
                double y = cloud.Y[index] - cy;
                double z = cloud.Z[index] - cz;
                c00 += x * x; c01 += x * y; c02 += x * z;
                c11 += y * y; c12 += y * z; c22 += z * z;
            }
            var (_, basis) = Math3D.Jacobi3x3(c00, c01, c02, c11, c12, c22);
            double determinant =
                basis[0, 0] * (basis[1, 1] * basis[2, 2] - basis[1, 2] * basis[2, 1])
                - basis[0, 1] * (basis[1, 0] * basis[2, 2] - basis[1, 2] * basis[2, 0])
                + basis[0, 2] * (basis[1, 0] * basis[2, 1] - basis[1, 1] * basis[2, 0]);
            if (determinant < 0)
                for (int row = 0; row < 3; row++)
                    basis[row, 2] = -basis[row, 2];
            return new PcaFrame { Centroid = [cx, cy, cz], Basis = basis };
        }
    }

    private readonly record struct Pair(
        double X,
        double Y,
        double Z,
        int TargetIndex,
        double DistanceSquared
    );

    private sealed class Candidate
    {
        public required double[] Rotation { get; init; }
        public required double[] Translation { get; init; }
        public double TrimmedRmse { get; init; }
        public double InlierRatio { get; init; }
        public int Iterations { get; init; }
    }

    public sealed class PoseResult
    {
        public double RotationXDeg { get; set; }
        public double RotationYDeg { get; set; }
        public double RotationZDeg { get; set; }
        public double TranslationX { get; set; }
        public double TranslationY { get; set; }
        public double TranslationZ { get; set; }
        public double[] RotationMatrix { get; set; } = [];
        public double[] TransformMatrix { get; set; } = [];
        public double InlierRatio { get; set; }
        public double TrimmedRmse { get; set; }
        public double SecondBestTrimmedRmse { get; set; }
        public double ConfidenceGap { get; set; }
        public int Iterations { get; set; }
        public int HypothesisCount { get; set; }
        public int SourceSampleCount { get; set; }
        public int TargetSampleCount { get; set; }
        public string EulerConvention { get; set; } = string.Empty;
        public bool PreserveUpDirection { get; set; }
        public double YawCenterDeg { get; set; }
        public double YawSearchRangeDeg { get; set; }
        public double YawOffsetFromCenterDeg { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
