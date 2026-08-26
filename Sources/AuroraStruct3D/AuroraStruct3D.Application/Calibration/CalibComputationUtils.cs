using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

namespace AuroraStruct3D.Calibration;

public static class CalibComputationUtils
{
    public const double MinimumRectificationCoveragePercent = 5d;

    /// <summary>
    /// 双目整平时允许 OpenCV 分别平移左右主点，以最大化有效共同视场。
    /// 大夹角/会聚安装下强制 ZeroDisparity 会把两张有效图推向相反方向，
    /// 即使原始图像存在共同视场，生成的映射也可能完全不重叠。
    /// </summary>
    public static void StereoRectifyMaximizeUsefulArea(
        Mat cameraMatrix1,
        Mat distCoeffs1,
        Mat cameraMatrix2,
        Mat distCoeffs2,
        Size imageSize,
        Mat rotation,
        Mat translation,
        Mat rectification1,
        Mat rectification2,
        Mat projection1,
        Mat projection2,
        Mat disparityToDepth)
    {
        Cv2.StereoRectify(
            cameraMatrix1,
            distCoeffs1,
            cameraMatrix2,
            distCoeffs2,
            imageSize,
            rotation,
            translation,
            rectification1,
            rectification2,
            projection1,
            projection2,
            disparityToDepth,
            flags: StereoRectificationFlags.None,
            alpha: -1d,
            newImageSize: imageSize
        );
    }

    public static bool TryValidateCameraModel(
        Mat cameraMatrix,
        Mat distCoeffs,
        Size imageSize,
        out string reason
    )
    {
        reason = string.Empty;
        if (cameraMatrix.Empty() || cameraMatrix.Rows != 3 || cameraMatrix.Cols != 3)
        {
            reason = "内参矩阵尺寸不是 3x3";
            return false;
        }

        double fx = cameraMatrix.At<double>(0, 0);
        double fy = cameraMatrix.At<double>(1, 1);
        double cx = cameraMatrix.At<double>(0, 2);
        double cy = cameraMatrix.At<double>(1, 2);
        double maxDimension = Math.Max(imageSize.Width, imageSize.Height);
        if (!double.IsFinite(fx) || !double.IsFinite(fy) || fx <= 0 || fy <= 0)
        {
            reason = "焦距不是有限正数";
            return false;
        }
        if (fx < maxDimension * 0.25d || fy < maxDimension * 0.25d
            || fx > maxDimension * 10d || fy > maxDimension * 10d)
        {
            reason = $"焦距超出图像尺度合理范围：fx={fx:F3}, fy={fy:F3}";
            return false;
        }
        double aspectRatio = fx / fy;
        if (aspectRatio < 0.5d || aspectRatio > 2d)
        {
            reason = $"焦距纵横比异常：fx/fy={aspectRatio:F3}";
            return false;
        }
        if (!double.IsFinite(cx) || !double.IsFinite(cy)
            || cx < 0 || cx >= imageSize.Width || cy < 0 || cy >= imageSize.Height)
        {
            reason = $"主点位于图像外：cx={cx:F3}, cy={cy:F3}";
            return false;
        }

        double[] limits = [2d, 20d, 0.1d, 0.1d, 100d];
        int coefficientCount = Math.Min((int)distCoeffs.Total(), limits.Length);
        for (int index = 0; index < coefficientCount; index++)
        {
            double value = distCoeffs.At<double>(index);
            if (!double.IsFinite(value) || Math.Abs(value) > limits[index])
            {
                reason = $"畸变系数 d{index} 异常：{value:G6}";
                return false;
            }
        }
        return true;
    }

    public static (double MainPercent, double SecondaryPercent, double OverlapPercent)
        ComputeRectificationMapCoverage(
            Mat map1x,
            Mat map1y,
            Mat map2x,
            Mat map2y,
            int sourceWidth,
            int sourceHeight,
            int sampleStep = 8
        )
    {
        if (map1x.Empty() || map1y.Empty() || map2x.Empty() || map2y.Empty()
            || map1x.Size() != map1y.Size() || map1x.Size() != map2x.Size()
            || map1x.Size() != map2y.Size())
            return (0, 0, 0);
        if (sourceWidth <= 1 || sourceHeight <= 1 || sampleStep <= 0)
            return (0, 0, 0);

        long sampled = 0, mainValid = 0, secondaryValid = 0, overlapValid = 0;
        int rows = map1x.Rows;
        int columns = map1x.Cols;
        for (int y = 0; y < rows; y += sampleStep)
        for (int x = 0; x < columns; x += sampleStep)
        {
            sampled++;
            float mainX = map1x.At<float>(y, x);
            float mainY = map1y.At<float>(y, x);
            float secondaryX = map2x.At<float>(y, x);
            float secondaryY = map2y.At<float>(y, x);
            bool mainInside = float.IsFinite(mainX) && float.IsFinite(mainY)
                && mainX >= 0 && mainX < sourceWidth - 1
                && mainY >= 0 && mainY < sourceHeight - 1;
            bool secondaryInside = float.IsFinite(secondaryX) && float.IsFinite(secondaryY)
                && secondaryX >= 0 && secondaryX < sourceWidth - 1
                && secondaryY >= 0 && secondaryY < sourceHeight - 1;
            if (mainInside) mainValid++;
            if (secondaryInside) secondaryValid++;
            if (mainInside && secondaryInside) overlapValid++;
        }
        return sampled == 0
            ? (0, 0, 0)
            : (mainValid * 100d / sampled, secondaryValid * 100d / sampled,
                overlapValid * 100d / sampled);
    }

    public static double GetMaxSingleCameraReprojectionError(CalibProject project)
    {
        return project.BoardType == CalibrationBoardType.Chessboard
            ? CalibConsts.MaxSingleCameraReprojectionError
            : CalibConsts.MaxCircleBoardReprojectionError;
    }

    public static double GetMaxProjectorReprojectionError(CalibProject project)
    {
        return project.BoardType == CalibrationBoardType.Chessboard
            ? CalibConsts.MaxSingleCameraReprojectionError
            : CalibConsts.MaxCircleBoardReprojectionError;
    }

    public static Point3f[] BuildBoardWorldPoints(
        CalibProject project,
        Size patternSize,
        float spacingMm
    )
    {
        bool asymmetric = project.BoardType == CalibrationBoardType.AsymmetricCircleGrid;
        bool marked = project.BoardType == CalibrationBoardType.MarkedSymmetricCircleGrid;
        int cols = patternSize.Width;
        int rows = patternSize.Height;
        int total = cols * rows - (marked ? 1 : 0);
        Point3f[] points = new Point3f[total];
        int idx = 0;

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                if (marked && r == rows / 2 && c == cols / 2)
                    continue;

                float x = asymmetric && (r % 2 == 1)
                    ? (c + 0.5f) * spacingMm
                    : c * spacingMm;
                float y = r * spacingMm;
                points[idx++] = new Point3f(x, y, 0);
            }
        }

        return points;
    }

    public static double ComputeReprojectionError(
        Point2f[] imagePoints,
        Point2f[] projectedPoints
    )
    {
        if (imagePoints.Length != projectedPoints.Length)
            throw new ArgumentException("图像点和投影点数量不一致");

        double totalError = 0;
        for (int i = 0; i < imagePoints.Length; i++)
        {
            double dx = imagePoints[i].X - projectedPoints[i].X;
            double dy = imagePoints[i].Y - projectedPoints[i].Y;
            totalError += Math.Sqrt(dx * dx + dy * dy);
        }

        return imagePoints.Length > 0 ? totalError / imagePoints.Length : 0;
    }

    public static (double avgError, double maxError, double minError) ComputeReprojectionErrorStats(
        Point2f[] imagePoints,
        Point2f[] projectedPoints
    )
    {
        if (imagePoints.Length != projectedPoints.Length)
            throw new ArgumentException("图像点和投影点数量不一致");

        double sumErr = 0;
        double maxErr = 0;
        double minErr = double.MaxValue;

        for (int i = 0; i < imagePoints.Length; i++)
        {
            double dx = imagePoints[i].X - projectedPoints[i].X;
            double dy = imagePoints[i].Y - projectedPoints[i].Y;
            double dist = Math.Sqrt(dx * dx + dy * dy);
            sumErr += dist;
            maxErr = Math.Max(maxErr, dist);
            minErr = Math.Min(minErr, dist);
        }

        double avgErr = imagePoints.Length > 0 ? sumErr / imagePoints.Length : 0;
        return (avgErr, maxErr, minErr);
    }

    public static (Mat R_avg, Mat t_avg) ComputeAverageExtrinsics(Mat[] rvecs, Mat[] tvecs)
    {
        if (rvecs.Length == 0 || rvecs.Length != tvecs.Length)
            throw new ArgumentException("外参数组无效");

        int nPoses = rvecs.Length;
        double[] sumR = new double[9];
        double[] sumT = new double[3];

        for (int pi = 0; pi < nPoses; pi++)
        {
            using Mat R_proj_i = new();
            Cv2.Rodrigues(rvecs[pi], R_proj_i);
            for (int rr = 0; rr < 3; rr++)
            {
                for (int cc = 0; cc < 3; cc++)
                    sumR[rr * 3 + cc] += R_proj_i.At<double>(rr, cc);
                sumT[rr] += tvecs[pi].At<double>(rr, 0);
            }
        }

        using Mat R_cp_avg = new(3, 3, MatType.CV_64FC1);
        for (int rr = 0; rr < 3; rr++)
        for (int cc = 0; cc < 3; cc++)
            R_cp_avg.Set(rr, cc, sumR[rr * 3 + cc] / nPoses);

        using Mat U_svd = new(), S_svd = new(), Vt_svd = new();
        Cv2.SVDecomp(R_cp_avg, S_svd, U_svd, Vt_svd, SVD.Flags.FullUV);
        Mat R_cp_ortho = U_svd * Vt_svd;

        Mat t_cp_avg = new(3, 1, MatType.CV_64FC1);
        for (int rr = 0; rr < 3; rr++)
            t_cp_avg.Set(rr, 0, sumT[rr] / nPoses);

        return (R_cp_ortho, t_cp_avg);
    }

    public static Mat CreateInitialProjectorIntrinsic(Size projectorSize)
    {
        Mat projKMat = new(3, 3, MatType.CV_64FC1);
        projKMat.SetTo(Scalar.All(0));
        projKMat.Set(0, 0, projectorSize.Width / 2.0);
        projKMat.Set(1, 1, projectorSize.Height / 2.0);
        projKMat.Set(0, 2, projectorSize.Width / 2.0);
        projKMat.Set(1, 2, projectorSize.Height / 2.0);
        projKMat.Set(2, 2, 1.0);
        return projKMat;
    }

    public static Point2f[] CreateProjectorPixelPoints(
        Size projPatternSize,
        int projPx
    )
    {
        int projCols = projPatternSize.Width;
        int projRows = projPatternSize.Height;
        Point2f[] projPixels = new Point2f[projCols * projRows];
        for (int r = 0; r < projRows; r++)
        for (int c = 0; c < projCols; c++)
            projPixels[r * projCols + c] = new Point2f((c + 1) * projPx, (r + 1) * projPx);
        return projPixels;
    }

    public static Size ComputeProjectorSize(
        Size projPatternSize,
        int projPx
    )
    {
        return new Size(
            (projPatternSize.Width + 2) * projPx,
            (projPatternSize.Height + 2) * projPx
        );
    }
}
