using System.Text.Json;
using AuroraStruct3D.Calibration.Dtos;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Volo.Abp.DependencyInjection;

namespace AuroraStruct3D.Calibration;

public class CalibBoardDetector : ITransientDependency
{
    private readonly ILogger<CalibBoardDetector> _logger;

    public CalibBoardDetector(ILogger<CalibBoardDetector> logger)
    {
        _logger = logger;
    }

    // ─── 亚像素角点精化参数（Cv2.CornerSubPix，多处共用）───
    /// <summary>亚像素角点精化搜索窗口半径</summary>
    private static readonly Size SubPixWinSize = new(11, 11);

    /// <summary>亚像素角点精化死区（-1,-1 表示不使用死区）</summary>
    private static readonly Size SubPixZeroZone = new(-1, -1);

    /// <summary>亚像素角点精化收敛准则：最多迭代 30 次或精度达 0.001 像素即停止</summary>
    private static readonly TermCriteria SubPixCriteria = new(
        CriteriaTypes.Eps | CriteriaTypes.MaxIter,
        30,
        0.001
    );

    /// <summary>投影棋盘格 ROI 提取二值化阈值：灰度 &gt; 80 视为投影亮区</summary>
    private const int ProjectorMaskThreshold = 80;

    /// <summary>投影棋盘格 ROI 形态学开/闭运算的矩形结构元尺寸（像素）</summary>
    private static readonly Size ProjectorMaskMorphKernel = new(31, 31);

    /// <summary>圆点 BLOB 面积自适应缩放的参考分辨率（标定相机全画幅 2448×2048）</summary>
    private const double ReferenceImagePixels = 2448.0 * 2048.0;

    public (bool isValid, int cornerCount) DetectBoardFeaturePoints(
        byte[] imageBytes,
        CalibProject project,
        bool isProjectedBoard,
        int rotationAngle = 0
    )
    {
        CalibrationBoardType detectionBoardType = GetDetectionBoardType(project, isProjectedBoard);
        if (detectionBoardType == CalibrationBoardType.Chessboard)
        {
            Size chess = GetBoardPatternSize(project, isProjectedBoard);

            if (isProjectedBoard && project.BoardType != CalibrationBoardType.Chessboard)
            {
                using Mat projGray = CalibImageUtils.LoadGrayMatWithRotation(
                    imageBytes,
                    rotationAngle
                );
                if (projGray.Empty())
                    return (false, 0);

                using Mat projMask = new();
                Cv2.Threshold(projGray, projMask, ProjectorMaskThreshold, 255, ThresholdTypes.Binary);
                using Mat maskClosed = new();
                Cv2.MorphologyEx(
                    projMask,
                    maskClosed,
                    MorphTypes.Close,
                    Cv2.GetStructuringElement(MorphShapes.Rect, ProjectorMaskMorphKernel)
                );
                using Mat maskOpen = new();
                Cv2.MorphologyEx(
                    maskClosed,
                    maskOpen,
                    MorphTypes.Open,
                    Cv2.GetStructuringElement(MorphShapes.Rect, ProjectorMaskMorphKernel)
                );

                using Mat maskedGray = new();
                projGray.CopyTo(maskedGray, maskOpen);

                _logger.LogInformation(
                    "投影棋盘格ROI提取: 原图={W}x{H}",
                    projGray.Cols,
                    projGray.Rows
                );

                Point2f[]? corners1 = FindCornersSubpixGray(maskedGray, chess);
                if (corners1 != null)
                {
                    _logger.LogInformation(
                        "投影棋盘格检测成功[原图+SB]: Pattern={Cols}x{Rows}, 角点数={Count}",
                        chess.Width,
                        chess.Height,
                        corners1.Length
                    );
                    return (true, corners1.Length);
                }

                using CLAHE clahe = Cv2.CreateCLAHE(clipLimit: 2.0, tileGridSize: new Size(8, 8));
                using Mat claheMat = new();
                clahe.Apply(maskedGray, claheMat);
                Point2f[]? corners2 = FindCornersSubpixGray(claheMat, chess);
                if (corners2 != null)
                {
                    _logger.LogInformation(
                        "投影棋盘格检测成功[CLAHE+SB]: Pattern={Cols}x{Rows}, 角点数={Count}",
                        chess.Width,
                        chess.Height,
                        corners2.Length
                    );
                    return (true, corners2.Length);
                }

                using Mat blurred = new();
                Cv2.GaussianBlur(maskedGray, blurred, new Size(3, 3), 0);
                Point2f[]? corners3 = FindCornersSubpixGray(blurred, chess);
                if (corners3 != null)
                {
                    _logger.LogInformation(
                        "投影棋盘格检测成功[高斯模糊+SB]: Pattern={Cols}x{Rows}, 角点数={Count}",
                        chess.Width,
                        chess.Height,
                        corners3.Length
                    );
                    return (true, corners3.Length);
                }

                try
                {
                    if (
                        Cv2.FindChessboardCornersSB(
                            projGray,
                            chess,
                            out Point2f[] sbCorners,
                            ChessboardFlags.AdaptiveThresh
                                | ChessboardFlags.NormalizeImage
                                | ChessboardFlags.FilterQuads
                        )
                    )
                    {
                        using Mat sbGray = new();
                        if (projGray.Channels() > 1)
                            Cv2.CvtColor(projGray, sbGray, ColorConversionCodes.BGR2GRAY);
                        else
                            projGray.CopyTo(sbGray);

                        Cv2.CornerSubPix(
                            sbGray,
                            sbCorners,
                            SubPixWinSize,
                            SubPixZeroZone,
                            SubPixCriteria
                        );

                        _logger.LogInformation(
                            "投影棋盘格检测成功[SB原图]: Pattern={Cols}x{Rows}, 角点数={Count}",
                            chess.Width,
                            chess.Height,
                            sbCorners.Length
                        );
                        return (true, sbCorners.Length);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "投影棋盘格 FindChessboardCornersSB[SB原图] 检测失败，已跳过");
                }

                _logger.LogWarning(
                    "投影棋盘格检测失败(所有策略均失败): Pattern={Cols}x{Rows}, Image={W}x{H}",
                    chess.Width,
                    chess.Height,
                    projGray.Cols,
                    projGray.Rows
                );
                return (false, 0);
            }

            return DetectChessboardCorners(imageBytes, chess.Width, chess.Height, rotationAngle);
        }

        try
        {
            Size patternSize = GetBoardPatternSize(project, isProjectedBoard);
            using Mat gray = CalibImageUtils.LoadGrayMatWithRotation(imageBytes, rotationAngle);
            if (gray.Empty())
                return (false, 0);

            Point2f[]? points = FindBoardPointsSubpixGray(
                gray,
                patternSize,
                detectionBoardType,
                project
            );
            if (points == null)
            {
                _logger.LogWarning(
                    "圆点板检测失败: BoardType={BoardType}, Pattern={Cols}x{Rows}, Projected={Projected}, Image={W}x{H}",
                    detectionBoardType,
                    patternSize.Width,
                    patternSize.Height,
                    isProjectedBoard,
                    gray.Cols,
                    gray.Rows
                );
                return (false, 0);
            }

            int expectedCount = patternSize.Width * patternSize.Height;
            if (project.BoardType == CalibrationBoardType.MarkedSymmetricCircleGrid)
            {
                expectedCount--;
            }
            if (points.Length != expectedCount)
            {
                _logger.LogWarning(
                    "圆点板点数不完整: BoardType={BoardType}, Pattern={Cols}x{Rows}, Expected={Expected}, Actual={Actual}",
                    detectionBoardType,
                    patternSize.Width,
                    patternSize.Height,
                    expectedCount,
                    points.Length
                );
                return (false, points.Length);
            }

            return (true, points.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "圆点板检测异常");
            return (false, 0);
        }
    }

    private (bool isValid, int cornerCount) DetectChessboardCorners(
        byte[] imageBytes,
        int cols,
        int rows,
        int rotationAngle = 0
    )
    {
        try
        {
            Size patternSize = new(cols, rows);

            using Mat mat = Cv2.ImDecode(imageBytes, ImreadModes.Color);
            if (!mat.Empty())
            {
                using Mat rotatedMat = CalibImageUtils.ApplyRotation(mat, rotationAngle);
                if (!HasChessboardFeatureQuickCheck(rotatedMat, patternSize))
                {
                    _logger.LogDebug(
                        "棋盘格快速预检未通过 (OpenCV解码路径): {Cols}x{Rows}，图像大小 {W}x{H}",
                        cols,
                        rows,
                        rotatedMat.Cols,
                        rotatedMat.Rows
                    );
                    return (false, 0);
                }

                _logger.LogDebug(
                    "棋盘格检测: OpenCV 解码成功 {W}x{H}，目标格式 {Cols}x{Rows}",
                    rotatedMat.Cols,
                    rotatedMat.Rows,
                    cols,
                    rows
                );
                Point2f[]? corners = FindCornersSubpix(rotatedMat, patternSize);
                if (corners != null)
                {
                    _logger.LogInformation(
                        "棋盘格检测成功 (OpenCV解码路径): 找到 {Count} 个角点",
                        corners.Length
                    );
                    return (true, corners.Length);
                }
                _logger.LogWarning(
                    "棋盘格检测失败 (OpenCV解码): {Cols}x{Rows} 角点未找到，图像大小 {W}x{H}",
                    cols,
                    rows,
                    rotatedMat.Cols,
                    rotatedMat.Rows
                );
                return (false, 0);
            }

            _logger.LogDebug("棋盘格检测: OpenCV 解码失败，尝试 SkiaSharp fallback");
            using Mat skMat = CalibImageUtils.SkiaBytesToBgrMat(imageBytes);
            if (skMat.Empty())
            {
                _logger.LogWarning("棋盘格检测: SkiaSharp 解码也失败，图像不可用");
                return (false, 0);
            }

            using Mat rotatedSkMat = CalibImageUtils.ApplyRotation(skMat, rotationAngle);
            if (!HasChessboardFeatureQuickCheck(rotatedSkMat, patternSize))
            {
                _logger.LogDebug(
                    "棋盘格快速预检未通过 (SkiaSharp解码路径): {Cols}x{Rows}，图像大小 {W}x{H}",
                    cols,
                    rows,
                    rotatedSkMat.Cols,
                    rotatedSkMat.Rows
                );
                return (false, 0);
            }

            _logger.LogDebug(
                "棋盘格检测: SkiaSharp 解码成功 {W}x{H}，目标格式 {Cols}x{Rows}",
                rotatedSkMat.Cols,
                rotatedSkMat.Rows,
                cols,
                rows
            );
            Point2f[]? skCorners = FindCornersSubpix(rotatedSkMat, patternSize);
            if (skCorners != null)
            {
                _logger.LogInformation(
                    "棋盘格检测成功 (SkiaSharp解码路径): 找到 {Count} 个角点",
                    skCorners.Length
                );
                return (true, skCorners.Length);
            }
            _logger.LogWarning(
                "棋盘格检测失败 (SkiaSharp解码): {Cols}x{Rows} 角点未找到，图像大小 {W}x{H}",
                cols,
                rows,
                rotatedSkMat.Cols,
                rotatedSkMat.Rows
            );
            return (false, 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "棋盘格角点检测异常 ({Cols}x{Rows})", cols, rows);
            return (false, 0);
        }
    }

    private static bool HasChessboardFeatureQuickCheck(Mat mat, Size patternSize)
    {
        using Mat gray = new();
        if (mat.Channels() > 1)
        {
            Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY);
        }
        else
        {
            mat.CopyTo(gray);
        }

        const int maxSide = 640;
        using Mat preview = new();
        int longSide = Math.Max(gray.Cols, gray.Rows);
        if (longSide > maxSide)
        {
            double scale = maxSide / (double)longSide;
            int w = Math.Max(1, (int)Math.Round(gray.Cols * scale));
            int h = Math.Max(1, (int)Math.Round(gray.Rows * scale));
            Cv2.Resize(gray, preview, new Size(w, h));
        }
        else
        {
            gray.CopyTo(preview);
        }

        return Cv2.FindChessboardCorners(
            preview,
            patternSize,
            out _,
            ChessboardFlags.AdaptiveThresh | ChessboardFlags.NormalizeImage
        );
    }

    private static Point2f[]? FindCornersSubpix(Mat mat, Size patternSize)
    {
        using Mat gray = new();
        if (mat.Channels() > 1)
        {
            Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY);
        }
        else
        {
            mat.CopyTo(gray);
        }

        return FindCornersSubpixGray(gray, patternSize);
    }

    private static Point2f[]? FindCornersSubpixGray(Mat grayFull, Size patternSize)
    {
        int halfWidth = grayFull.Cols / 2;
        int halfHeight = grayFull.Rows / 2;

        Point2f[]? TryFind(bool useHalfSize)
        {
            using Mat gray = useHalfSize ? new() : grayFull;
            if (useHalfSize)
            {
                Cv2.Resize(grayFull, gray, new Size(halfWidth, halfHeight));
            }

            bool found = Cv2.FindChessboardCorners(
                gray,
                patternSize,
                out Point2f[] corners,
                ChessboardFlags.AdaptiveThresh | ChessboardFlags.NormalizeImage
            );
            if (found)
            {
                Cv2.CornerSubPix(
                    gray,
                    corners,
                    SubPixWinSize,
                    SubPixZeroZone,
                    SubPixCriteria
                );

                if (useHalfSize)
                {
                    for (int i = 0; i < corners.Length; i++)
                    {
                        corners[i].X *= 2;
                        corners[i].Y *= 2;
                    }
                }

                return corners;
            }

            found = Cv2.FindChessboardCorners(
                gray,
                patternSize,
                out corners,
                ChessboardFlags.AdaptiveThresh
                    | ChessboardFlags.NormalizeImage
                    | ChessboardFlags.FilterQuads
            );
            if (found)
            {
                Cv2.CornerSubPix(
                    gray,
                    corners,
                    SubPixWinSize,
                    SubPixZeroZone,
                    SubPixCriteria
                );

                if (useHalfSize)
                {
                    for (int i = 0; i < corners.Length; i++)
                    {
                        corners[i].X *= 2;
                        corners[i].Y *= 2;
                    }
                }

                return corners;
            }

            try
            {
                bool sbFound = Cv2.FindChessboardCornersSB(
                    gray,
                    patternSize,
                    out corners,
                    ChessboardFlags.AdaptiveThresh
                        | ChessboardFlags.NormalizeImage
                        | ChessboardFlags.FilterQuads
                );
                if (sbFound)
                {
                    Cv2.CornerSubPix(
                        gray,
                        corners,
                        SubPixWinSize,
                        SubPixZeroZone,
                        SubPixCriteria
                    );

                    if (useHalfSize)
                    {
                        for (int i = 0; i < corners.Length; i++)
                        {
                            corners[i].X *= 2;
                            corners[i].Y *= 2;
                        }
                    }

                    return corners;
                }
            }
            catch (Exception)
            {
                // FindChessboardCornersSB 在特定图像或 OpenCV 版本上可能抛出；此处为静态上下文，回退为 null
            }

            return null;
        }

        Point2f[]? halfResult = TryFind(true);
        if (halfResult != null)
            return halfResult;

        return TryFind(false);
    }

    public Point2f[]? FindBoardPointsSubpixGray(
        Mat grayFull,
        Size patternSize,
        CalibrationBoardType boardType,
        CalibProject? project = null
    )
    {
        if (boardType == CalibrationBoardType.Chessboard)
            return FindCornersSubpixGray(grayFull, patternSize);

        CircleBlobDetectorConfigDto detectorConfig = LoadCircleDetectorConfig(project);
        return FindCircleGridPoints(grayFull, patternSize, boardType, detectorConfig, project);
    }

    private static CircleBlobDetectorConfigDto LoadCircleDetectorConfig(CalibProject? project)
    {
        if (project == null || string.IsNullOrWhiteSpace(project.CircleDetectorConfigJson))
            return CircleBlobDetectorConfigDto.CreateDefault();

        try
        {
            return JsonSerializer.Deserialize<CircleBlobDetectorConfigDto>(
                    project.CircleDetectorConfigJson
                ) ?? CircleBlobDetectorConfigDto.CreateDefault();
        }
        catch
        {
            return CircleBlobDetectorConfigDto.CreateDefault();
        }
    }

    private Point2f[]? FindCircleGridPoints(
        Mat gray,
        Size patternSize,
        CalibrationBoardType boardType,
        CircleBlobDetectorConfigDto detectorConfig,
        CalibProject? project
    )
    {
        bool hasMarkerHole = boardType == CalibrationBoardType.MarkedSymmetricCircleGrid;
        int expectedCount = patternSize.Width * patternSize.Height - (hasMarkerHole ? 1 : 0);

        int markerRow = project?.MarkerRow ?? ((patternSize.Height - 1) / 2);
        int markerCol = project?.MarkerCol ?? ((patternSize.Width - 1) / 2);

        Point2f[]? TryDetect(Mat inputGray, string preprocessingName)
        {
            using Feature2D detector = CreateCircleBlobDetector(
                detectorConfig,
                new Size(inputGray.Cols, inputGray.Rows)
            );

            if (hasMarkerHole)
            {
                KeyPoint[] markedKeypoints = detector.Detect(inputGray);
                if (markedKeypoints.Length == 0)
                    return null;

                Point2f[] markedCenters = markedKeypoints
                    .Select(x => new Point2f(x.Pt.X, x.Pt.Y))
                    .ToArray();

                bool markedOk = TryBuildOrderedCircleGrid(
                    markedCenters,
                    patternSize.Width,
                    patternSize.Height,
                    hasMarkerHole: true,
                    markerRow,
                    markerCol,
                    out Point2f[]? orderedMarked,
                    out string? markedFailureReason
                );

                if (markedOk && orderedMarked != null && orderedMarked.Length == expectedCount)
                {
                    _logger.LogInformation(
                        "圆点板检测成功[{Preprocessing}]: BoardType={BoardType}, Pattern={Cols}x{Rows}, 点数={Count}",
                        preprocessingName,
                        boardType,
                        patternSize.Width,
                        patternSize.Height,
                        orderedMarked.Length
                    );
                    return orderedMarked;
                }

                return null;
            }
            else
            {
                FindCirclesGridFlags flags =
                    boardType == CalibrationBoardType.AsymmetricCircleGrid
                        ? FindCirclesGridFlags.AsymmetricGrid | FindCirclesGridFlags.Clustering
                        : FindCirclesGridFlags.SymmetricGrid | FindCirclesGridFlags.Clustering;

                if (
                    Cv2.FindCirclesGrid(
                        inputGray,
                        patternSize,
                        out Point2f[] centers,
                        flags,
                        detector
                    )
                )
                {
                    _logger.LogInformation(
                        "圆点板检测成功[{Preprocessing}]: BoardType={BoardType}, Pattern={Cols}x{Rows}, 点数={Count}, Method=OpenCV",
                        preprocessingName,
                        boardType,
                        patternSize.Width,
                        patternSize.Height,
                        centers.Length
                    );
                    return centers;
                }

                KeyPoint[] keypoints = detector.Detect(inputGray);
                if (keypoints.Length == 0)
                    return null;

                Point2f[] blobCenters = keypoints
                    .Select(x => new Point2f(x.Pt.X, x.Pt.Y))
                    .ToArray();

                bool orderedOk = TryBuildOrderedCircleGrid(
                    blobCenters,
                    patternSize.Width,
                    patternSize.Height,
                    hasMarkerHole: false,
                    markerRow: 0,
                    markerCol: 0,
                    out Point2f[]? ordered,
                    out string? failureReason
                );

                if (orderedOk && ordered != null && ordered.Length == expectedCount)
                {
                    _logger.LogInformation(
                        "圆点板检测成功[{Preprocessing}]: BoardType={BoardType}, Pattern={Cols}x{Rows}, 点数={Count}, Method=CustomSort",
                        preprocessingName,
                        boardType,
                        patternSize.Width,
                        patternSize.Height,
                        ordered.Length
                    );
                    return ordered;
                }

                return null;
            }
        }

        using Mat bgSuppressed = new();
        Cv2.Threshold(gray, bgSuppressed, 30, 255, ThresholdTypes.Binary);

        using Mat maskedGray = new();
        gray.CopyTo(maskedGray, bgSuppressed);

        Point2f[]? result;

        result = TryDetect(maskedGray, "背景抑制");
        if (result != null)
            return result;

        result = TryDetect(gray, "原图");
        if (result != null)
            return result;

        using Mat claheMat = new();
        using CLAHE clahe = Cv2.CreateCLAHE(clipLimit: 2.0, tileGridSize: new Size(8, 8));
        clahe.Apply(gray, claheMat);
        result = TryDetect(claheMat, "CLAHE");
        if (result != null)
            return result;

        using Mat blurredMat = new();
        Cv2.GaussianBlur(gray, blurredMat, new Size(3, 3), 0);
        result = TryDetect(blurredMat, "高斯模糊");
        if (result != null)
            return result;

        using Mat eqMat = new();
        Cv2.EqualizeHist(gray, eqMat);
        result = TryDetect(eqMat, "直方图均衡化");
        if (result != null)
            return result;

        _logger.LogWarning(
            "圆点板检测失败(所有策略均失败): BoardType={BoardType}, Pattern={Cols}x{Rows}, Image={W}x{H}",
            boardType,
            patternSize.Width,
            patternSize.Height,
            gray.Cols,
            gray.Rows
        );
        return null;
    }

    private static Feature2D CreateCircleBlobDetector(
        CircleBlobDetectorConfigDto cfg,
        Size? imageSize = null
    )
    {
        float minArea = (float)cfg.MinArea;
        float maxArea = (float)cfg.MaxArea;

        if (imageSize.HasValue && imageSize.Value.Width > 0 && imageSize.Value.Height > 0)
        {
            double imagePixels = imageSize.Value.Width * imageSize.Value.Height;
            double scaleFactor = imagePixels / ReferenceImagePixels;

            if (scaleFactor > 0)
            {
                minArea = (float)(cfg.MinArea * scaleFactor);
                maxArea = (float)(cfg.MaxArea * scaleFactor);
            }
        }

        var p = new SimpleBlobDetector.Params
        {
            MinThreshold = (float)cfg.MinThreshold,
            MaxThreshold = (float)cfg.MaxThreshold,
            FilterByArea = true,
            MinArea = minArea,
            MaxArea = maxArea,
            FilterByCircularity = false,
            FilterByConvexity = true,
            MinConvexity = (float)cfg.MinConvexity,
            FilterByInertia = true,
            MinInertiaRatio = 0.1f,
            MaxInertiaRatio = 1.0f,
        };
        return SimpleBlobDetector.Create(p);
    }

    private static bool TryBuildOrderedCircleGrid(
        Point2f[] points,
        int cols,
        int rows,
        bool hasMarkerHole,
        int markerRow,
        int markerCol,
        out Point2f[]? orderedPoints,
        out string? failureReason
    )
    {
        orderedPoints = null;
        failureReason = null;
        int expected = rows * cols - (hasMarkerHole ? 1 : 0);
        if (cols < 2 || rows < 2)
        {
            failureReason = $"invalid-grid:{cols}x{rows}";
            return false;
        }
        if (points.Length < expected)
        {
            failureReason = $"insufficient-points:{points.Length}<{expected}";
            return false;
        }

        int n = points.Length;
        double[] nnDist = new double[n];
        for (int i = 0; i < n; i++)
        {
            double best = double.MaxValue;
            for (int j = 0; j < n; j++)
            {
                if (i == j)
                    continue;
                double dxi = points[i].X - points[j].X;
                double dyi = points[i].Y - points[j].Y;
                double d2 = (dxi * dxi) + (dyi * dyi);
                if (d2 < best)
                    best = d2;
            }
            nnDist[i] = Math.Sqrt(best);
        }
        double[] sortedNn = (double[])nnDist.Clone();
        Array.Sort(sortedNn);
        double medianNn = sortedNn[sortedNn.Length / 2];
        if (medianNn <= 1e-6)
        {
            failureReason = "degenerate-nn";
            return false;
        }

        List<Point2f> core = new(n);
        for (int i = 0; i < n; i++)
        {
            if (nnDist[i] <= 2.0 * medianNn)
                core.Add(points[i]);
        }
        if (core.Count < expected)
        {
            failureReason = $"core-insufficient:{core.Count}<{expected}";
            return false;
        }

        Point2f tl = core[0],
            tr = core[0],
            br = core[0],
            bl = core[0];
        double tlv = double.MaxValue,
            brv = double.MinValue,
            trv = double.MinValue,
            blv = double.MaxValue;
        foreach (Point2f p in core)
        {
            double sum = p.X + p.Y;
            double diff = p.X - p.Y;
            if (sum < tlv)
            {
                tlv = sum;
                tl = p;
            }
            if (sum > brv)
            {
                brv = sum;
                br = p;
            }
            if (diff > trv)
            {
                trv = diff;
                tr = p;
            }
            if (diff < blv)
            {
                blv = diff;
                bl = p;
            }
        }

        Point2f[] idealCorners =
        [
            new(0, 0),
            new(cols - 1, 0),
            new(cols - 1, rows - 1),
            new(0, rows - 1),
        ];
        Point2f[] imageCorners = [tl, tr, br, bl];
        double[] h;
        try
        {
            using Mat hMat = Cv2.GetPerspectiveTransform(idealCorners, imageCorners);
            h = new double[9];
            for (int i = 0; i < 9; i++)
                h[i] = hMat.At<double>(i / 3, i % 3);
        }
        catch (Exception)
        {
            failureReason = "homography-failed";
            return false;
        }

        int midC = cols / 2;
        int midR = rows / 2;
        Point2f pc = ApplyHomographyToPoint(h, midC, midR);
        Point2f pcx = ApplyHomographyToPoint(h, midC + 1, midR);
        Point2f pcy = ApplyHomographyToPoint(h, midC, midR + 1);
        double cell = Math.Min(PointDistanceForGrid(pc, pcx), PointDistanceForGrid(pc, pcy));
        if (cell <= 1e-3)
        {
            failureReason = "degenerate-cell";
            return false;
        }
        double half = 0.5 * cell;
        double matchThreshold2 = half * half;

        bool[] used = new bool[core.Count];
        Point2f[] ordered = new Point2f[expected];
        int outIdx = 0;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                if (hasMarkerHole && r == markerRow && c == markerCol)
                    continue;

                Point2f pred = ApplyHomographyToPoint(h, c, r);
                int bestJ = -1;
                double bestD2 = matchThreshold2;
                for (int j = 0; j < core.Count; j++)
                {
                    if (used[j])
                        continue;
                    double dxj = core[j].X - pred.X;
                    double dyj = core[j].Y - pred.Y;
                    double d2 = (dxj * dxj) + (dyj * dyj);
                    if (d2 <= bestD2)
                    {
                        bestD2 = d2;
                        bestJ = j;
                    }
                }
                if (bestJ < 0)
                {
                    failureReason = $"cell-unmatched:({r},{c})";
                    return false;
                }
                used[bestJ] = true;
                ordered[outIdx++] = core[bestJ];
            }
        }

        if (outIdx != expected)
        {
            failureReason = $"ordered-count-mismatch:{outIdx}!={expected}";
            return false;
        }

        orderedPoints = ordered;
        return true;
    }

    private static Point2f ApplyHomographyToPoint(double[] h, double x, double y)
    {
        double w = (h[6] * x) + (h[7] * y) + h[8];
        if (Math.Abs(w) < 1e-12)
            w = w < 0 ? -1e-12 : 1e-12;
        double u = ((h[0] * x) + (h[1] * y) + h[2]) / w;
        double v = ((h[3] * x) + (h[4] * y) + h[5]) / w;
        return new Point2f((float)u, (float)v);
    }

    private static double PointDistanceForGrid(Point2f a, Point2f b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static CalibrationBoardType GetDetectionBoardType(
        CalibProject project,
        bool isProjectedBoard
    )
    {
        return isProjectedBoard ? CalibrationBoardType.Chessboard : project.BoardType;
    }

    private static Size GetBoardPatternSize(CalibProject project, bool isProjectedBoard)
    {
        if (isProjectedBoard)
        {
            return new Size(project.ProjectedCornerCols, project.ProjectedCornerRows);
        }

        if (project.BoardType == CalibrationBoardType.Chessboard)
        {
            return new Size(project.PhysicalCornerCols, project.PhysicalCornerRows);
        }

        int cols = project.CirclePatternCols ?? 0;
        int rows = project.CirclePatternRows ?? 0;
        return new Size(cols, rows);
    }
}
