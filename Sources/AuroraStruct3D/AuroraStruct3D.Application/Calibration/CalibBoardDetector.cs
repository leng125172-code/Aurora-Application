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

    public (bool isValid, int cornerCount, Point2f[] points) DetectIntrinsicBoardFeaturePoints(
        Mat gray,
        CalibProject project
    )
    {
        try
        {
            Size patternSize = GetBoardPatternSize(project, isProjectedBoard: false);
            CalibrationBoardType boardType = GetDetectionBoardType(
                project,
                isProjectedBoard: false
            );
            Point2f[] points =
                FindBoardPointsSubpixGray(gray, patternSize, boardType, project)
                ?? Array.Empty<Point2f>();
            int expectedCount = patternSize.Width * patternSize.Height;
            if (boardType == CalibrationBoardType.MarkedSymmetricCircleGrid)
                expectedCount--;

            bool isValid = points.Length == expectedCount;
            if (!isValid)
            {
                _logger.LogWarning(
                    "内参标定板检测失败或点数不完整: BoardType={BoardType}, Pattern={Cols}x{Rows}, Expected={Expected}, Actual={Actual}",
                    boardType,
                    patternSize.Width,
                    patternSize.Height,
                    expectedCount,
                    points.Length
                );
            }

            return (isValid, points.Length, points);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "内参标定板检测异常");
            return (false, 0, Array.Empty<Point2f>());
        }
    }

    public (bool isValid, int cornerCount) DetectBoardFeaturePoints(
        byte[] imageBytes,
        CalibProject project,
        bool isProjectedBoard
    )
    {
        CalibrationBoardType detectionBoardType = GetDetectionBoardType(project, isProjectedBoard);
        if (detectionBoardType == CalibrationBoardType.Chessboard)
        {
            Size chess = GetBoardPatternSize(project, isProjectedBoard);

            if (isProjectedBoard && project.BoardType != CalibrationBoardType.Chessboard)
            {
                using Mat projGray = CalibImageUtils.LoadGrayMat(imageBytes);
                if (projGray.Empty())
                    return (false, 0);

                using Mat projMask = new();
                Cv2.Threshold(
                    projGray,
                    projMask,
                    ProjectorMaskThreshold,
                    255,
                    ThresholdTypes.Binary
                );
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
                    _logger.LogDebug(
                        ex,
                        "投影棋盘格 FindChessboardCornersSB[SB原图] 检测失败，已跳过"
                    );
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

            return DetectChessboardCorners(imageBytes, chess.Width, chess.Height);
        }

        try
        {
            Size patternSize = GetBoardPatternSize(project, isProjectedBoard);
            using Mat gray = CalibImageUtils.LoadGrayMat(imageBytes);
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
        int rows
    )
    {
        try
        {
            Size patternSize = new(cols, rows);

            using Mat mat = Cv2.ImDecode(imageBytes, ImreadModes.Color);
            if (!mat.Empty())
            {
                if (!HasChessboardFeatureQuickCheck(mat, patternSize))
                {
                    _logger.LogDebug(
                        "棋盘格快速预检未通过 (OpenCV解码路径): {Cols}x{Rows}，图像大小 {W}x{H}",
                        cols,
                        rows,
                        mat.Cols,
                        mat.Rows
                    );
                    return (false, 0);
                }

                _logger.LogDebug(
                    "棋盘格检测: OpenCV 解码成功 {W}x{H}，目标格式 {Cols}x{Rows}",
                    mat.Cols,
                    mat.Rows,
                    cols,
                    rows
                );
                Point2f[]? corners = FindCornersSubpix(mat, patternSize);
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
                    mat.Cols,
                    mat.Rows
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

            if (!HasChessboardFeatureQuickCheck(skMat, patternSize))
            {
                _logger.LogDebug(
                    "棋盘格快速预检未通过 (SkiaSharp解码路径): {Cols}x{Rows}，图像大小 {W}x{H}",
                    cols,
                    rows,
                    skMat.Cols,
                    skMat.Rows
                );
                return (false, 0);
            }

            _logger.LogDebug(
                "棋盘格检测: SkiaSharp 解码成功 {W}x{H}，目标格式 {Cols}x{Rows}",
                skMat.Cols,
                skMat.Rows,
                cols,
                rows
            );
            Point2f[]? skCorners = FindCornersSubpix(skMat, patternSize);
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
                skMat.Cols,
                skMat.Rows
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
                Cv2.CornerSubPix(gray, corners, SubPixWinSize, SubPixZeroZone, SubPixCriteria);

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
                Cv2.CornerSubPix(gray, corners, SubPixWinSize, SubPixZeroZone, SubPixCriteria);

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
                    Cv2.CornerSubPix(gray, corners, SubPixWinSize, SubPixZeroZone, SubPixCriteria);

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
        // 标定拍照通常为 2K/5MP 图像。圆点组网并不需要全分辨率，
        // 先在半分辨率完成 Blob 检测可将像素扫描量降至约 1/4；
        // 快速路径失败时仍回退原图，避免降低困难姿态下的识别率。
        if (gray.Cols >= 1600 || gray.Rows >= 1600)
        {
            int halfWidth = Math.Max(1, gray.Cols / 2);
            int halfHeight = Math.Max(1, gray.Rows / 2);
            using Mat half = new();
            Cv2.Resize(
                gray,
                half,
                new Size(halfWidth, halfHeight),
                0,
                0,
                InterpolationFlags.Area
            );

            Point2f[]? halfResult = FindCircleGridPointsCore(
                half,
                patternSize,
                boardType,
                detectorConfig,
                project,
                "半分辨率/"
            );
            if (halfResult != null)
            {
                float scaleX = (float)gray.Cols / halfWidth;
                float scaleY = (float)gray.Rows / halfHeight;
                for (int i = 0; i < halfResult.Length; i++)
                {
                    halfResult[i].X *= scaleX;
                    halfResult[i].Y *= scaleY;
                }

                return halfResult;
            }

            _logger.LogDebug(
                "圆点板半分辨率快速检测失败，回退全分辨率: Image={Width}x{Height}",
                gray.Cols,
                gray.Rows
            );
        }

        return FindCircleGridPointsCore(
            gray,
            patternSize,
            boardType,
            detectorConfig,
            project,
            string.Empty
        );
    }

    private Point2f[]? FindCircleGridPointsCore(
        Mat gray,
        Size patternSize,
        CalibrationBoardType boardType,
        CircleBlobDetectorConfigDto detectorConfig,
        CalibProject? project,
        string preprocessingPrefix
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

                // 利用 keypoint.Size 估算先验像素半径
                float estimatedRadius = EstimateMedianRadiusPx(markedKeypoints);
                KeyPoint[] sizeFiltered = FilterCircleKeypointsBySize(
                    markedKeypoints,
                    expectedCount
                );
                Point2f[] markedCenters = sizeFiltered
                    .Select(x => new Point2f(x.Pt.X, x.Pt.Y))
                    .ToArray();

                // 边缘亚像素精化临时关闭，待算法优化后再启用
                _logger.LogDebug(
                    "圆心边缘精化已关闭 — SimpleBlobDetector 候选={RawCount}, 尺寸过滤后={FilteredCount}, 中位半径={Radius:F2}px",
                    markedKeypoints.Length,
                    markedCenters.Length,
                    estimatedRadius
                );

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
                        preprocessingPrefix + preprocessingName,
                        boardType,
                        patternSize.Width,
                        patternSize.Height,
                        orderedMarked.Length
                    );
                    return orderedMarked;
                }

                _logger.LogWarning(
                    "圆点板检测失败[{Preprocessing}]: BoardType={BoardType}, Pattern={Cols}x{Rows}, 原因={FailureReason}",
                    preprocessingPrefix + preprocessingName,
                    boardType,
                    patternSize.Width,
                    patternSize.Height,
                    markedFailureReason
                );
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
                        preprocessingPrefix + preprocessingName,
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

                // 利用 keypoint.Size 估算先验像素半径
                float estimatedRadius = EstimateMedianRadiusPx(keypoints);
                KeyPoint[] sizeFiltered = FilterCircleKeypointsBySize(keypoints, expectedCount);
                Point2f[] blobCenters = sizeFiltered
                    .Select(x => new Point2f(x.Pt.X, x.Pt.Y))
                    .ToArray();

                // 边缘亚像素精化临时关闭，待算法优化后再启用
                _logger.LogDebug(
                    "圆心边缘精化已关闭 — SimpleBlobDetector 候选={RawCount}, 尺寸过滤后={FilteredCount}, 中位半径={Radius:F2}px",
                    keypoints.Length,
                    blobCenters.Length,
                    estimatedRadius
                );

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
                        preprocessingPrefix + preprocessingName,
                        boardType,
                        patternSize.Width,
                        patternSize.Height,
                        ordered.Length
                    );
                    return ordered;
                }

                _logger.LogWarning(
                    "圆点板检测失败[{Preprocessing}]: BoardType={BoardType}, Pattern={Cols}x{Rows}, 原因={FailureReason}, Method=CustomSort",
                    preprocessingPrefix + preprocessingName,
                    boardType,
                    patternSize.Width,
                    patternSize.Height,
                    failureReason
                );
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

    /// <summary>
    /// 圆心边缘亚像素精化：基于 SimpleBlobDetector 估算的先验半径，
    /// 在圆心附近沿径向搜索 Sobel 梯度最大点，用 Cv2.FitEllipse 拟合椭圆中心。
    /// 适用于斜拍角度 &lt; 45° 的场景（椭圆短轴/长轴比 &gt;= 0.7）。
    /// </summary>
    /// <param name="gray">输入灰度图</param>
    /// <param name="roughCenters">SimpleBlobDetector 给出的粗略圆心</param>
    /// <param name="estimatedRadiusPx">先验像素半径（来自 keypoint.Size 中位数）</param>
    /// <returns>精化后的圆心数组；精化失败的位置回退为原始圆心</returns>
    private Point2f[] RefineCircleCentersByEdge(
        Mat gray,
        Point2f[] roughCenters,
        float estimatedRadiusPx
    )
    {
        // 先验半径过小或没有圆心，直接返回原值
        if (roughCenters.Length == 0 || estimatedRadiusPx < 3f)
            return roughCenters;

        // 高斯模糊降噪，提升梯度响应稳定性
        using Mat blurred = new();
        Cv2.GaussianBlur(gray, blurred, new Size(3, 3), 0);

        // Sobel 梯度幅值图（CV_32F）
        using Mat gradX = new();
        using Mat gradY = new();
        using Mat gradMag = new();
        Cv2.Sobel(blurred, gradX, MatType.CV_32F, 1, 0, 3);
        Cv2.Sobel(blurred, gradY, MatType.CV_32F, 0, 1, 3);
        Cv2.Magnitude(gradX, gradY, gradMag);

        // 搜索范围：理论半径 ±30%，覆盖斜拍 45° 时短轴 ≈ 0.7R 的变化
        double rMin = estimatedRadiusPx * 0.7;
        double rMax = estimatedRadiusPx * 1.3;
        int radialSteps = (int)Math.Ceiling(rMax - rMin) + 1;
        const int NAngles = 32; // 32 个方向采样，兼顾精度与性能
        const float MinEdgeGradient = 30f; // 过滤弱响应，避免噪声干扰

        Point2f[] refined = new Point2f[roughCenters.Length];
        int refineCount = 0;
        int fallbackCount = 0;
        double totalShift = 0;
        double maxShift = 0;
        double totalEdgePoints = 0;
        for (int i = 0; i < roughCenters.Length; i++)
        {
            double cx = roughCenters[i].X;
            double cy = roughCenters[i].Y;

            List<Point2f> edgePoints = new(NAngles);
            for (int a = 0; a < NAngles; a++)
            {
                double theta = 2.0 * Math.PI * a / NAngles;
                double cosT = Math.Cos(theta);
                double sinT = Math.Sin(theta);

                // 沿径向搜索最大梯度点
                float maxGrad = 0f;
                double bestR = estimatedRadiusPx;
                for (int dr = 0; dr < radialSteps; dr++)
                {
                    double r = rMin + dr;
                    if (r > rMax)
                        break;

                    int x = (int)Math.Round(cx + r * cosT);
                    int y = (int)Math.Round(cy + r * sinT);
                    if (x < 0 || x >= gray.Cols || y < 0 || y >= gray.Rows)
                        continue;

                    float g = gradMag.At<float>(y, x);
                    if (g > maxGrad)
                    {
                        maxGrad = g;
                        bestR = r;
                    }
                }

                // 仅保留梯度足够强的边缘点
                if (maxGrad > MinEdgeGradient)
                {
                    edgePoints.Add(
                        new Point2f((float)(cx + bestR * cosT), (float)(cy + bestR * sinT))
                    );
                }
            }

            totalEdgePoints += edgePoints.Count;

            // 至少需要 5 个有效边缘点才能稳定拟合椭圆
            if (edgePoints.Count >= 5)
            {
                try
                {
                    // Cv2.FitEllipse 需要 InputArray 参数，用 Mat 包装点集（CV_32FC2 双通道浮点）
                    Point2f[] pts = edgePoints.ToArray();
                    using Mat pointsMat = Mat.FromArray(pts);
                    RotatedRect ellipse = Cv2.FitEllipse(pointsMat);

                    // 健壮性校验：精化中心与原圆心偏移不超过半径的 50%
                    // 避免边缘搜索失败导致椭圆拟合发散
                    double dx = ellipse.Center.X - cx;
                    double dy = ellipse.Center.Y - cy;
                    double shift = Math.Sqrt((dx * dx) + (dy * dy));
                    if (shift < estimatedRadiusPx * 0.5)
                    {
                        refined[i] = new Point2f(ellipse.Center.X, ellipse.Center.Y);
                        refineCount++;
                        totalShift += shift;
                        if (shift > maxShift)
                            maxShift = shift;
                        continue;
                    }
                }
                catch
                {
                    // FitEllipse 在点共线或退化时可能抛异常，回退到原始圆心
                }
            }

            // 回退：使用原始粗略圆心
            refined[i] = roughCenters[i];
            fallbackCount++;
        }

        _logger.LogInformation(
            "圆心边缘精化统计 — 总数={Total}, 精化={Refined}, 回退={Fallback}, "
                + "平均边缘点数={AvgEdgePoints:F1}/32, 平均偏移={AvgShift:F3}px, 最大偏移={MaxShift:F3}px, 先验半径={Radius:F2}px",
            roughCenters.Length,
            refineCount,
            fallbackCount,
            roughCenters.Length > 0 ? totalEdgePoints / roughCenters.Length : 0,
            refineCount > 0 ? totalShift / refineCount : 0,
            maxShift,
            estimatedRadiusPx
        );

        return refined;
    }

    /// <summary>
    /// 从 KeyPoint 数组估算中位像素半径。
    /// SimpleBlobDetector 的 KeyPoint.Size 表示 BLOB 直径（像素），半径 = Size / 2。
    /// 取中位数而非均值，以抑制异常 BLOB 的影响。
    /// </summary>
    private static float EstimateMedianRadiusPx(KeyPoint[] keypoints)
    {
        if (keypoints.Length == 0)
            return 0f;

        float[] sizes = new float[keypoints.Length];
        for (int i = 0; i < keypoints.Length; i++)
            sizes[i] = keypoints[i].Size;
        Array.Sort(sizes);

        // 中位数（浮点 Size 取中位即可）
        return sizes[sizes.Length / 2] / 2f;
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

    /// <summary>
    /// 利用标定板圆点直径应连续变化的特性，剔除明显过小/过大的背景 BLOB。
    /// 若过滤后不足目标点数则回退原集合，避免激进过滤破坏远近透视较大的图像。
    /// </summary>
    private static KeyPoint[] FilterCircleKeypointsBySize(
        KeyPoint[] keypoints,
        int expectedCount
    )
    {
        if (keypoints.Length <= expectedCount || keypoints.Length == 0)
            return keypoints;

        float[] sizes = keypoints
            .Select(x => x.Size)
            .Where(x => x > 0)
            .OrderBy(x => x)
            .ToArray();
        if (sizes.Length == 0)
            return keypoints;

        float median = sizes[sizes.Length / 2];
        float minSize = median * 0.55f;
        float maxSize = median * 1.8f;
        KeyPoint[] filtered = keypoints
            .Where(x => x.Size >= minSize && x.Size <= maxSize)
            .ToArray();

        return filtered.Length >= expectedCount ? filtered : keypoints;
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
        if (
            hasMarkerHole
            && (markerRow < 0 || markerRow >= rows || markerCol < 0 || markerCol >= cols)
        )
        {
            failureReason =
                $"invalid-marker-position:{markerRow},{markerCol} for grid {cols}x{rows}";
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

        // 真实网格角点至少有两个相邻圆点，边点有三个，内部点更多。
        // 仅用最近邻距离无法排除板外孤立杂点；增加邻接密度约束后再估计四角。
        double minNeighborDistance = 0.35 * medianNn;
        double maxNeighborDistance = 1.8 * medianNn;
        double minNeighborDistance2 = minNeighborDistance * minNeighborDistance;
        double maxNeighborDistance2 = maxNeighborDistance * maxNeighborDistance;
        int[] neighborCounts = new int[n];
        List<int>[] adjacency = Enumerable.Range(0, n).Select(_ => new List<int>()).ToArray();
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                double dx = points[i].X - points[j].X;
                double dy = points[i].Y - points[j].Y;
                double d2 = (dx * dx) + (dy * dy);
                if (d2 >= minNeighborDistance2 && d2 <= maxNeighborDistance2)
                {
                    neighborCounts[i]++;
                    neighborCounts[j]++;
                    adjacency[i].Add(j);
                    adjacency[j].Add(i);
                }
            }
        }

        // 水平板斜拍时近端/远端圆距不同，但整块标定板仍形成最大的连通网格簇。
        // 先取最大连通分量，可避免图像边缘、支架和反光产生的 BLOB 拉偏四角。
        bool[] visited = new bool[n];
        List<int> largestComponent = new();
        for (int start = 0; start < n; start++)
        {
            if (visited[start] || neighborCounts[start] < 2)
                continue;

            List<int> component = new();
            Queue<int> queue = new();
            visited[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                component.Add(current);
                foreach (int next in adjacency[current])
                {
                    if (visited[next] || neighborCounts[next] < 2)
                        continue;
                    visited[next] = true;
                    queue.Enqueue(next);
                }
            }

            if (component.Count > largestComponent.Count)
                largestComponent = component;
        }

        List<int> coreIndices = largestComponent
            .Where(i => nnDist[i] <= 2.0 * medianNn && neighborCounts[i] >= 2)
            .ToList();
        List<Point2f> core = coreIndices.Select(i => points[i]).ToList();
        if (core.Count < expected)
        {
            failureReason = $"core-insufficient:{core.Count}<{expected}";
            return false;
        }

        // 真正的四角在连通网格中邻接度最低。优先从低邻接度点选角，
        // 避免斜拍时板外残留点或内部强反光点成为透视四角。
        List<int> cornerIndices = coreIndices.Where(i => neighborCounts[i] <= 4).ToList();
        if (cornerIndices.Count < 4)
            cornerIndices = coreIndices;

        Point2f tl = points[cornerIndices[0]],
            tr = points[cornerIndices[0]],
            br = points[cornerIndices[0]],
            bl = points[cornerIndices[0]];
        double tlv = double.MaxValue,
            brv = double.MinValue,
            trv = double.MinValue,
            blv = double.MaxValue;
        foreach (int cornerIndex in cornerIndices)
        {
            Point2f p = points[cornerIndex];
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
        // 初始四角来自离散圆心，强透视下中间区域预测会存在累计误差。
        // 0.72 个单元仍小于相邻点间距，可保持唯一匹配并显著提高斜拍容差。
        double matchThreshold = 0.72 * cell;
        double matchThreshold2 = matchThreshold * matchThreshold;

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
