using OpenCvSharp;

namespace AuroraStruct3D.Calibration;

internal static class ScanFrameExposureAnalyzer
{
    public static ScanFrameExposureMetrics Analyze(byte[] encodedImage)
    {
        using Mat gray = CalibImageUtils.LoadGrayMat(encodedImage);
        byte[] pixels = CalibImageUtils.CopyGrayPixels(gray);
        if (pixels.Length == 0) return default;

        int saturated = 0, crushed = 0;
        int[] histogram = new int[256];
        foreach (byte value in pixels)
        {
            histogram[value]++;
            if (value >= 250) saturated++;
            if (value <= 5) crushed++;
        }

        return new ScanFrameExposureMetrics(
            (double)saturated / pixels.Length,
            (double)crushed / pixels.Length,
            Percentile(histogram, pixels.Length, 0.01),
            Percentile(histogram, pixels.Length, 0.50),
            Percentile(histogram, pixels.Length, 0.99));
    }

    private static byte Percentile(int[] histogram, int count, double percentile)
    {
        int target = Math.Max(1, (int)Math.Ceiling(count * percentile));
        int sum = 0;
        for (int value = 0; value < histogram.Length; value++)
        {
            sum += histogram[value];
            if (sum >= target) return (byte)value;
        }
        return 255;
    }
}

internal readonly record struct ScanFrameExposureMetrics(
    double SaturatedRatio, double CrushedRatio, byte P01, byte P50, byte P99);
