using System.Runtime.InteropServices;
using System.Text.Json;
using AuroraStruct3D.Calibration.Dtos;
using AuroraStruct3D.Cameras;
using AuroraStruct3D.Cameras.Dtos;
using AuroraStruct3D.Projectors;
using AuroraStruct3D.Projectors.Dtos;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using SkiaSharp;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.Calibration;

/// <summary>
/// 标定照片管理应用服务实现（Step 5）。
/// 负责：棋盘格参数配置、内参/外参拍照（含 OpenCV 角点检测）、照片查询/删除、内外参计算。
/// </summary>
[Authorize]
public class CalibPhotoAppService : AuroraStruct3DAppService, ICalibPhotoAppService
{
    private readonly IRepository<CalibProject, Guid> _projectRepo;
    private readonly IRepository<CalibCameraParam, Guid> _cameraParamRepo;
    private readonly IRepository<CalibPhotoRecord, Guid> _photoRepo;
    private readonly IBlobContainer<CalibPhotoBlobContainer> _blobContainer;
    private readonly ICameraDeviceAppService _cameraService;
    private readonly IProjectorDeviceAppService _projectorService;
    private readonly ILogger<CalibPhotoAppService> _logger;

    /// <summary>构造注入</summary>
    public CalibPhotoAppService(
        IRepository<CalibProject, Guid> projectRepo,
        IRepository<CalibCameraParam, Guid> cameraParamRepo,
        IRepository<CalibPhotoRecord, Guid> photoRepo,
        IBlobContainer<CalibPhotoBlobContainer> blobContainer,
        ICameraDeviceAppService cameraService,
        IProjectorDeviceAppService projectorService,
        ILogger<CalibPhotoAppService> logger
    )
    {
        _projectRepo = projectRepo;
        _cameraParamRepo = cameraParamRepo;
        _photoRepo = photoRepo;
        _blobContainer = blobContainer;
        _cameraService = cameraService;
        _projectorService = projectorService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<CalibBoardConfigDto> UpdateBoardConfigAsync(UpdateBoardConfigInput input)
    {
        CalibProject project = await _projectRepo.GetAsync(input.CalibProjectId);

        project.SetBoardConfig(
            input.PhysicalCornerRows,
            input.PhysicalCornerCols,
            input.PhysicalSquareSizeMm,
            input.ProjectedCornerRows,
            input.ProjectedCornerCols,
            input.ProjectedPixelSize
        );

        await _projectRepo.UpdateAsync(project);
        return ToBoardConfigDto(project);
    }

    /// <inheritdoc/>
    public async Task<CalibBoardConfigDto> GetBoardConfigAsync(Guid calibProjectId)
    {
        CalibProject project = await _projectRepo.GetAsync(calibProjectId);
        return ToBoardConfigDto(project);
    }

    /// <inheritdoc/>
    public async Task<CalibPhotoDto> TakeIntrinsicPhotoAsync(TakeIntrinsicPhotoInput input)
    {
        // 获取项目棋盘格参数
        CalibProject project = await _projectRepo.GetAsync(input.CalibProjectId);

        // 如果传入了投影仪 ID，先关圆 LED，确保内参拍照不受投影光干扰
        if (input.ProjectorDeviceId.HasValue)
        {
            await _projectorService.LedOffAsync(input.ProjectorDeviceId.Value);
        }

        // 拍照
        CameraSnapshotDto snapshot = await _cameraService.TakeSnapshotAsync(input.CameraDeviceId);

        // 从 data URI 提取 JPEG bytes
        byte[] jpegBytes = ExtractJpegBytes(snapshot.DataUri);

        // OpenCV 棋盘格角点检测
        (bool isValid, int cornerCount) = DetectChessboardCorners(
            jpegBytes,
            project.PhysicalCornerCols,
            project.PhysicalCornerRows
        );

        // 生成缩略图
        string? thumbBase64 = GenerateThumbnailBase64(jpegBytes);

        // 存 BLOB
        string blobKey = BuildBlobKey(
            input.CalibProjectId,
            input.CameraDeviceId,
            CalibPhotoType.Intrinsic
        );
        await _blobContainer.SaveAsync(blobKey, jpegBytes, overrideExisting: false);

        // 写数据库
        CalibPhotoRecord record = new(
            GuidGenerator.Create(),
            input.CalibProjectId,
            input.CameraDeviceId,
            CalibPhotoType.Intrinsic,
            blobKey,
            isValid,
            cornerCount,
            thumbBase64
        );
        await _photoRepo.InsertAsync(record);

        return ToPhotoDto(record);
    }

    /// <inheritdoc/>
    public async Task<CalibPhotoDto> TakeExtrinsicPhotoAsync(TakeExtrinsicPhotoInput input)
    {
        CalibProject project = await _projectRepo.GetAsync(input.CalibProjectId);

        // 1. 开灯
        await _projectorService.LedOnAsync(input.ProjectorDeviceId);

        // 2. 设置显示模式为棋盘格
        await _projectorService.SetDisplayModeAsync(
            new SetProjectorDisplayModeDto
            {
                ProjectorDeviceId = input.ProjectorDeviceId,
                Mode = ProjectorDisplayMode.Checkerboard,
            }
        );

        // 3. 设置棋盘格像素尺寸（使用项目配置的 ProjectedPixelSize）
        await _projectorService.SetCheckerboardPixelSizeAsync(
            new SetProjectorCheckerboardDto
            {
                ProjectorDeviceId = input.ProjectorDeviceId,
                PixelSize = project.ProjectedPixelSize > 0 ? project.ProjectedPixelSize : 30,
            }
        );

        // 4. 短暂延迟，等待投影仪稳定（约 200ms）
        await Task.Delay(200);

        // 5. 拍照
        CameraSnapshotDto snapshot = await _cameraService.TakeSnapshotAsync(input.CameraDeviceId);

        // 6. 关灯（拍完立即关）
        await _projectorService.LedOffAsync(input.ProjectorDeviceId);

        byte[] jpegBytes = ExtractJpegBytes(snapshot.DataUri);

        // 外参照片检测：棋盘格内角点配置与内参相同（实体棋盘格）
        (bool isValid, int cornerCount) = DetectChessboardCorners(
            jpegBytes,
            project.PhysicalCornerCols,
            project.PhysicalCornerRows
        );

        string? thumbBase64 = GenerateThumbnailBase64(jpegBytes);

        string blobKey = BuildBlobKey(
            input.CalibProjectId,
            input.CameraDeviceId,
            CalibPhotoType.Extrinsic
        );
        await _blobContainer.SaveAsync(blobKey, jpegBytes, overrideExisting: false);

        CalibPhotoRecord record = new(
            GuidGenerator.Create(),
            input.CalibProjectId,
            input.CameraDeviceId,
            CalibPhotoType.Extrinsic,
            blobKey,
            isValid,
            cornerCount,
            thumbBase64
        );
        await _photoRepo.InsertAsync(record);

        return ToPhotoDto(record);
    }

    /// <inheritdoc/>
    public async Task<List<CalibPhotoDto>> GetPhotoListAsync(
        Guid calibProjectId,
        Guid cameraDeviceId,
        CalibPhotoType? photoType = null
    )
    {
        IQueryable<CalibPhotoRecord> query = await _photoRepo.GetQueryableAsync();
        query = query.Where(x =>
            x.CalibProjectId == calibProjectId && x.CameraDeviceId == cameraDeviceId
        );
        if (photoType.HasValue)
        {
            query = query.Where(x => x.PhotoType == photoType.Value);
        }
        query = query.OrderBy(x => x.CapturedAt);

        List<CalibPhotoRecord> items = await AsyncExecuter.ToListAsync(query);
        return items.Select(ToPhotoDto).ToList();
    }

    /// <inheritdoc/>
    /// <inheritdoc/>
    public async Task DeleteAsync(Guid id)
    {
        CalibPhotoRecord record = await _photoRepo.GetAsync(id);

        // 删除 BLOB 文件（忽略不存在的情况）
        try
        {
            await _blobContainer.DeleteAsync(record.BlobKey);
        }
        catch
        {
            // BLOB 不存在时忽略
        }

        await _photoRepo.DeleteAsync(id);
    }

    /// <inheritdoc/>
    public async Task<int> DeleteInvalidPhotosAsync(Guid calibProjectId, Guid cameraDeviceId)
    {
        IQueryable<CalibPhotoRecord> query = await _photoRepo.GetQueryableAsync();
        List<CalibPhotoRecord> invalidPhotos = await AsyncExecuter.ToListAsync(
            query.Where(x =>
                x.CalibProjectId == calibProjectId
                && x.CameraDeviceId == cameraDeviceId
                && !x.IsValid
            )
        );

        if (invalidPhotos.Count == 0)
            return 0;

        foreach (CalibPhotoRecord record in invalidPhotos)
        {
            try
            {
                await _blobContainer.DeleteAsync(record.BlobKey);
            }
            catch
            {
                // BLOB 不存在时忽略
            }
        }

        await _photoRepo.DeleteManyAsync(invalidPhotos.Select(x => x.Id));

        _logger.LogInformation(
            "删除无效照片: 项目 {ProjectId} 相机 {CameraId} 共删除 {Count} 张",
            calibProjectId,
            cameraDeviceId,
            invalidPhotos.Count
        );

        return invalidPhotos.Count;
    }

    /// <inheritdoc/>
    public async Task<CalibComputeResultDto> ComputeCalibrationAsync(
        Guid calibProjectId,
        Guid cameraDeviceId
    )
    {
        CalibProject project = await _projectRepo.GetAsync(calibProjectId);

        if (
            project.PhysicalCornerRows < 2
            || project.PhysicalCornerCols < 2
            || project.PhysicalSquareSizeMm <= 0
        )
        {
            throw new UserFriendlyException("请先配置棋盘格参数（内角点行数、列数、方格边长）");
        }

        // 查询有效内参照片
        IQueryable<CalibPhotoRecord> query = await _photoRepo.GetQueryableAsync();
        List<CalibPhotoRecord> intrinsicPhotos = await AsyncExecuter.ToListAsync(
            query
                .Where(x =>
                    x.CalibProjectId == calibProjectId
                    && x.CameraDeviceId == cameraDeviceId
                    && x.PhotoType == CalibPhotoType.Intrinsic
                    && x.IsValid
                )
                .OrderBy(x => x.CapturedAt)
        );

        if (intrinsicPhotos.Count < CalibConsts.MinValidPhotoCount)
        {
            throw new UserFriendlyException(
                $"内参有效照片不足 {CalibConsts.MinValidPhotoCount} 张（当前 {intrinsicPhotos.Count} 张），无法计算内参"
            );
        }

        // 查询有效外参照片
        List<CalibPhotoRecord> extrinsicPhotos = await AsyncExecuter.ToListAsync(
            query
                .Where(x =>
                    x.CalibProjectId == calibProjectId
                    && x.CameraDeviceId == cameraDeviceId
                    && x.PhotoType == CalibPhotoType.Extrinsic
                    && x.IsValid
                )
                .OrderBy(x => x.CapturedAt)
        );

        // ── 计算内参 ─────────────────────────────────────────────────────────────
        int cornerCols = project.PhysicalCornerCols;
        int cornerRows = project.PhysicalCornerRows;
        float squareSizeMm = (float)project.PhysicalSquareSizeMm;
        Size patternSize = new(cornerCols, cornerRows);

        // 构建世界坐标（棋盘格平面，Z=0）
        Point3f[] worldCorners = BuildWorldCorners(cornerCols, cornerRows, squareSizeMm);

        // OpenCvSharp4 CalibrateCamera 需要 IEnumerable<Mat>，将 Point 数组转换为 Mat
        List<Mat> objectMats = [];
        List<Mat> imageMats = [];
        Size imageSize = default;

        foreach (CalibPhotoRecord photo in intrinsicPhotos)
        {
            byte[] bytes = await _blobContainer.GetAllBytesAsync(photo.BlobKey);

            // 优先 OpenCV，失败时用 SkiaSharp fallback（ARM64 JPEG 兼容性保障）
            using Mat mat = LoadBgrMat(bytes);
            if (mat.Empty())
                continue;

            if (imageSize == default)
            {
                imageSize = new Size(mat.Cols, mat.Rows);
            }

            Point2f[]? corners = FindCornersSubpix(mat, patternSize);
            if (corners == null)
                continue;

            objectMats.Add(Mat.FromArray(worldCorners));
            imageMats.Add(Mat.FromArray(corners));
        }

        if (objectMats.Count < CalibConsts.MinValidPhotoCount)
        {
            // 释放已创建的 Mat
            foreach (Mat m in objectMats)
                m.Dispose();
            foreach (Mat m in imageMats)
                m.Dispose();
            throw new UserFriendlyException("从 BLOB 重新加载后有效内参照片不足，请重新拍摄");
        }

        // calibrateCamera
        using Mat cameraMatrix = new();
        using Mat distCoeffs = new();

        double reprojError;
        Mat[] rvecArray;
        Mat[] tvecArray;
        try
        {
            reprojError = Cv2.CalibrateCamera(
                objectMats,
                imageMats,
                imageSize,
                cameraMatrix,
                distCoeffs,
                out rvecArray,
                out tvecArray,
                CalibrationFlags.None
            );
        }
        finally
        {
            foreach (Mat m in objectMats)
                m.Dispose();
            foreach (Mat m in imageMats)
                m.Dispose();
        }

        // 序列化内参
        string intrinsicJson = SerializeMatToJson(cameraMatrix);
        string distJson = SerializeVecToJson(distCoeffs);

        // ── 计算外参（使用第一张有效外参照片）─────────────────────────────────────
        string? rvecJson = null;
        string? tvecJson = null;

        if (extrinsicPhotos.Count > 0)
        {
            CalibPhotoRecord? bestExtrinsic = await FindBestExtrinsicAsync(
                extrinsicPhotos,
                cameraMatrix,
                distCoeffs,
                patternSize,
                worldCorners
            );

            if (bestExtrinsic != null)
            {
                byte[] exBytes = await _blobContainer.GetAllBytesAsync(bestExtrinsic.BlobKey);
                using Mat exMat = LoadBgrMat(exBytes);
                Point2f[]? exCorners = FindCornersSubpix(exMat, patternSize);
                if (exCorners != null)
                {
                    using Mat rvec = new();
                    using Mat tvec = new();
                    // SolvePnP 需要 InputArray，使用 InputArray.Create() 包装数组
                    Cv2.SolvePnP(
                        InputArray.Create(worldCorners),
                        InputArray.Create(exCorners),
                        cameraMatrix,
                        distCoeffs,
                        rvec,
                        tvec
                    );
                    rvecJson = SerializeVecToJson(rvec);
                    tvecJson = SerializeVecToJson(tvec);
                }
            }
        }

        // 释放 rvec/tvec 数组
        foreach (Mat m in rvecArray)
            m.Dispose();
        foreach (Mat m in tvecArray)
            m.Dispose();

        // ── 持久化结果到 CalibCameraParam ────────────────────────────────────────
        IQueryable<CalibCameraParam> paramQuery = await _cameraParamRepo.GetQueryableAsync();
        CalibCameraParam? camParam = await AsyncExecuter.FirstOrDefaultAsync(
            paramQuery.Where(x =>
                x.CalibProjectId == calibProjectId && x.CameraDeviceId == cameraDeviceId
            )
        );

        if (camParam != null)
        {
            camParam.SetCalibResult(intrinsicJson, distJson, reprojError, rvecJson, tvecJson);
            await _cameraParamRepo.UpdateAsync(camParam);
        }

        return new CalibComputeResultDto
        {
            IntrinsicMatrixJson = intrinsicJson,
            DistCoeffsJson = distJson,
            ReprojectionError = reprojError,
            ExtrinsicRvecJson = rvecJson,
            ExtrinsicTvecJson = tvecJson,
        };
    }

    /// <inheritdoc/>
    public async Task<CalibCameraStatusDto> GetCameraStatusAsync(
        Guid calibProjectId,
        Guid cameraDeviceId
    )
    {
        IQueryable<CalibPhotoRecord> query = await _photoRepo.GetQueryableAsync();
        List<CalibPhotoRecord> all = await AsyncExecuter.ToListAsync(
            query.Where(x =>
                x.CalibProjectId == calibProjectId && x.CameraDeviceId == cameraDeviceId
            )
        );

        int intrinsicTotal = all.Count(x => x.PhotoType == CalibPhotoType.Intrinsic);
        int intrinsicValid = all.Count(x => x.PhotoType == CalibPhotoType.Intrinsic && x.IsValid);
        int extrinsicTotal = all.Count(x => x.PhotoType == CalibPhotoType.Extrinsic);
        int extrinsicValid = all.Count(x => x.PhotoType == CalibPhotoType.Extrinsic && x.IsValid);

        // 读取最新标定结果
        IQueryable<CalibCameraParam> paramQuery = await _cameraParamRepo.GetQueryableAsync();
        CalibCameraParam? camParam = await AsyncExecuter.FirstOrDefaultAsync(
            paramQuery.Where(x =>
                x.CalibProjectId == calibProjectId && x.CameraDeviceId == cameraDeviceId
            )
        );

        CalibComputeResultDto? latestResult = null;
        if (camParam?.IntrinsicMatrixJson != null)
        {
            latestResult = new CalibComputeResultDto
            {
                IntrinsicMatrixJson = camParam.IntrinsicMatrixJson,
                DistCoeffsJson = camParam.DistCoeffsJson ?? string.Empty,
                ReprojectionError = camParam.ReprojectionError ?? 0,
                ExtrinsicRvecJson = camParam.ExtrinsicRvecJson,
                ExtrinsicTvecJson = camParam.ExtrinsicTvecJson,
            };
        }

        return new CalibCameraStatusDto
        {
            CameraDeviceId = cameraDeviceId,
            IntrinsicTotal = intrinsicTotal,
            IntrinsicValid = intrinsicValid,
            ExtrinsicTotal = extrinsicTotal,
            ExtrinsicValid = extrinsicValid,
            LatestResult = latestResult,
        };
    }

    // ─── 私有辅助方法 ─────────────────────────────────────────────────────────────

    /// <summary>从 data URI 提取 JPEG 二进制</summary>
    private static byte[] ExtractJpegBytes(string dataUri)
    {
        const string prefix = "data:image/jpeg;base64,";
        if (dataUri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return Convert.FromBase64String(dataUri[prefix.Length..]);
        }
        // 尝试直接 Base64
        return Convert.FromBase64String(dataUri);
    }

    /// <summary>构建标定照片的 BLOB Key</summary>
    private static string BuildBlobKey(Guid projectId, Guid cameraId, CalibPhotoType type)
    {
        string typeStr = type == CalibPhotoType.Intrinsic ? "intrinsic" : "extrinsic";
        string ts = DateTime.UtcNow.ToString("yyyyMMddHHmmss_fff");
        return $"{projectId}/{cameraId}/{typeStr}/{ts}.jpg";
    }

    /// <summary>OpenCV 棋盘格角点检测（使用亚像素精化）</summary>
    private (bool isValid, int cornerCount) DetectChessboardCorners(
        byte[] imageBytes,
        int cols,
        int rows
    )
    {
        try
        {
            Size patternSize = new(cols, rows);

            // 优先尝试 OpenCV 直接解码
            using Mat mat = Cv2.ImDecode(imageBytes, ImreadModes.Color);
            if (!mat.Empty())
            {
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

            // OpenCV 解码失败（ARM64 JPEG 兼容问题）——改用 SkiaSharp 解码后转换为 BGR Mat
            _logger.LogDebug("棋盘格检测: OpenCV 解码失败，尝试 SkiaSharp fallback");
            using Mat skMat = SkiaBytesToBgrMat(imageBytes);
            if (skMat.Empty())
            {
                _logger.LogWarning("棋盘格检测: SkiaSharp 解码也失败，图像不可用");
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

    /// <summary>
    /// 查找棋盘格角点并做亚像素精化。
    /// 按三个策略依次尝试，任意成功即返回；全部失败返回 null。
    /// 策略一：AdaptiveThresh + NormalizeImage（标准，最快）
    /// 策略二：加 FilterQuads（对复杂背景更鲁棒）
    /// 策略三：FindChessboardCornersSB（sub-pixel 搜索，对光照不均匀最鲁棒）
    /// </summary>
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

        // 策略一：标准 AdaptiveThresh + NormalizeImage
        if (
            Cv2.FindChessboardCorners(
                gray,
                patternSize,
                out Point2f[] corners1,
                ChessboardFlags.AdaptiveThresh | ChessboardFlags.NormalizeImage
            )
            && corners1.Length > 0
        )
        {
            Cv2.CornerSubPix(
                gray,
                corners1,
                new Size(11, 11),
                new Size(-1, -1),
                new TermCriteria(CriteriaTypes.Eps | CriteriaTypes.MaxIter, 30, 0.001)
            );
            return corners1;
        }

        // 策略二：增加 FilterQuads，对复杂背景有改善
        if (
            Cv2.FindChessboardCorners(
                gray,
                patternSize,
                out Point2f[] corners2,
                ChessboardFlags.AdaptiveThresh
                    | ChessboardFlags.NormalizeImage
                    | ChessboardFlags.FilterQuads
            )
            && corners2.Length > 0
        )
        {
            Cv2.CornerSubPix(
                gray,
                corners2,
                new Size(11, 11),
                new Size(-1, -1),
                new TermCriteria(CriteriaTypes.Eps | CriteriaTypes.MaxIter, 30, 0.001)
            );
            return corners2;
        }

        // 策略三：FindChessboardCornersSB，对光照不均匀最鲁棒（已内含亚像素精化）
        if (
            Cv2.FindChessboardCornersSB(gray, patternSize, out Point2f[] corners3)
            && corners3.Length > 0
        )
        {
            return corners3;
        }

        return null;
    }

    /// <summary>生成缩略图 Base64（宽 400px，保持比例）——优先 SkiaSharp，避免 OpenCV ARM64 JPEG 兼容问题</summary>
    private static string? GenerateThumbnailBase64(byte[] imageBytes)
    {
        try
        {
            // 使用 SkiaSharp 解码（与 Tucam JPEG 编码器一致，就算是硬件 JPEG 也能解码）
            using SKBitmap? bitmap = SKBitmap.Decode(imageBytes);
            if (bitmap == null || bitmap.IsNull)
                return null;

            int targetWidth = 400;
            int targetHeight = Math.Max(
                1,
                (int)(bitmap.Height * (targetWidth / (double)bitmap.Width))
            );

            SKImageInfo info = new(
                targetWidth,
                targetHeight,
                SKColorType.Bgra8888,
                SKAlphaType.Opaque
            );
            using SKBitmap resized = new(info);
            bitmap.ScalePixels(resized, new SKSamplingOptions(SKFilterMode.Linear));

            using SKImage skImg = SKImage.FromBitmap(resized);
            using SKData? encoded = skImg.Encode(SKEncodedImageFormat.Jpeg, 70);
            if (encoded == null)
                return null;

            return $"data:image/jpeg;base64,{Convert.ToBase64String(encoded.ToArray())}";
        }
        catch
        {
            return null;
        }
    }

    /// <summary>构建世界坐标系中的棋盘格角点（Z=0 平面）</summary>
    private static Point3f[] BuildWorldCorners(int cols, int rows, float squareSizeMm)
    {
        Point3f[] points = new Point3f[cols * rows];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                points[r * cols + c] = new Point3f(c * squareSizeMm, r * squareSizeMm, 0f);
            }
        }
        return points;
    }

    /// <summary>从多张外参照片中找重投影误差最小的一张</summary>
    private async Task<CalibPhotoRecord?> FindBestExtrinsicAsync(
        List<CalibPhotoRecord> photos,
        Mat cameraMatrix,
        Mat distCoeffs,
        Size patternSize,
        Point3f[] worldCorners
    )
    {
        CalibPhotoRecord? best = null;
        double bestError = double.MaxValue;

        foreach (CalibPhotoRecord photo in photos)
        {
            try
            {
                byte[] bytes = await _blobContainer.GetAllBytesAsync(photo.BlobKey);

                // 先尝试 OpenCV，失败则用 SkiaSharp fallback
                using Mat mat = LoadBgrMat(bytes);
                if (mat.Empty())
                    continue;

                Point2f[]? corners = FindCornersSubpix(mat, patternSize);
                if (corners == null)
                    continue;

                using Mat rvec = new();
                using Mat tvec = new();
                // SolvePnP 需要 InputArray
                Cv2.SolvePnP(
                    InputArray.Create(worldCorners),
                    InputArray.Create(corners),
                    cameraMatrix,
                    distCoeffs,
                    rvec,
                    tvec
                );

                // ProjectPoints 第6参数为 OutputArray（不能用 out 关键字）
                using Mat projectedMat = new();
                Cv2.ProjectPoints(
                    InputArray.Create(worldCorners),
                    rvec,
                    tvec,
                    cameraMatrix,
                    distCoeffs,
                    projectedMat
                );
                projectedMat.GetArray(out Point2f[] projected);

                double err = 0;
                for (int i = 0; i < corners.Length; i++)
                {
                    double dx = corners[i].X - projected[i].X;
                    double dy = corners[i].Y - projected[i].Y;
                    err += Math.Sqrt(dx * dx + dy * dy);
                }
                err /= corners.Length;

                if (err < bestError)
                {
                    bestError = err;
                    best = photo;
                }
            }
            catch
            {
                // 忽略单张失败
            }
        }

        return best;
    }

    /// <summary>OpenCV 不能解码时用 SkiaSharp 解码并转换为 BGR Mat</summary>
    private static Mat SkiaBytesToBgrMat(byte[] imageBytes)
    {
        try
        {
            using SKBitmap? skBitmap = SKBitmap.Decode(imageBytes);
            if (skBitmap == null || skBitmap.IsNull)
                return new Mat();

            // 确保 Bgra8888 格式（内存布局：[B,G,R,A] 每像素 4 字节）
            using SKBitmap bgra =
                skBitmap.ColorType == SKColorType.Bgra8888
                    ? skBitmap.Copy()
                    : skBitmap.Copy(SKColorType.Bgra8888);

            int width = bgra.Width;
            int height = bgra.Height;
            byte[] bgraBytes = bgra.Bytes;

            // BGRA → BGR：去掉 Alpha 通道
            byte[] bgrBytes = new byte[width * height * 3];
            for (int i = 0, src = 0; src < bgraBytes.Length; i += 3, src += 4)
            {
                bgrBytes[i + 0] = bgraBytes[src + 0]; // B
                bgrBytes[i + 1] = bgraBytes[src + 1]; // G
                bgrBytes[i + 2] = bgraBytes[src + 2]; // R
            }

            Mat mat = new Mat(height, width, MatType.CV_8UC3);
            Marshal.Copy(bgrBytes, 0, mat.Data, bgrBytes.Length);
            return mat;
        }
        catch
        {
            return new Mat();
        }
    }

    /// <summary>
    /// 加载图像为 BGR Mat：优先 OpenCV，失败则用 SkiaSharp（ARM64 硬件 JPEG 兼容性保障）
    /// </summary>
    private static Mat LoadBgrMat(byte[] imageBytes)
    {
        using Mat mat = Cv2.ImDecode(imageBytes, ImreadModes.Color);
        if (!mat.Empty())
        {
            // 返回副本避免 using 释放
            return mat.Clone();
        }
        return SkiaBytesToBgrMat(imageBytes);
    }

    private static string SerializeMatToJson(Mat mat)
    {
        int rows = mat.Rows;
        int cols = mat.Cols;
        double[][] arr = new double[rows][];
        for (int r = 0; r < rows; r++)
        {
            arr[r] = new double[cols];
            for (int c = 0; c < cols; c++)
            {
                arr[r][c] = mat.At<double>(r, c);
            }
        }
        return JsonSerializer.Serialize(arr);
    }

    /// <summary>将 OpenCV 向量 Mat (N×1 或 1×N) 序列化为 JSON double[]</summary>
    private static string SerializeVecToJson(Mat mat)
    {
        int total = (int)mat.Total();
        double[] arr = new double[total];
        for (int i = 0; i < total; i++)
        {
            // 支持列向量 (N,1) 或行向量 (1,N)
            arr[i] = mat.Rows > 1 ? mat.At<double>(i, 0) : mat.At<double>(0, i);
        }
        return JsonSerializer.Serialize(arr);
    }

    // ─── DTO 转换 ─────────────────────────────────────────────────────────────

    private static CalibBoardConfigDto ToBoardConfigDto(CalibProject project) =>
        new()
        {
            PhysicalCornerRows = project.PhysicalCornerRows,
            PhysicalCornerCols = project.PhysicalCornerCols,
            PhysicalSquareSizeMm = project.PhysicalSquareSizeMm,
            ProjectedCornerRows = project.ProjectedCornerRows,
            ProjectedCornerCols = project.ProjectedCornerCols,
            ProjectedPixelSize = project.ProjectedPixelSize,
        };

    private static CalibPhotoDto ToPhotoDto(CalibPhotoRecord record) =>
        new()
        {
            Id = record.Id,
            PhotoType = record.PhotoType,
            IsValid = record.IsValid,
            CornerCountDetected = record.CornerCountDetected,
            CapturedAt = record.CapturedAt,
            ThumbnailBase64 = record.ThumbnailBase64,
        };
}
