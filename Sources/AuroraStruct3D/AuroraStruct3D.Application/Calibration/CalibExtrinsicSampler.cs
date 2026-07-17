using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Volo.Abp.BlobStoring;

namespace AuroraStruct3D.Calibration;

public sealed class ProjectorExtrinsicSampleGroup
{
    public required Guid PairGroupId { get; init; }
    public required CalibPhotoRecord ProjectorOffPhoto { get; init; }
    public required CalibPhotoRecord ProjectorOnPhoto { get; init; }
    public required List<CalibPhotoRecord> ProjectorPatternPhotos { get; init; }

    public bool IsValid =>
        ProjectorPatternPhotos.Count > 0
        && ProjectorOffPhoto.IsValid
        && ProjectorOnPhoto.IsValid
        && (ProjectorOffPhoto.ImageDiffSignificant ?? true);
}

public class CalibExtrinsicSampler
{
    private readonly IBlobContainer<CalibPhotoBlobContainer> _blobContainer;
    private readonly ILogger<CalibExtrinsicSampler> _logger;
    private readonly CalibBoardDetector _boardDetector;

    public CalibExtrinsicSampler(
        IBlobContainer<CalibPhotoBlobContainer> blobContainer,
        ILogger<CalibExtrinsicSampler> logger,
        CalibBoardDetector boardDetector
    )
    {
        _blobContainer = blobContainer;
        _logger = logger;
        _boardDetector = boardDetector;
    }

    public static List<ProjectorExtrinsicSampleGroup> BuildExtrinsicSampleGroups(
        IEnumerable<CalibPhotoRecord> photos
    )
    {
        return photos
            .Where(x => x.PairGroupId.HasValue)
            .GroupBy(x => x.PairGroupId!.Value)
            .Select(group =>
            {
                CalibPhotoRecord? projectorOff =
                    group.FirstOrDefault(x => x.ExtrinsicPhase == ExtrinsicPhotoPhase.ProjectorOff)
                    ?? group.FirstOrDefault(x =>
                        x.ExtrinsicPhase == ExtrinsicPhotoPhase.WhiteScreen
                    );

                List<CalibPhotoRecord> projectorOnPhotos = group
                    .Where(x =>
                        x.ExtrinsicPhase == ExtrinsicPhotoPhase.ProjectorOn
                        || x.ExtrinsicPhase == ExtrinsicPhotoPhase.Checkerboard
                    )
                    .OrderBy(x => ExtractFrameIndexFromBlobKey(x.BlobKey) ?? int.MaxValue)
                    .ThenBy(x => x.CapturedAt)
                    .ToList();
                CalibPhotoRecord? projectorOn = projectorOnPhotos.LastOrDefault();

                return projectorOff == null || projectorOn == null
                    ? null
                    : new ProjectorExtrinsicSampleGroup
                    {
                        PairGroupId = group.Key,
                        ProjectorOffPhoto = projectorOff,
                        ProjectorOnPhoto = projectorOn,
                        ProjectorPatternPhotos = projectorOnPhotos,
                    };
            })
            .Where(x => x != null)
            .Select(x => x!)
            .OrderBy(x => x.ProjectorOffPhoto.CapturedAt)
            .ToList();
    }

    public static int? ExtractFrameIndexFromBlobKey(string blobKey)
    {
        int suffixStart = blobKey.LastIndexOf("_f", StringComparison.OrdinalIgnoreCase);
        int suffixEnd = blobKey.LastIndexOf(".jpg", StringComparison.OrdinalIgnoreCase);
        if (suffixStart < 0 || suffixEnd <= suffixStart + 2)
        {
            return null;
        }

        ReadOnlySpan<char> digits = blobKey.AsSpan(suffixStart + 2, suffixEnd - (suffixStart + 2));
        return int.TryParse(digits, out int frameIndex) ? frameIndex : null;
    }

    public async Task<ProjectorExtrinsicSampleGroup?> FindBestExtrinsicAsync(
        List<ProjectorExtrinsicSampleGroup> photos,
        Mat cameraMatrix,
        Mat distCoeffs,
        Size patternSize,
        Point3f[] worldCorners,
        CalibProject project,
        int rotationAngle = 0
    )
    {
        ProjectorExtrinsicSampleGroup? best = null;
        double bestError = double.MaxValue;

        foreach (ProjectorExtrinsicSampleGroup sample in photos)
        {
            try
            {
                Point2f[]? corners = await TryFindPhysicalBoardCornersFromSampleAsync(
                    sample,
                    patternSize,
                    project,
                    rotationAngle
                );
                if (corners == null)
                    continue;

                using Mat rvec = new();
                using Mat tvec = new();
                Cv2.SolvePnP(
                    InputArray.Create(worldCorners),
                    InputArray.Create(corners),
                    cameraMatrix,
                    distCoeffs,
                    rvec,
                    tvec
                );

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

                double err = CalibComputationUtils.ComputeReprojectionError(corners, projected);

                if (err < bestError)
                {
                    bestError = err;
                    best = sample;
                }
            }
            catch
            {
                // 忽略单张失败
            }
        }

        return best;
    }

    public async Task<Point2f[]?> TryFindPhysicalBoardCornersFromSampleAsync(
        ProjectorExtrinsicSampleGroup sample,
        Size patternSize,
        CalibProject project,
        int rotationAngle = 0
    )
    {
        try
        {
            byte[] bytes = await _blobContainer.GetAllBytesAsync(sample.ProjectorOffPhoto.BlobKey);
            using Mat mat = CalibImageUtils.LoadGrayMatWithRotation(bytes, rotationAngle);
            if (mat.Empty())
                return null;

            return _boardDetector.FindBoardPointsSubpixGray(mat, patternSize, project.BoardType, project);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[外参采样] 查找实体板角点失败 — PairGroupId={PairGroupId}", sample.PairGroupId);
            return null;
        }
    }

    public async Task<Point3f[]?> BuildProjectorObjectPointsCamAsync(
        ProjectorExtrinsicSampleGroup sample,
        Mat cameraMatrix,
        Mat distCoeffs,
        CalibProject project,
        Size physicalPatternSize,
        Point3f[] physicalWorldCorners,
        Size projPatternSize,
        int expectedProjCornerCount,
        int rotationAngle = 0
    )
    {
        try
        {
            Point2f[]? boardCorners = await TryFindPhysicalBoardCornersFromSampleAsync(
                sample,
                physicalPatternSize,
                project,
                rotationAngle
            );
            if (boardCorners == null || boardCorners.Length != physicalWorldCorners.Length)
                return null;

            using Mat rvecBoard = new();
            using Mat tvecBoard = new();
            Cv2.SolvePnP(
                InputArray.Create(physicalWorldCorners),
                InputArray.Create(boardCorners),
                cameraMatrix,
                distCoeffs,
                rvecBoard,
                tvecBoard
            );
            using Mat rBoard = new();
            Cv2.Rodrigues(rvecBoard, rBoard);

            double nx = rBoard.At<double>(0, 2);
            double ny = rBoard.At<double>(1, 2);
            double nz = rBoard.At<double>(2, 2);
            double p0x = tvecBoard.At<double>(0, 0);
            double p0y = tvecBoard.At<double>(1, 0);
            double p0z = tvecBoard.At<double>(2, 0);
            double nDotP0 = (nx * p0x) + (ny * p0y) + (nz * p0z);

            List<Point2f[]> detectedCornerSets = [];
            foreach (CalibPhotoRecord patternPhoto in sample.ProjectorPatternPhotos)
            {
                byte[] onBytes = await _blobContainer.GetAllBytesAsync(patternPhoto.BlobKey);
                using Mat onGray = CalibImageUtils.LoadGrayMatWithRotation(onBytes, rotationAngle);
                if (onGray.Empty())
                    continue;

                Point2f[]? detectedCorners = _boardDetector.FindBoardPointsSubpixGray(
                    onGray,
                    projPatternSize,
                    CalibrationBoardType.Chessboard,
                    project
                );

                if (detectedCorners != null && detectedCorners.Length == expectedProjCornerCount)
                {
                    detectedCornerSets.Add(detectedCorners);
                }
            }

            if (detectedCornerSets.Count == 0)
                return null;

            Point2f[] patternCorners;
            if (detectedCornerSets.Count == 1)
            {
                patternCorners = detectedCornerSets[0];
            }
            else
            {
                patternCorners = FuseCornersRobust(detectedCornerSets, expectedProjCornerCount);
            }

            using Mat undistMat = new();
            Cv2.UndistortPoints(InputArray.Create(patternCorners), undistMat, cameraMatrix, distCoeffs);
            undistMat.GetArray(out Point2f[] undist);
            if (undist.Length != patternCorners.Length)
                return null;

            double boardThickness = (double)project.BoardThicknessMm;

            Point3f[] objPts = new Point3f[patternCorners.Length];
            for (int i = 0; i < patternCorners.Length; i++)
            {
                double dx = undist[i].X;
                double dy = undist[i].Y;
                const double dz = 1.0;
                double nDotD = (nx * dx) + (ny * dy) + (nz * dz);
                if (Math.Abs(nDotD) < 1e-9)
                    return null;
                double tRay = nDotP0 / nDotD;
                if (tRay <= 0)
                    return null;

                double px = tRay * dx;
                double py = tRay * dy;
                double pz = tRay * dz;

                if (boardThickness > 0)
                {
                    double compensation = boardThickness / nDotD;
                    px += compensation * dx;
                    py += compensation * dy;
                    pz += compensation * dz;
                }

                objPts[i] = new Point3f((float)px, (float)py, (float)pz);
            }

            return objPts;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[外参采样] 构建投影仪对象点失败 — PairGroupId={PairGroupId}", sample.PairGroupId);
            return null;
        }
    }

    private static Point2f[] FuseCornersByMedian(
        List<Point2f[]> cornerSets,
        int expectedCornerCount
    )
    {
        Point2f[] fused = new Point2f[expectedCornerCount];
        for (int i = 0; i < expectedCornerCount; i++)
        {
            List<float> xs = new(cornerSets.Count);
            List<float> ys = new(cornerSets.Count);
            foreach (Point2f[] set in cornerSets)
            {
                xs.Add(set[i].X);
                ys.Add(set[i].Y);
            }

            xs.Sort();
            ys.Sort();
            int mid = xs.Count / 2;
            float mx = xs.Count % 2 == 1 ? xs[mid] : (xs[mid - 1] + xs[mid]) * 0.5f;
            float my = ys.Count % 2 == 1 ? ys[mid] : (ys[mid - 1] + ys[mid]) * 0.5f;
            fused[i] = new Point2f(mx, my);
        }

        return fused;
    }

    private static Point2f[] FuseCornersRobust(List<Point2f[]> cornerSets, int expectedCornerCount)
    {
        Point2f[] medianCorners = FuseCornersByMedian(cornerSets, expectedCornerCount);
        double[] rmsDistances = new double[cornerSets.Count];
        for (int setIndex = 0; setIndex < cornerSets.Count; setIndex++)
        {
            Point2f[] set = cornerSets[setIndex];
            double sumSquared = 0;
            for (int cornerIndex = 0; cornerIndex < expectedCornerCount; cornerIndex++)
            {
                double dx = set[cornerIndex].X - medianCorners[cornerIndex].X;
                double dy = set[cornerIndex].Y - medianCorners[cornerIndex].Y;
                sumSquared += (dx * dx) + (dy * dy);
            }

            rmsDistances[setIndex] = Math.Sqrt(sumSquared / expectedCornerCount);
        }

        double medianRms = ComputeMedian(rmsDistances);
        double inlierThreshold = Math.Max(0.5, medianRms * 2.5);

        List<int> inlierIndices = [];
        for (int i = 0; i < rmsDistances.Length; i++)
        {
            if (rmsDistances[i] <= inlierThreshold)
            {
                inlierIndices.Add(i);
            }
        }

        if (inlierIndices.Count == 0)
        {
            return medianCorners;
        }

        Point2f[] coarseFused = new Point2f[expectedCornerCount];
        for (int cornerIndex = 0; cornerIndex < expectedCornerCount; cornerIndex++)
        {
            double sumWeight = 0;
            double sumX = 0;
            double sumY = 0;
            foreach (int inlierIndex in inlierIndices)
            {
                double weight = 1.0 / Math.Max(0.25, rmsDistances[inlierIndex]);
                sumWeight += weight;
                sumX += cornerSets[inlierIndex][cornerIndex].X * weight;
                sumY += cornerSets[inlierIndex][cornerIndex].Y * weight;
            }

            coarseFused[cornerIndex] = new Point2f(
                (float)(sumX / sumWeight),
                (float)(sumY / sumWeight)
            );
        }

        Point2f[] refined = new Point2f[expectedCornerCount];
        for (int cornerIndex = 0; cornerIndex < expectedCornerCount; cornerIndex++)
        {
            double[] cornerDistances = new double[inlierIndices.Count];
            for (int i = 0; i < inlierIndices.Count; i++)
            {
                Point2f p = cornerSets[inlierIndices[i]][cornerIndex];
                double dx = p.X - coarseFused[cornerIndex].X;
                double dy = p.Y - coarseFused[cornerIndex].Y;
                cornerDistances[i] = Math.Sqrt((dx * dx) + (dy * dy));
            }

            double medianCornerDistance = ComputeMedian(cornerDistances);
            double cornerInlierThreshold = Math.Max(0.35, medianCornerDistance * 2.5);

            double sumWeight = 0;
            double sumX = 0;
            double sumY = 0;
            for (int i = 0; i < inlierIndices.Count; i++)
            {
                if (cornerDistances[i] > cornerInlierThreshold)
                {
                    continue;
                }

                int inlierIndex = inlierIndices[i];
                double frameWeight = 1.0 / Math.Max(0.25, rmsDistances[inlierIndex]);
                double cornerWeight = 1.0 / Math.Max(0.15, cornerDistances[i]);
                double weight = frameWeight * cornerWeight;

                sumWeight += weight;
                sumX += cornerSets[inlierIndex][cornerIndex].X * weight;
                sumY += cornerSets[inlierIndex][cornerIndex].Y * weight;
            }

            if (sumWeight > 1e-9)
            {
                refined[cornerIndex] = new Point2f(
                    (float)(sumX / sumWeight),
                    (float)(sumY / sumWeight)
                );
            }
            else
            {
                refined[cornerIndex] = coarseFused[cornerIndex];
            }
        }

        return refined;
    }

    private static double ComputeMedian(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        double[] sorted = values.ToArray();
        Array.Sort(sorted);
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) * 0.5;
    }
}
