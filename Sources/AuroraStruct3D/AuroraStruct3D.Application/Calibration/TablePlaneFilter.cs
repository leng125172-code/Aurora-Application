using OpenCvSharp;

namespace AuroraStruct3D.Calibration;

internal sealed record TablePlaneModel(double A, double B, double C, double D)
{
    public double SignedDistance(double x, double y, double z) => A * x + B * y + C * z + D;
}

internal sealed record TablePlaneFilterResult(
    bool Applied,
    TablePlaneModel? Plane,
    int CandidateCount,
    int InlierCount,
    int RemovedPointCount,
    string? FailureReason);

internal static class TablePlaneFilter
{
    private const int MinimumCandidateCount = 500;
    private const double MinimumInlierRate = 0.20d;
    private const double MinimumFallbackInlierRate = 0.35d;
    private const double RansacDistanceMm = 2d;
    private const int RansacIterations = 300;
    private const double BorderFraction = 0.15d;

    public static TablePlaneFilterResult Apply(
        Mat depth,
        Mat projectionP1,
        double clearanceMm,
        TablePlaneModel? cachedPlane = null)
    {
        ArgumentNullException.ThrowIfNull(depth);
        ArgumentNullException.ThrowIfNull(projectionP1);
        if (clearanceMm < 0 || clearanceMm > 50)
            throw new ArgumentOutOfRangeException(nameof(clearanceMm));

        TablePlaneModel? plane = cachedPlane;
        List<SamplePoint> candidates = [];
        int inlierCount = 0;
        if (plane is null)
        {
            candidates = CollectBorderCandidates(depth, projectionP1);
            bool usedFullFrameFallback = candidates.Count < MinimumCandidateCount;
            if (usedFullFrameFallback)
                candidates = CollectCandidates(depth, projectionP1, borderOnly: false);
            if (candidates.Count < MinimumCandidateCount)
                return Failed(candidates.Count, "台面候选点不足");

            (plane, inlierCount) = FitPlane(candidates);
            if (plane is null)
                return Failed(candidates.Count, "RANSAC 未找到可靠平面");
            double requiredInlierRate = usedFullFrameFallback
                ? MinimumFallbackInlierRate
                : MinimumInlierRate;
            if (inlierCount < MinimumCandidateCount
                || inlierCount / (double)candidates.Count < requiredInlierRate)
                return Failed(candidates.Count, "台面平面内点率不足", inlierCount);
            if (!HasSufficientSpatialCoverage(candidates, plane, usedFullFrameFallback))
                return Failed(candidates.Count, "台面平面空间覆盖不足", inlierCount);
        }

        int removed = FilterDepth(depth, projectionP1, plane, clearanceMm);
        return new TablePlaneFilterResult(true, plane, candidates.Count, inlierCount, removed, null);
    }

    private static List<SamplePoint> CollectBorderCandidates(Mat depth, Mat p1)
        => CollectCandidates(depth, p1, borderOnly: true);

    private static List<SamplePoint> CollectCandidates(Mat depth, Mat p1, bool borderOnly)
    {
        int width = depth.Cols;
        int height = depth.Rows;
        int borderX = Math.Max(1, (int)Math.Ceiling(width * BorderFraction));
        int borderY = Math.Max(1, (int)Math.Ceiling(height * BorderFraction));
        int step = Math.Max(1, Math.Min(width, height) / 400);
        double fx = p1.At<double>(0, 0), fy = p1.At<double>(1, 1);
        double cx = p1.At<double>(0, 2), cy = p1.At<double>(1, 2);
        List<SamplePoint> result = [];
        for (int y = 0; y < height; y += step)
        for (int x = 0; x < width; x += step)
        {
            if (borderOnly
                && x >= borderX && x < width - borderX
                && y >= borderY && y < height - borderY)
                continue;
            double z = depth.At<double>(y, x);
            if (!double.IsFinite(z) || z <= 0 || z > 100000) continue;
            result.Add(new SamplePoint((x - cx) * z / fx, (y - cy) * z / fy, z, x, y));
        }
        return result;
    }

    private static (TablePlaneModel? Plane, int Inliers) FitPlane(List<SamplePoint> points)
    {
        Random random = new(0x3DA1);
        TablePlaneModel? best = null;
        List<SamplePoint> bestInliers = [];
        for (int iteration = 0; iteration < RansacIterations; iteration++)
        {
            SamplePoint p0 = points[random.Next(points.Count)];
            SamplePoint p1 = points[random.Next(points.Count)];
            SamplePoint p2 = points[random.Next(points.Count)];
            TablePlaneModel? candidate = PlaneFromThreePoints(p0, p1, p2);
            if (candidate is null) continue;
            List<SamplePoint> inliers = points
                .Where(p => Math.Abs(candidate.SignedDistance(p.X, p.Y, p.Z)) <= RansacDistanceMm)
                .ToList();
            if (inliers.Count > bestInliers.Count)
            {
                best = candidate;
                bestInliers = inliers;
            }
        }
        if (best is null || bestInliers.Count < 3) return (null, 0);
        return (RefinePlane(bestInliers), bestInliers.Count);
    }

    private static TablePlaneModel? RefinePlane(List<SamplePoint> points)
    {
        double cx = points.Average(p => p.X), cy = points.Average(p => p.Y), cz = points.Average(p => p.Z);
        using Mat covariance = Mat.Zeros(3, 3, MatType.CV_64FC1);
        foreach (SamplePoint point in points)
        {
            double x = point.X - cx, y = point.Y - cy, z = point.Z - cz;
            covariance.Set(0, 0, covariance.At<double>(0, 0) + x * x);
            covariance.Set(0, 1, covariance.At<double>(0, 1) + x * y);
            covariance.Set(0, 2, covariance.At<double>(0, 2) + x * z);
            covariance.Set(1, 1, covariance.At<double>(1, 1) + y * y);
            covariance.Set(1, 2, covariance.At<double>(1, 2) + y * z);
            covariance.Set(2, 2, covariance.At<double>(2, 2) + z * z);
        }
        covariance.Set(1, 0, covariance.At<double>(0, 1));
        covariance.Set(2, 0, covariance.At<double>(0, 2));
        covariance.Set(2, 1, covariance.At<double>(1, 2));
        using Mat eigenValues = new();
        using Mat eigenVectors = new();
        if (!Cv2.Eigen(covariance, eigenValues, eigenVectors)) return null;
        double a = eigenVectors.At<double>(2, 0), b = eigenVectors.At<double>(2, 1), c = eigenVectors.At<double>(2, 2);
        return NormalizeTowardCamera(a, b, c, -(a * cx + b * cy + c * cz));
    }

    private static TablePlaneModel? PlaneFromThreePoints(SamplePoint p0, SamplePoint p1, SamplePoint p2)
    {
        double ux = p1.X - p0.X, uy = p1.Y - p0.Y, uz = p1.Z - p0.Z;
        double vx = p2.X - p0.X, vy = p2.Y - p0.Y, vz = p2.Z - p0.Z;
        double a = uy * vz - uz * vy, b = uz * vx - ux * vz, c = ux * vy - uy * vx;
        double norm = Math.Sqrt(a * a + b * b + c * c);
        if (norm < 1e-9) return null;
        return NormalizeTowardCamera(a / norm, b / norm, c / norm,
            -(a * p0.X + b * p0.Y + c * p0.Z) / norm);
    }

    private static TablePlaneModel NormalizeTowardCamera(double a, double b, double c, double d)
        => d >= 0 ? new(a, b, c, d) : new(-a, -b, -c, -d);

    private static int CountCoveredQuadrants(List<SamplePoint> points, TablePlaneModel plane)
    {
        int midX = (points.Min(p => p.PixelX) + points.Max(p => p.PixelX)) / 2;
        int midY = (points.Min(p => p.PixelY) + points.Max(p => p.PixelY)) / 2;
        HashSet<int> quadrants = [];
        foreach (SamplePoint p in points)
            if (Math.Abs(plane.SignedDistance(p.X, p.Y, p.Z)) <= RansacDistanceMm)
                quadrants.Add((p.PixelX >= midX ? 1 : 0) | (p.PixelY >= midY ? 2 : 0));
        return quadrants.Count;
    }

    private static bool HasSufficientSpatialCoverage(
        List<SamplePoint> points,
        TablePlaneModel plane,
        bool usedFullFrameFallback)
    {
        if (CountCoveredQuadrants(points, plane) >= 3) return true;
        if (!usedFullFrameFallback) return false;

        List<SamplePoint> inliers = points
            .Where(p => Math.Abs(plane.SignedDistance(p.X, p.Y, p.Z)) <= RansacDistanceMm)
            .ToList();
        if (inliers.Count == 0) return false;
        int allWidth = Math.Max(1, points.Max(p => p.PixelX) - points.Min(p => p.PixelX));
        int inlierWidth = inliers.Max(p => p.PixelX) - inliers.Min(p => p.PixelX);
        // 实际机台在画面里常表现为横向窄带，无法覆盖三个象限；横跨大部分
        // 有效视场仍足以证明它是台面，而非工件上的局部小平面。
        return inlierWidth / (double)allWidth >= 0.60d;
    }

    private static int FilterDepth(Mat depth, Mat p1, TablePlaneModel plane, double clearance)
    {
        double fx = p1.At<double>(0, 0), fy = p1.At<double>(1, 1);
        double cx = p1.At<double>(0, 2), cy = p1.At<double>(1, 2);
        int rows = depth.Rows, cols = depth.Cols;
        int removed = 0;
        for (int y = 0; y < rows; y++)
        for (int x = 0; x < cols; x++)
        {
            double z = depth.At<double>(y, x);
            if (!double.IsFinite(z) || z <= 0) continue;
            double px = (x - cx) * z / fx, py = (y - cy) * z / fy;
            if (plane.SignedDistance(px, py, z) > clearance) continue;
            depth.Set(y, x, double.NaN);
            removed++;
        }
        return removed;
    }

    private static TablePlaneFilterResult Failed(int candidates, string reason, int inliers = 0)
        => new(false, null, candidates, inliers, 0, reason);

    private readonly record struct SamplePoint(double X, double Y, double Z, int PixelX, int PixelY);
}
