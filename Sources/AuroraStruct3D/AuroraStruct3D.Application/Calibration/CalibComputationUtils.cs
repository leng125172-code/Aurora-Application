using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

namespace AuroraStruct3D.Calibration;

public static class CalibComputationUtils
{
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