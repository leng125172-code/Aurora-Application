namespace AuroraStruct3D.Cameras;

/// <summary>白底黑色标定板的灰度图质量评分（1-100）。</summary>
public static class CameraImageQualityCalculator
{
    public readonly record struct Scores(int ExposureScore, int SharpnessScore);

    public static Scores ComputeGray(
        byte[] pixels,
        int width,
        int height,
        int roiX = 0,
        int roiY = 0,
        int roiWidth = 0,
        int roiHeight = 0
    )
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0 || height <= 0 || pixels.Length < width * height)
            throw new ArgumentException("灰度图尺寸或像素数据无效。", nameof(pixels));

        roiX = Math.Clamp(roiX, 0, width - 1);
        roiY = Math.Clamp(roiY, 0, height - 1);
        roiWidth = roiWidth <= 0 ? width - roiX : Math.Min(roiWidth, width - roiX);
        roiHeight = roiHeight <= 0 ? height - roiY : Math.Min(roiHeight, height - roiY);

        const int step = 4;
        int sampledWidth = (roiWidth + step - 1) / step;
        int sampledHeight = (roiHeight + step - 1) / step;
        var gray = new byte[sampledWidth * sampledHeight];
        int count = 0;
        int saturatedCount = 0;
        for (int y = roiY; y < roiY + roiHeight; y += step)
        {
            for (int x = roiX; x < roiX + roiWidth; x += step)
            {
                byte value = pixels[y * width + x];
                gray[count++] = value;
                if (value >= 254)
                    saturatedCount++;
            }
        }

        if (count < 9)
            return new Scores(50, 50);

        int edgeCapacity = Math.Max(0, (sampledWidth - 2) * (sampledHeight - 2));
        var edgeResponses = new List<int>(edgeCapacity);
        for (int y = 1; y < sampledHeight - 1; y++)
        {
            for (int x = 1; x < sampledWidth - 1; x++)
            {
                int index = y * sampledWidth + x;
                int horizontal = Math.Abs(gray[index + 1] - gray[index - 1]);
                int vertical = Math.Abs(
                    gray[index + sampledWidth] - gray[index - sampledWidth]
                );
                edgeResponses.Add(horizontal + vertical);
            }
        }

        Array.Sort(gray, 0, count);
        edgeResponses.Sort();

        double darkLevel = Percentile(gray, count, 0.05);
        double backgroundLevel = Percentile(gray, count, 0.90);
        double contrast = backgroundLevel - darkLevel;

        // 白底允许接近 255，但大量像素完全饱和会丢失板面纹理和圆点边缘细节。
        double backgroundScore = Normalize(backgroundLevel, 160, 200);
        double darkDotScore = 1 - Normalize(darkLevel, 60, 120);
        double contrastScore = Normalize(contrast, 60, 160);
        double exposure =
            (backgroundScore * 0.25 + darkDotScore * 0.30 + contrastScore * 0.45) * 100;
        double saturatedRatio = (double)saturatedCount / count;
        double saturationPenalty = Normalize(saturatedRatio, 0.05, 0.40) * 0.35;
        exposure *= 1 - saturationPenalty;

        // 只看较强边缘的响应，避免大面积白色背景稀释清晰度。
        double edgeStrength = edgeResponses.Count == 0
            ? 0
            : Percentile(edgeResponses, 0.90);
        // 提高满分门槛，避免高黑白反差本身让轻微失焦图片直接达到 100。
        double sharpness = Normalize(edgeStrength, 12, 300) * 100;

        return new Scores(ToDisplayScore(exposure), ToDisplayScore(sharpness));
    }

    private static double Percentile(byte[] sorted, int count, double percentile)
    {
        int index = Math.Clamp((int)Math.Round((count - 1) * percentile), 0, count - 1);
        return sorted[index];
    }

    private static double Percentile(List<int> sorted, double percentile)
    {
        int index = Math.Clamp(
            (int)Math.Round((sorted.Count - 1) * percentile),
            0,
            sorted.Count - 1
        );
        return sorted[index];
    }

    private static double Normalize(double value, double low, double high) =>
        Math.Clamp((value - low) / (high - low), 0, 1);

    private static int ToDisplayScore(double value) =>
        Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 1, 100);
}
