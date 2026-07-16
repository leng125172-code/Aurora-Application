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
            new VisionParameter<string>
            {
                ParameterName = "result_json",
                DisplayName = "比较结果",
                ParameterType = typeof(string),
                ControlType = PortControlType.Download,
            },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "useCoarseRegistration",
                DisplayName = "启用粗配准",
                ParameterType = typeof(bool),
                DefaultValue = "false",
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
                DisplayName = "最大对应点距离",
                ParameterType = typeof(double),
                DefaultValue = "0.1",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "distanceThreshold",
                DisplayName = "缺陷阈值",
                ParameterType = typeof(double),
                DefaultValue = "0.05",
                Required = false,
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
        };

    private readonly bool _useCoarseRegistration;
    private readonly string _registrationMethod;
    private readonly int _maxIterations;
    private readonly double _maxCorrespondenceDistance;
    private readonly double _distanceThreshold;
    private readonly int _imageResolution;
    private readonly double _voxelSize;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public cloud_compare(
        bool useCoarseRegistration = false,
        string registrationMethod = "point_to_plane",
        int maxIterations = 50,
        double maxCorrespondenceDistance = 0.1,
        double distanceThreshold = 0.05,
        int imageResolution = 512,
        double voxelSize = 0
    )
    {
        _useCoarseRegistration = useCoarseRegistration;
        _registrationMethod = registrationMethod;
        _maxIterations = maxIterations;
        _maxCorrespondenceDistance = maxCorrespondenceDistance;
        _distanceThreshold = distanceThreshold;
        _imageResolution = imageResolution;
        _voxelSize = voxelSize;
    }

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        PointCloudData sourceData =
            context.Get<PointCloudData>("source_cloud")
            ?? throw new InvalidOperationException(
                "上下文变量 'source_cloud' 为空，请确认输入绑定已正确设置。"
            );

        PointCloudData targetData =
            context.Get<PointCloudData>("target_cloud")
            ?? throw new InvalidOperationException(
                "上下文变量 'target_cloud' 为空，请确认输入绑定已正确设置。"
            );

        Mat sourceCloud = sourceData.PointCloud!;
        Mat targetCloud = targetData.PointCloud!;

        if (sourceCloud is null || sourceCloud.Empty())
            throw new InvalidOperationException("源点云为空，无法执行 3D 比较。");
        if (targetCloud is null || targetCloud.Empty())
            throw new InvalidOperationException("目标点云为空，无法执行 3D 比较。");

        int sourceCount = sourceCloud.Rows;
        int targetCount = targetCloud.Rows;

        if (sourceCount < 3)
            throw new InvalidOperationException("源点云点数不足（< 3），无法执行比较。");
        if (targetCount < 3)
            throw new InvalidOperationException("目标点云点数不足（< 3），无法执行比较。");

        Mat workingSource = sourceCloud.Clone();
        Mat workingTarget = targetCloud.Clone();

        if (_voxelSize > 0)
        {
            workingSource = VoxelDownsample(workingSource, _voxelSize);
            workingTarget = VoxelDownsample(workingTarget, _voxelSize);
            sourceCount = workingSource.Rows;
            targetCount = workingTarget.Rows;
        }

        float[] srcX = new float[sourceCount];
        float[] srcY = new float[sourceCount];
        float[] srcZ = new float[sourceCount];
        for (int i = 0; i < sourceCount; i++)
        {
            srcX[i] = workingSource.Get<float>(i, 0);
            srcY[i] = workingSource.Get<float>(i, 1);
            srcZ[i] = workingSource.Get<float>(i, 2);
        }

        float[] tgtX = new float[targetCount];
        float[] tgtY = new float[targetCount];
        float[] tgtZ = new float[targetCount];
        for (int i = 0; i < targetCount; i++)
        {
            tgtX[i] = workingTarget.Get<float>(i, 0);
            tgtY[i] = workingTarget.Get<float>(i, 1);
            tgtZ[i] = workingTarget.Get<float>(i, 2);
        }

        double[] accumR = { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        double[] accumT = { 0, 0, 0 };

        if (_useCoarseRegistration)
        {
            (accumR, accumT) = CoarseRegistration(srcX, srcY, srcZ, tgtX, tgtY, tgtZ);

            for (int i = 0; i < sourceCount; i++)
            {
                double x = srcX[i], y = srcY[i], z = srcZ[i];
                srcX[i] = (float)(accumR[0] * x + accumR[1] * y + accumR[2] * z + accumT[0]);
                srcY[i] = (float)(accumR[3] * x + accumR[4] * y + accumR[5] * z + accumT[1]);
                srcZ[i] = (float)(accumR[6] * x + accumR[7] * y + accumR[8] * z + accumT[2]);
            }
        }

        switch (_registrationMethod.ToLower())
        {
            case "icp":
                (accumR, accumT) = IcpRegistration(srcX, srcY, srcZ, tgtX, tgtY, tgtZ, _maxCorrespondenceDistance, _maxIterations);
                break;
            case "point_to_plane":
                (accumR, accumT) = PointToPlaneIcp(srcX, srcY, srcZ, tgtX, tgtY, tgtZ, _maxCorrespondenceDistance, _maxIterations);
                break;
            case "ndt":
                (accumR, accumT) = NdtRegistration(srcX, srcY, srcZ, tgtX, tgtY, tgtZ);
                break;
            default:
                throw new InvalidOperationException($"未知的配准方法: {_registrationMethod}");
        }

        for (int i = 0; i < sourceCount; i++)
        {
            double x = srcX[i], y = srcY[i], z = srcZ[i];
            srcX[i] = (float)(accumR[0] * x + accumR[1] * y + accumR[2] * z + accumT[0]);
            srcY[i] = (float)(accumR[3] * x + accumR[4] * y + accumR[5] * z + accumT[1]);
            srcZ[i] = (float)(accumR[6] * x + accumR[7] * y + accumR[8] * z + accumT[2]);
        }

        Mat alignedCloud = new Mat(sourceCount, 3, MatType.CV_32FC1);
        for (int i = 0; i < sourceCount; i++)
        {
            alignedCloud.Set(i, 0, srcX[i]);
            alignedCloud.Set(i, 1, srcY[i]);
            alignedCloud.Set(i, 2, srcZ[i]);
        }

        Mat transformMatrix = Mat.Eye(4, 4, MatType.CV_64FC1);
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
                transformMatrix.Set(r, c, accumR[r * 3 + c]);
            transformMatrix.Set(r, 3, accumT[r]);
        }

        var (distances, maxDist, meanDist, stdDev, defectCount) = ComputeCloudToCloudDistance(
            srcX, srcY, srcZ, tgtX, tgtY, tgtZ, _maxCorrespondenceDistance, _distanceThreshold
        );

        Mat distanceImage = GenerateDistanceHeatmap(
            alignedCloud, distances, _imageResolution, maxDist
        );

        double rotationAngle = ComputeRotationAngle(accumR);
        double translationMagnitude = Math.Sqrt(
            accumT[0] * accumT[0] + accumT[1] * accumT[1] + accumT[2] * accumT[2]
        );

        double minDist = double.MaxValue;
        for (int i = 0; i < distances.Rows; i++)
        {
            double d = distances.Get<double>(i, 0);
            if (d >= 0 && d < minDist)
                minDist = d;
        }
        if (minDist == double.MaxValue) minDist = 0;

        var result = new CloudCompareResult
        {
            SourcePointCount = sourceCount,
            TargetPointCount = targetCount,
            HeightDifference = new HeightDifferenceStats
            {
                MaxDistance = maxDist,
                MeanDistance = meanDist,
                StdDev = stdDev,
                MinDistance = minDist,
                DefectCount = defectCount,
                DefectRatio = sourceCount > 0 ? (double)defectCount / sourceCount : 0,
                Threshold = _distanceThreshold,
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
        };

        string resultJson = JsonSerializer.Serialize(result, JsonOptions);

        var outputCloud = new PointCloudData();
        outputCloud.Value = alignedCloud;
        if (sourceData.HasColors && sourceData.Colors != null)
            outputCloud.SetColors(sourceData.Colors.Clone());

        context.Set("aligned_cloud", outputCloud);
        context.Set("transform_matrix", transformMatrix);
        context.Set("distance_mat", distances);
        context.Set("distance_image", distanceImage);
        context.Set("result_json", resultJson);
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

    private static (double[], double[]) CoarseRegistration(
        float[] srcX, float[] srcY, float[] srcZ,
        float[] tgtX, float[] tgtY, float[] tgtZ
    )
    {
        int srcCount = srcX.Length;
        int tgtCount = tgtX.Length;
        int sampleCount = Math.Min(300, Math.Min(srcCount, tgtCount));

        int[] srcSamples = UniformSample(srcCount, sampleCount);
        int[] tgtSamples = UniformSample(tgtCount, sampleCount);

        var srcGrid = new SpatialHashGrid(srcX, srcY, srcZ, 0.05f, srcCount);
        var tgtGrid = new SpatialHashGrid(tgtX, tgtY, tgtZ, 0.05f, tgtCount);

        var srcFeat = ComputeFpfhFeatures(srcX, srcY, srcZ, srcGrid, srcSamples, 0.05);
        var tgtFeat = ComputeFpfhFeatures(tgtX, tgtY, tgtZ, tgtGrid, tgtSamples, 0.05);

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
                correspondences.Add((srcSamples[i], tgtSamples[i]));
        }

        if (correspondences.Count < 3)
            return (new[] { 1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0 }, new[] { 0.0, 0.0, 0.0 });

        Random rng = new Random();
        double[] bestR = { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        double[] bestT = { 0, 0, 0 };
        int bestInliers = -1;
        double threshSq = 0.05 * 0.05;

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

        return (bestR, bestT);
    }

    private static (double[], double[]) IcpRegistration(
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

        for (int iter = 0; iter < maxIterations; iter++)
        {
            var correspondences = new List<(int, int)>();
            for (int i = 0; i < srcCount; i++)
            {
                int nearest = grid.FindNearestNeighbor(srcX[i], srcY[i], srcZ[i], out _);
                if (nearest < 0) continue;
                float dx = srcX[i] - tgtX[nearest];
                float dy = srcY[i] - tgtY[nearest];
                float dz = srcZ[i] - tgtZ[nearest];
                double distSq = dx * dx + dy * dy + dz * dz;
                if (distSq <= maxCorrDistSq)
                    correspondences.Add((i, nearest));
            }

            if (correspondences.Count < 3) break;

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

            if (Math.Abs(prevRmse - rmse) < 1e-6) break;
            prevRmse = rmse;
        }

        return (R, t);
    }

    private static (double[], double[]) PointToPlaneIcp(
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

        for (int iter = 0; iter < maxIterations; iter++)
        {
            double[,] A = new double[6, 6];
            double[] b = new double[6];
            int corrCount = 0;

            for (int i = 0; i < srcCount; i++)
            {
                int nn = grid.FindNearestNeighbor(srcX[i], srcY[i], srcZ[i], out double distSq);
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

            if (corrCount < 6) break;

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
            if (transformChange < 1e-8) break;
        }

        return (R, t);
    }

    private static (double[], double[]) NdtRegistration(
        float[] srcX, float[] srcY, float[] srcZ,
        float[] tgtX, float[] tgtY, float[] tgtZ
    )
    {
        int srcCount = srcX.Length;
        int tgtCount = tgtX.Length;

        double resolution = 1.0;
        var voxels = BuildNdtVoxels(tgtX, tgtY, tgtZ, resolution);

        double[] p = new double[6];
        double[] hStep = { 1e-3, 1e-3, 1e-3, 1e-4, 1e-4, 1e-4 };
        double prevScore = NdtScore(p, srcX, srcY, srcZ, voxels, resolution);

        for (int iter = 0; iter < 35; iter++)
        {
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

            if (change < 1e-8) break;
        }

        double[] R = RodriguesToMatrix(p[3], p[4], p[5]);
        double[] t = { p[0], p[1], p[2] };

        return (R, t);
    }

    private static (Mat, double, double, double, int) ComputeCloudToCloudDistance(
        float[] srcX, float[] srcY, float[] srcZ,
        float[] tgtX, float[] tgtY, float[] tgtZ,
        double maxCorrespondenceDistance, double distanceThreshold
    )
    {
        int srcCount = srcX.Length;
        int tgtCount = tgtX.Length;

        var grid = new SpatialHashGrid(tgtX, tgtY, tgtZ, (float)maxCorrespondenceDistance, tgtCount);

        Mat distances = new Mat(srcCount, 1, MatType.CV_64FC1);
        double maxDist = 0, sumDist = 0;
        int defectCount = 0;

        for (int i = 0; i < srcCount; i++)
        {
            int nearestIdx = grid.FindNearestNeighbor(srcX[i], srcY[i], srcZ[i], out _);
            double dist;

            if (nearestIdx >= 0)
            {
                float dx = srcX[i] - tgtX[nearestIdx];
                float dy = srcY[i] - tgtY[nearestIdx];
                float dz = srcZ[i] - tgtZ[nearestIdx];
                dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }
            else
            {
                dist = -1;
            }

            if (dist > 0)
            {
                distances.Set(i, 0, dist);
                sumDist += dist;
                if (dist > maxDist) maxDist = dist;
                if (dist > distanceThreshold) defectCount++;
            }
            else
            {
                distances.Set(i, 0, double.MaxValue);
            }
        }

        int validCount = srcCount - defectCount;
        double meanDist = validCount > 0 ? sumDist / validCount : 0;

        double sumSq = 0;
        for (int i = 0; i < srcCount; i++)
        {
            double d = distances.Get<double>(i, 0);
            if (d >= 0 && d < double.MaxValue)
                sumSq += Math.Pow(d - meanDist, 2);
        }
        double stdDev = validCount > 1 ? Math.Sqrt(sumSq / validCount) : 0;

        return (distances, maxDist, meanDist, stdDev, defectCount);
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
    }

    public class HeightDifferenceStats
    {
        public double MaxDistance { get; set; }
        public double MeanDistance { get; set; }
        public double StdDev { get; set; }
        public double MinDistance { get; set; }
        public int DefectCount { get; set; }
        public double DefectRatio { get; set; }
        public double Threshold { get; set; }
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