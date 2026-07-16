namespace AuroraStruct3D.OpenCV.ImageOps;

public class ImageDiffResult
{
    public bool IsSignificant { get; init; }
    public double DiffScore { get; init; }
    public int ChangedPixelCount { get; init; }
    public double ChangedPixelRatio { get; init; }
}

public static class ImageDiffAnalyzer
{
    private const double DefaultDiffThreshold = 20.0;
    private const double DefaultSignificantRatio = 0.05;

    public static ImageDiffResult ComputeDiff(byte[] image1Bytes, byte[] image2Bytes)
    {
        return ComputeDiff(image1Bytes, image2Bytes, DefaultDiffThreshold, DefaultSignificantRatio);
    }

    public static ImageDiffResult ComputeDiff(
        byte[] image1Bytes,
        byte[] image2Bytes,
        double diffThreshold,
        double significantRatio
    )
    {
        using Mat mat1 = Cv2.ImDecode(image1Bytes, ImreadModes.Grayscale);
        using Mat mat2 = Cv2.ImDecode(image2Bytes, ImreadModes.Grayscale);

        return ComputeDiff(mat1, mat2, diffThreshold, significantRatio);
    }

    public static ImageDiffResult ComputeDiff(Mat image1, Mat image2)
    {
        return ComputeDiff(image1, image2, DefaultDiffThreshold, DefaultSignificantRatio);
    }

    public static ImageDiffResult ComputeDiff(
        Mat image1,
        Mat image2,
        double diffThreshold,
        double significantRatio
    )
    {
        if (image1.Empty() || image2.Empty())
        {
            return new ImageDiffResult
            {
                IsSignificant = false,
                DiffScore = 0,
                ChangedPixelCount = 0,
                ChangedPixelRatio = 0,
            };
        }

        if (image1.Size() != image2.Size())
        {
            Cv2.Resize(image2, image2, image1.Size());
        }

        Mat gray1 = image1;
        Mat gray2 = image2;
        bool needDispose1 = false;
        bool needDispose2 = false;

        try
        {
            if (image1.Channels() > 1)
            {
                gray1 = new Mat();
                Cv2.CvtColor(image1, gray1, ColorConversionCodes.BGR2GRAY);
                needDispose1 = true;
            }

            if (image2.Channels() > 1)
            {
                gray2 = new Mat();
                Cv2.CvtColor(image2, gray2, ColorConversionCodes.BGR2GRAY);
                needDispose2 = true;
            }

            using Mat diff = new();
            Cv2.Absdiff(gray1, gray2, diff);

            using Mat thresholded = new();
            Cv2.Threshold(diff, thresholded, diffThreshold, 255, ThresholdTypes.Binary);

            int changedPixelCount = Cv2.CountNonZero(thresholded);
            int totalPixels = gray1.Rows * gray1.Cols;

            double changedPixelRatio = totalPixels > 0
                ? (double)changedPixelCount / totalPixels
                : 0;

            Cv2.MeanStdDev(diff, out Scalar mean, out Scalar stddev);
            double diffScore = Math.Min(mean.Val0 / 255.0, 1.0);

            bool isSignificant = changedPixelRatio >= significantRatio;

            return new ImageDiffResult
            {
                IsSignificant = isSignificant,
                DiffScore = diffScore,
                ChangedPixelCount = changedPixelCount,
                ChangedPixelRatio = changedPixelRatio,
            };
        }
        finally
        {
            if (needDispose1)
                gray1.Dispose();
            if (needDispose2)
                gray2.Dispose();
        }
    }
}