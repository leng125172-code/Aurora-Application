namespace AuroraStruct3D.OpenCV.GeometryOps;

internal static class ChessboardDetectionHelper
{
    public static Mat GetGrayMat(Mat inputMat, out bool needDispose)
    {
        if (inputMat.Channels() > 1)
        {
            Mat gray = new();
            Cv2.CvtColor(
                inputMat,
                gray,
                inputMat.Channels() == 4
                    ? ColorConversionCodes.BGRA2GRAY
                    : ColorConversionCodes.BGR2GRAY
            );
            needDispose = true;
            return gray;
        }

        if (inputMat.Type() != MatType.CV_8UC1)
        {
            Mat gray = new();
            inputMat.ConvertTo(gray, MatType.CV_8UC1);
            needDispose = true;
            return gray;
        }

        needDispose = false;
        return inputMat;
    }

    public static Point2f[]? FindCornersGray(Mat grayFull, Size patternSize, bool refineCorners)
    {
        ChessboardFlags flags1 = ChessboardFlags.AdaptiveThresh | ChessboardFlags.NormalizeImage;
        ChessboardFlags flags2 = flags1 | ChessboardFlags.FilterQuads;
        TermCriteria subPixCriteria = new(CriteriaTypes.Eps | CriteriaTypes.MaxIter, 30, 0.001);

        int halfWidth = Math.Max(1, grayFull.Cols / 2);
        int halfHeight = Math.Max(1, grayFull.Rows / 2);
        using Mat grayHalf = new();
        Cv2.Resize(
            grayFull,
            grayHalf,
            new Size(halfWidth, halfHeight),
            0,
            0,
            InterpolationFlags.Area
        );

        Point2f[]? halfFound = null;
        if (
            Cv2.FindChessboardCorners(grayHalf, patternSize, out Point2f[] corners1, flags1)
            && corners1.Length > 0
        )
        {
            halfFound = corners1;
        }
        else if (
            Cv2.FindChessboardCorners(grayHalf, patternSize, out Point2f[] corners2, flags2)
            && corners2.Length > 0
        )
        {
            halfFound = corners2;
        }
        else if (
            Cv2.FindChessboardCornersSB(grayHalf, patternSize, out Point2f[] corners3)
            && corners3.Length > 0
        )
        {
            halfFound = corners3;
        }

        if (halfFound != null)
        {
            for (int index = 0; index < halfFound.Length; index++)
            {
                halfFound[index] = new Point2f(halfFound[index].X * 2f, halfFound[index].Y * 2f);
            }

            if (refineCorners)
            {
                Cv2.CornerSubPix(
                    grayFull,
                    halfFound,
                    new Size(11, 11),
                    new Size(-1, -1),
                    subPixCriteria
                );
            }

            return halfFound;
        }

        if (
            Cv2.FindChessboardCorners(grayFull, patternSize, out Point2f[] fullCorners1, flags1)
            && fullCorners1.Length > 0
        )
        {
            if (refineCorners)
            {
                Cv2.CornerSubPix(
                    grayFull,
                    fullCorners1,
                    new Size(11, 11),
                    new Size(-1, -1),
                    subPixCriteria
                );
            }

            return fullCorners1;
        }

        if (
            Cv2.FindChessboardCorners(grayFull, patternSize, out Point2f[] fullCorners2, flags2)
            && fullCorners2.Length > 0
        )
        {
            if (refineCorners)
            {
                Cv2.CornerSubPix(
                    grayFull,
                    fullCorners2,
                    new Size(11, 11),
                    new Size(-1, -1),
                    subPixCriteria
                );
            }

            return fullCorners2;
        }

        return null;
    }

    public static Mat CreateOutputMat(Mat inputMat)
    {
        if (inputMat.Channels() == 1)
        {
            Mat output = new();
            Cv2.CvtColor(inputMat, output, ColorConversionCodes.GRAY2BGR);
            return output;
        }

        if (inputMat.Channels() == 4)
        {
            Mat output = new();
            Cv2.CvtColor(inputMat, output, ColorConversionCodes.BGRA2BGR);
            return output;
        }

        return inputMat.Clone();
    }

    public static double ComputeCoverageRatio(Point2f[] corners, Size imageSize)
    {
        if (corners.Length == 0 || imageSize.Width <= 0 || imageSize.Height <= 0)
        {
            return 0d;
        }

        float minX = corners.Min(point => point.X);
        float maxX = corners.Max(point => point.X);
        float minY = corners.Min(point => point.Y);
        float maxY = corners.Max(point => point.Y);

        double width = Math.Max(0d, maxX - minX);
        double height = Math.Max(0d, maxY - minY);
        double area = width * height;
        double imageArea = (double)imageSize.Width * imageSize.Height;
        return imageArea > 0 ? Math.Clamp(area / imageArea, 0d, 1d) : 0d;
    }

    public static double ComputeLaplacianVariance(Mat gray, Point2f[] corners)
    {
        Rect roi = BuildCornerRect(corners, gray.Size());
        using Mat view = roi.Width > 0 && roi.Height > 0 ? new Mat(gray, roi) : gray;
        using Mat laplacian = new();
        Cv2.Laplacian(view, laplacian, MatType.CV_64FC1);
        Cv2.MeanStdDev(laplacian, out _, out Scalar stddev);
        return stddev.Val0 * stddev.Val0;
    }

    private static Rect BuildCornerRect(Point2f[] corners, Size imageSize)
    {
        if (corners.Length == 0)
        {
            return new Rect(0, 0, imageSize.Width, imageSize.Height);
        }

        float minX = corners.Min(point => point.X);
        float maxX = corners.Max(point => point.X);
        float minY = corners.Min(point => point.Y);
        float maxY = corners.Max(point => point.Y);

        int paddingX = Math.Max(8, (int)Math.Round((maxX - minX) * 0.08));
        int paddingY = Math.Max(8, (int)Math.Round((maxY - minY) * 0.08));

        int x = Math.Max(0, (int)Math.Floor(minX) - paddingX);
        int y = Math.Max(0, (int)Math.Floor(minY) - paddingY);
        int right = Math.Min(imageSize.Width, (int)Math.Ceiling(maxX) + paddingX);
        int bottom = Math.Min(imageSize.Height, (int)Math.Ceiling(maxY) + paddingY);

        int width = Math.Max(1, right - x);
        int height = Math.Max(1, bottom - y);
        return new Rect(x, y, width, height);
    }
}
