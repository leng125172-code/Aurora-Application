using System.Text.Json;
using AuroraStruct3D.CalibrationManagement.Results;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BlobStoring;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Uow;

namespace AuroraStruct3D.CalibrationManagement.Projects.Jobs;

/// <summary>
/// 标定计算后台任务：异步执行多目相机内外参与结构光标定，写入 <see cref="CalibrationResult"/>。
/// </summary>
/// <remarks>
/// Phase 4：已集成 OpenCvSharp4，对棋盘格 / 圆形点阵标定板执行真实的角点检测 + 单相机内参 +
/// 多相机外参求解。AprilTag 标定板 OpenCV 主仓不支持，仍按占位写入并记录警告。
/// </remarks>
public class CalibrationComputeJob
    : AsyncBackgroundJob<CalibrationComputeJobArgs>,
        ITransientDependency
{
    private readonly ICalibrationProjectRepository _projectRepository;
    private readonly ICalibrationResultRepository _resultRepository;
    private readonly IBlobContainer<CalibrationImageBlobContainer> _imageBlobContainer;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ILogger<CalibrationComputeJob> _logger;

    public CalibrationComputeJob(
        ICalibrationProjectRepository projectRepository,
        ICalibrationResultRepository resultRepository,
        IBlobContainer<CalibrationImageBlobContainer> imageBlobContainer,
        IUnitOfWorkManager unitOfWorkManager,
        IGuidGenerator guidGenerator,
        ILogger<CalibrationComputeJob> logger
    )
    {
        _projectRepository = projectRepository;
        _resultRepository = resultRepository;
        _imageBlobContainer = imageBlobContainer;
        _unitOfWorkManager = unitOfWorkManager;
        _guidGenerator = guidGenerator;
        _logger = logger;
    }

    /// <inheritdoc/>
    public override async Task ExecuteAsync(CalibrationComputeJobArgs args)
    {
        Guid projectId = args.CalibrationProjectId;
        _logger.LogInformation("[CalibrationComputeJob] 开始计算工程 {ProjectId}", projectId);

        // ── 第一步：将工程状态切换为"计算中" ──
        using (IUnitOfWork uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            CalibrationProject? project = await _projectRepository.FindWithDetailsAsync(projectId);
            if (project is null)
            {
                _logger.LogWarning("[CalibrationComputeJob] 工程 {ProjectId} 不存在，任务终止", projectId);
                return;
            }

            project.TransitionStatus(CalibrationProjectStatus.Computing);
            await _projectRepository.UpdateAsync(project);
            await uow.CompleteAsync();
        }

        // ── 第二步：执行 OpenCV 计算 ──
        ComputationOutput output;
        try
        {
            CalibrationProject? loaded = await _projectRepository.FindWithDetailsAsync(projectId);
            if (loaded is null)
            {
                return;
            }

            output = await RunOpenCvComputationAsync(loaded);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CalibrationComputeJob] 工程 {ProjectId} 计算异常", projectId);

            using IUnitOfWork failUow = _unitOfWorkManager.Begin(
                requiresNew: true,
                isTransactional: true
            );
            CalibrationProject? failedProject = await _projectRepository.FindWithDetailsAsync(
                projectId
            );
            if (failedProject is not null)
            {
                failedProject.TransitionStatus(CalibrationProjectStatus.Failed, ex.Message);
                await _projectRepository.UpdateAsync(failedProject);
                await failUow.CompleteAsync();
            }
            return;
        }

        // ── 第三步：写入 CalibrationResult + 回填单图误差 + 切换工程状态 ──
        using (IUnitOfWork uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            CalibrationProject? project = await _projectRepository.FindWithDetailsAsync(projectId);
            if (project is null)
            {
                return;
            }

            // 回填每张图像的重投影误差
            foreach (CalibrationCaptureFrame frame in project.Frames)
            {
                foreach (CalibrationCaptureImage image in frame.Images)
                {
                    if (output.PerImageReprojErrors.TryGetValue(image.Id, out double err))
                    {
                        image.SetReprojectionError(err);
                    }
                }
            }

            int maxVersion = await _resultRepository.GetMaxVersionAsync(projectId);
            CalibrationResult result = new(
                _guidGenerator.Create(),
                projectId,
                project.CalibrationDeviceId,
                version: maxVersion + 1
            );
            result.SetCameraIntrinsics(output.CameraIntrinsicsJson);
            result.SetCameraExtrinsics(output.CameraExtrinsicsJson);
            if (!string.IsNullOrEmpty(output.StructuredLightCalibrationJson))
            {
                result.SetStructuredLightCalibration(output.StructuredLightCalibrationJson);
            }
            result.SetErrorStatistics(
                output.OverallReprojectionError,
                output.MaxError,
                output.MinError,
                output.MeanError,
                output.RmsError
            );

            // 首次结果默认生效；已有生效版本则保持原状（用户可后续切换）
            List<CalibrationResult> existingActives = await _resultRepository.GetActiveResultsAsync(
                projectId
            );
            if (existingActives.Count == 0)
            {
                result.SetActive(true);
            }

            await _resultRepository.InsertAsync(result);

            project.TransitionStatus(CalibrationProjectStatus.Completed);
            await _projectRepository.UpdateAsync(project);

            await uow.CompleteAsync();
        }

        _logger.LogInformation(
            "[CalibrationComputeJob] 工程 {ProjectId} 计算完成，重投影误差 RMS={Rms:F4}px",
            projectId,
            output.RmsError
        );
    }

    /// <summary>
    /// OpenCV 标定计算主流程：
    ///   1. 加载所有已接受帧的图像字节流 → Cv2.ImDecode 解码
    ///   2. 按 cameraDeviceId 分组，依标定板类型检测角点（FindChessboardCornersSB / FindCirclesGrid）
    ///   3. 逐相机调用 Cv2.CalibrateCamera 求解内参 + 畸变 + 单图重投影误差
    ///   4. 以 Master 角色为基准，对每个从相机调用 Cv2.StereoCalibrate 求解外参 [R|t]
    ///   5. 序列化为 JSON 并统计整体误差
    /// </summary>
    private async Task<ComputationOutput> RunOpenCvComputationAsync(CalibrationProject project)
    {
        Point3f[]? objectPointTemplate = BuildObjectPointTemplate(project);
        if (objectPointTemplate is null)
        {
            _logger.LogWarning(
                "[CalibrationComputeJob] 标定板类型 {Type} 当前不支持自动检测，写入占位结果",
                project.BoardType
            );
            return BuildPlaceholderOutput(project);
        }

        Size patternSize = new(project.BoardCols, project.BoardRows);

        Dictionary<Guid, CameraSamples> cameraGroups = await LoadCameraSamplesAsync(
            project,
            patternSize,
            objectPointTemplate
        );

        if (cameraGroups.Count == 0 || cameraGroups.Values.All(c => c.ValidFrameCount == 0))
        {
            _logger.LogWarning(
                "[CalibrationComputeJob] 工程 {ProjectId} 未检测到任何有效角点，写入占位结果",
                project.Id
            );
            return BuildPlaceholderOutput(project);
        }

        // ── 单相机内参标定 ──
        Dictionary<Guid, IntrinsicResult> intrinsicResults = new();
        Dictionary<Guid, double> perImageErrors = new();
        List<double> allErrors = new();

        foreach (KeyValuePair<Guid, CameraSamples> kv in cameraGroups)
        {
            CameraSamples samples = kv.Value;
            if (samples.ValidFrameCount < 3)
            {
                _logger.LogWarning(
                    "[CalibrationComputeJob] 相机 {CameraId} 仅 {N} 帧检测到角点，少于 3 帧无法标定",
                    kv.Key,
                    samples.ValidFrameCount
                );
                continue;
            }

            IntrinsicResult intrin = CalibrateSingleCamera(samples, kv.Key);
            intrinsicResults[kv.Key] = intrin;

            List<FrameSample> validFrames = samples.Frames.Where(f => f.Corners is not null).ToList();
            for (int i = 0; i < validFrames.Count; i++)
            {
                double err = i < intrin.PerFrameErrors.Count ? intrin.PerFrameErrors[i] : 0.0;
                perImageErrors[validFrames[i].ImageEntityId] = err;
                allErrors.Add(err);
            }
        }

        // ── 多相机外参标定（Master 为基准）──
        Dictionary<string, ExtrinsicResult> extrinsicResults = new();
        Guid? masterId = cameraGroups
            .Where(kv => IsMasterRole(kv.Value.Role))
            .Select(kv => (Guid?)kv.Key)
            .FirstOrDefault();

        if (masterId.HasValue && intrinsicResults.ContainsKey(masterId.Value))
        {
            CameraSamples masterSamples = cameraGroups[masterId.Value];
            IntrinsicResult masterIntrin = intrinsicResults[masterId.Value];

            foreach (KeyValuePair<Guid, CameraSamples> kv in cameraGroups)
            {
                if (kv.Key == masterId.Value)
                {
                    continue;
                }
                if (!intrinsicResults.TryGetValue(kv.Key, out IntrinsicResult? slaveIntrin))
                {
                    continue;
                }

                ExtrinsicResult? ex = CalibrateStereoPair(
                    masterSamples,
                    kv.Value,
                    masterIntrin,
                    slaveIntrin
                );
                if (ex is not null)
                {
                    extrinsicResults[$"{kv.Key:N}->{masterId.Value:N}"] = ex;
                }
            }
        }

        // ── 统计整体误差 ──
        double maxErr = allErrors.Count > 0 ? allErrors.Max() : 0.0;
        double minErr = allErrors.Count > 0 ? allErrors.Min() : 0.0;
        double meanErr = allErrors.Count > 0 ? allErrors.Average() : 0.0;
        double rmsErr =
            allErrors.Count > 0 ? Math.Sqrt(allErrors.Select(e => e * e).Average()) : 0.0;

        // ── 序列化 JSON ──
        JsonSerializerOptions jsonOpts = new() { WriteIndented = false };
        Dictionary<string, object> intrinsicsDict = intrinsicResults.ToDictionary(
            kv => kv.Key.ToString("N"),
            kv => (object)
                new
                {
                    role = cameraGroups[kv.Key].Role.ToString(),
                    imageSize = new[] { cameraGroups[kv.Key].ImageSize.Width, cameraGroups[kv.Key].ImageSize.Height },
                    fx = kv.Value.CameraMatrix[0, 0],
                    fy = kv.Value.CameraMatrix[1, 1],
                    cx = kv.Value.CameraMatrix[0, 2],
                    cy = kv.Value.CameraMatrix[1, 2],
                    distortion = kv.Value.DistCoeffs,
                    rms = kv.Value.Rms,
                    frameCount = kv.Value.PerFrameErrors.Count,
                }
        );
        Dictionary<string, object> extrinsicsDict = extrinsicResults.ToDictionary(
            kv => kv.Key,
            kv => (object)
                new
                {
                    rotation = kv.Value.Rotation,
                    translation = kv.Value.Translation,
                    rms = kv.Value.Rms,
                }
        );

        return new ComputationOutput
        {
            CameraIntrinsicsJson = JsonSerializer.Serialize(intrinsicsDict, jsonOpts),
            CameraExtrinsicsJson = JsonSerializer.Serialize(extrinsicsDict, jsonOpts),
            StructuredLightCalibrationJson = null,
            OverallReprojectionError = meanErr,
            MaxError = maxErr,
            MinError = minErr,
            MeanError = meanErr,
            RmsError = rmsErr,
            PerImageReprojErrors = perImageErrors,
        };
    }

    /// <summary>
    /// 根据标定板配置生成模板 3D 物理坐标（Z=0 平面）。AprilTag 返回 null 表示不支持。
    /// </summary>
    private static Point3f[]? BuildObjectPointTemplate(CalibrationProject project)
    {
        if (project.BoardType == CalibrationBoardType.AprilTag)
        {
            return null;
        }

        int rows = project.BoardRows;
        int cols = project.BoardCols;
        double unitMm = project.BoardType switch
        {
            CalibrationBoardType.Chessboard => project.SquareSizeMm ?? 1.0,
            CalibrationBoardType.CircleGrid => project.CircleSpacingMm ?? 1.0,
            _ => 1.0,
        };

        Point3f[] pts = new Point3f[rows * cols];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                pts[r * cols + c] = new Point3f((float)(c * unitMm), (float)(r * unitMm), 0f);
            }
        }
        return pts;
    }

    /// <summary>
    /// 从 BLOB 加载所有图像，按 cameraDeviceId 分组、检测角点。
    /// </summary>
    private async Task<Dictionary<Guid, CameraSamples>> LoadCameraSamplesAsync(
        CalibrationProject project,
        Size patternSize,
        Point3f[] objectPointTemplate
    )
    {
        Dictionary<Guid, CameraSamples> groups = new();

        foreach (CalibrationCaptureFrame frame in project.Frames.Where(f => f.IsAccepted))
        {
            foreach (CalibrationCaptureImage image in frame.Images)
            {
                byte[]? bytes = await _imageBlobContainer.GetAllBytesOrNullAsync(image.BlobName);
                if (bytes is null || bytes.Length == 0)
                {
                    _logger.LogWarning(
                        "[CalibrationComputeJob] BLOB {BlobName} 不存在或为空，跳过",
                        image.BlobName
                    );
                    continue;
                }

                using Mat decoded = Cv2.ImDecode(bytes, ImreadModes.Grayscale);
                if (decoded.Empty())
                {
                    _logger.LogWarning(
                        "[CalibrationComputeJob] 图像 {BlobName} 解码失败",
                        image.BlobName
                    );
                    continue;
                }

                Size imgSize = new(decoded.Width, decoded.Height);
                Point2f[]? corners = DetectCorners(decoded, project.BoardType, patternSize);

                if (!groups.TryGetValue(image.CameraDeviceId, out CameraSamples? samples))
                {
                    samples = new CameraSamples
                    {
                        CameraId = image.CameraDeviceId,
                        Role = image.CameraRole,
                        ImageSize = imgSize,
                    };
                    groups[image.CameraDeviceId] = samples;
                }

                samples.Frames.Add(
                    new FrameSample
                    {
                        ImageEntityId = image.Id,
                        FrameIndex = frame.FrameIndex,
                        Corners = corners,
                        ObjectPoints = corners is null ? null : objectPointTemplate,
                    }
                );
            }
        }

        return groups;
    }

    /// <summary>
    /// 角点检测：棋盘格用 FindChessboardCornersSB（OpenCV 4.x 推荐），圆形点阵用 FindCirclesGrid。
    /// </summary>
    private Point2f[]? DetectCorners(Mat gray, CalibrationBoardType boardType, Size patternSize)
    {
        try
        {
            if (boardType == CalibrationBoardType.Chessboard)
            {
                bool ok = Cv2.FindChessboardCornersSB(
                    gray,
                    patternSize,
                    out Point2f[] corners,
                    ChessboardFlags.None
                );
                return ok ? corners : null;
            }
            if (boardType == CalibrationBoardType.CircleGrid)
            {
                bool ok = Cv2.FindCirclesGrid(
                    gray,
                    patternSize,
                    out Point2f[] centers,
                    FindCirclesGridFlags.SymmetricGrid
                );
                return ok ? centers : null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[CalibrationComputeJob] 角点检测异常");
        }
        return null;
    }

    /// <summary>
    /// 单相机内参标定：Cv2.CalibrateCamera。返回内参矩阵、畸变系数、整体 RMS、每帧重投影误差。
    /// </summary>
    private IntrinsicResult CalibrateSingleCamera(CameraSamples samples, Guid cameraId)
    {
        List<FrameSample> validFrames = samples.Frames.Where(f => f.Corners is not null).ToList();

        List<Mat> objMats = validFrames.Select(f => ToPoint3fMat(f.ObjectPoints!)).ToList();
        List<Mat> imgMats = validFrames.Select(f => ToPoint2fMat(f.Corners!)).ToList();

        using Mat cameraMatrix = new(3, 3, MatType.CV_64FC1, new Scalar(0));
        using Mat distCoeffs = new(5, 1, MatType.CV_64FC1, new Scalar(0));

        try
        {
            double rms = Cv2.CalibrateCamera(
                objMats,
                imgMats,
                samples.ImageSize,
                cameraMatrix,
                distCoeffs,
                out Mat[] rvecs,
                out Mat[] tvecs,
                CalibrationFlags.None
            );

            // 计算每帧重投影误差
            List<double> perFrameErrors = new();
            for (int i = 0; i < validFrames.Count; i++)
            {
                try
                {
                    using Mat projected = new();
                    Cv2.ProjectPoints(
                        objMats[i],
                        rvecs[i],
                        tvecs[i],
                        cameraMatrix,
                        distCoeffs,
                        projected
                    );

                    Point2f[] observed = validFrames[i].Corners!;
                    int n = Math.Min((int)projected.Total(), observed.Length);
                    double sumSq = 0;
                    for (int k = 0; k < n; k++)
                    {
                        Vec2f p = projected.At<Vec2f>(k, 0);
                        double dx = p.Item0 - observed[k].X;
                        double dy = p.Item1 - observed[k].Y;
                        sumSq += dx * dx + dy * dy;
                    }
                    perFrameErrors.Add(n > 0 ? Math.Sqrt(sumSq / n) : 0.0);
                }
                catch
                {
                    perFrameErrors.Add(0.0);
                }
            }

            foreach (Mat rv in rvecs) rv.Dispose();
            foreach (Mat tv in tvecs) tv.Dispose();

            _logger.LogInformation(
                "[CalibrationComputeJob] 相机 {CameraId} 内参标定完成，RMS={Rms:F4}px，{N} 帧",
                cameraId,
                rms,
                validFrames.Count
            );

            return new IntrinsicResult
            {
                CameraMatrix = MatToArray3x3(cameraMatrix),
                DistCoeffs = MatToArray1D(distCoeffs, 5),
                Rms = rms,
                PerFrameErrors = perFrameErrors,
            };
        }
        finally
        {
            foreach (Mat m in objMats) m.Dispose();
            foreach (Mat m in imgMats) m.Dispose();
        }
    }

    /// <summary>将 Point3f 数组打包为 N×1 CV_32FC3 Mat（OpenCvSharp 标定 API 要求格式）。</summary>
    private static Mat ToPoint3fMat(Point3f[] pts)
    {
        Mat m = new(pts.Length, 1, MatType.CV_32FC3);
        for (int i = 0; i < pts.Length; i++)
        {
            m.Set(i, 0, new Vec3f(pts[i].X, pts[i].Y, pts[i].Z));
        }
        return m;
    }

    /// <summary>将 Point2f 数组打包为 N×1 CV_32FC2 Mat。</summary>
    private static Mat ToPoint2fMat(Point2f[] pts)
    {
        Mat m = new(pts.Length, 1, MatType.CV_32FC2);
        for (int i = 0; i < pts.Length; i++)
        {
            m.Set(i, 0, new Vec2f(pts[i].X, pts[i].Y));
        }
        return m;
    }

    private static double[,] MatToArray3x3(Mat m)
    {
        double[,] arr = new double[3, 3];
        for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++)
            arr[i, j] = m.At<double>(i, j);
        return arr;
    }

    private static double[] MatToArray1D(Mat m, int n)
    {
        double[] arr = new double[n];
        for (int i = 0; i < n; i++) arr[i] = m.At<double>(i, 0);
        return arr;
    }

    /// <summary>
    /// 双目外参标定：Cv2.StereoCalibrate（FixIntrinsic 模式，仅求解 R/t）。
    /// 仅使用两台相机都成功检测到角点的共同帧。
    /// </summary>
    private ExtrinsicResult? CalibrateStereoPair(
        CameraSamples master,
        CameraSamples slave,
        IntrinsicResult masterIntrin,
        IntrinsicResult slaveIntrin
    )
    {
        Dictionary<int, FrameSample> slaveByIdx = slave.Frames.ToDictionary(f => f.FrameIndex);
        List<(FrameSample m, FrameSample s)> common = new();
        foreach (FrameSample mf in master.Frames)
        {
            if (mf.Corners is null)
            {
                continue;
            }
            if (
                slaveByIdx.TryGetValue(mf.FrameIndex, out FrameSample? sf)
                && sf.Corners is not null
            )
            {
                common.Add((mf, sf));
            }
        }

        if (common.Count < 3)
        {
            _logger.LogWarning(
                "[CalibrationComputeJob] 相机 {Slave} 与 Master 共同有效帧仅 {N} 张，跳过外参",
                slave.CameraId,
                common.Count
            );
            return null;
        }

        List<Mat> objMats = common.Select(p => ToPoint3fMat(p.m.ObjectPoints!)).ToList();
        List<Mat> mMats = common.Select(p => ToPoint2fMat(p.m.Corners!)).ToList();
        List<Mat> sMats = common.Select(p => ToPoint2fMat(p.s.Corners!)).ToList();

        using Mat camM = ArrayToMat3x3(masterIntrin.CameraMatrix);
        using Mat distM = ArrayToMat1D(masterIntrin.DistCoeffs);
        using Mat camS = ArrayToMat3x3(slaveIntrin.CameraMatrix);
        using Mat distS = ArrayToMat1D(slaveIntrin.DistCoeffs);
        using Mat r = new();
        using Mat t = new();
        using Mat e = new();
        using Mat f = new();

        try
        {
            double rms = Cv2.StereoCalibrate(
                objMats.Select(x => (InputArray)x),
                mMats.Select(x => (InputArray)x),
                sMats.Select(x => (InputArray)x),
                camM,
                distM,
                camS,
                distS,
                master.ImageSize,
                r,
                t,
                e,
                f,
                CalibrationFlags.FixIntrinsic
            );

            double[][] rotation = new double[3][];
            for (int i = 0; i < 3; i++)
            {
                rotation[i] = new[]
                {
                    r.At<double>(i, 0),
                    r.At<double>(i, 1),
                    r.At<double>(i, 2),
                };
            }
            double[] translation =
            {
                t.At<double>(0, 0),
                t.At<double>(1, 0),
                t.At<double>(2, 0),
            };

            _logger.LogInformation(
                "[CalibrationComputeJob] 相机 {Slave} → Master 外参 RMS={Rms:F4}px",
                slave.CameraId,
                rms
            );

            return new ExtrinsicResult
            {
                Rotation = rotation,
                Translation = translation,
                Rms = rms,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CalibrationComputeJob] 双目外参标定异常");
            return null;
        }
        finally
        {
            foreach (Mat m in objMats) m.Dispose();
            foreach (Mat m in mMats) m.Dispose();
            foreach (Mat m in sMats) m.Dispose();
        }
    }

    private static Mat ArrayToMat3x3(double[,] arr)
    {
        Mat m = new(3, 3, MatType.CV_64FC1);
        for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++)
            m.Set(i, j, arr[i, j]);
        return m;
    }

    private static Mat ArrayToMat1D(double[] arr)
    {
        Mat m = new(arr.Length, 1, MatType.CV_64FC1);
        for (int i = 0; i < arr.Length; i++) m.Set(i, 0, arr[i]);
        return m;
    }

    private static bool IsMasterRole(CameraRole role) =>
        role == CameraRole.Master || role == CameraRole.TopMaster;

    /// <summary>构造无法计算（如 AprilTag）时的占位输出。</summary>
    private static ComputationOutput BuildPlaceholderOutput(CalibrationProject project)
    {
        Dictionary<string, object> placeholder = new()
        {
            ["note"] = "无法自动计算（标定板类型暂不支持或无有效角点），返回占位结果",
            ["boardType"] = project.BoardType.ToString(),
            ["frameCount"] = project.Frames.Count(f => f.IsAccepted),
            ["targetCaptureCount"] = project.TargetCaptureCount,
        };
        JsonSerializerOptions jsonOpts = new() { WriteIndented = false };
        return new ComputationOutput
        {
            CameraIntrinsicsJson = JsonSerializer.Serialize(placeholder, jsonOpts),
            CameraExtrinsicsJson = "{}",
            StructuredLightCalibrationJson = null,
            OverallReprojectionError = 0.0,
            MaxError = 0.0,
            MinError = 0.0,
            MeanError = 0.0,
            RmsError = 0.0,
            PerImageReprojErrors = new Dictionary<Guid, double>(),
        };
    }

    // ── 内部 DTO ────────────────────────────────────────────────

    private sealed class ComputationOutput
    {
        public string CameraIntrinsicsJson { get; set; } = "{}";
        public string CameraExtrinsicsJson { get; set; } = "{}";
        public string? StructuredLightCalibrationJson { get; set; }
        public double OverallReprojectionError { get; set; }
        public double MaxError { get; set; }
        public double MinError { get; set; }
        public double MeanError { get; set; }
        public double RmsError { get; set; }
        public Dictionary<Guid, double> PerImageReprojErrors { get; set; } =
            new Dictionary<Guid, double>();
    }

    private sealed class CameraSamples
    {
        public Guid CameraId { get; set; }
        public CameraRole Role { get; set; }
        public Size ImageSize { get; set; }
        public List<FrameSample> Frames { get; set; } = new();
        public int ValidFrameCount => Frames.Count(f => f.Corners is not null);
    }

    private sealed class FrameSample
    {
        public Guid ImageEntityId { get; set; }
        public int FrameIndex { get; set; }
        public Point2f[]? Corners { get; set; }
        public Point3f[]? ObjectPoints { get; set; }
    }

    private sealed class IntrinsicResult
    {
        public double[,] CameraMatrix { get; set; } = new double[3, 3];
        public double[] DistCoeffs { get; set; } = Array.Empty<double>();
        public double Rms { get; set; }
        public List<double> PerFrameErrors { get; set; } = new();
    }

    private sealed class ExtrinsicResult
    {
        public double[][] Rotation { get; set; } = Array.Empty<double[]>();
        public double[] Translation { get; set; } = Array.Empty<double>();
        public double Rms { get; set; }
    }
}
