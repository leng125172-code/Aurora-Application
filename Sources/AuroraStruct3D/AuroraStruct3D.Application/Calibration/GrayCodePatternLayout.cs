namespace AuroraStruct3D.Calibration;

/// <summary>
/// 固定 1280×720 投影分辨率的多尺度二值条纹帧布局。
/// 每种条纹宽度均按原图、互补图成对排列，先横条纹再竖条纹。
/// </summary>
internal static class GrayCodePatternLayout
{
    public const int ProjectorWidth = 1280;
    public const int ProjectorHeight = 720;
    public static readonly int[] HorizontalStripeWidths = [6, 12, 24, 48];
    public static readonly int[] VerticalStripeWidths = [8, 16, 32, 64];
    public const int HorizontalBitCount = 4;
    public const int VerticalBitCount = 4;
    public const int HorizontalFrameCount = HorizontalBitCount * 2;
    public const int VerticalFrameCount = VerticalBitCount * 2;
    public const int TotalFrameCount = HorizontalFrameCount + VerticalFrameCount;

    public static byte[][] BuildFrames()
    {
        byte[][] frames = new byte[TotalFrameCount][];
        int frameIndex = 0;

        foreach (int stripeWidth in HorizontalStripeWidths)
        {
            byte[] original = BuildStripeLine(ProjectorHeight, stripeWidth);
            frames[frameIndex++] = original;
            frames[frameIndex++] = Invert(original);
        }

        foreach (int stripeWidth in VerticalStripeWidths)
        {
            byte[] original = BuildStripeLine(ProjectorWidth, stripeWidth);
            frames[frameIndex++] = original;
            frames[frameIndex++] = Invert(original);
        }

        return frames;
    }

    public static string GetFrameLabel(int index)
    {
        if (index < HorizontalFrameCount)
        {
            int width = HorizontalStripeWidths[index / 2];
            return $"横条纹 {width}px {(index % 2 == 0 ? "原图" : "互补")}";
        }

        int verticalWidth = VerticalStripeWidths[(index - HorizontalFrameCount) / 2];
        return $"竖条纹 {verticalWidth}px {(index % 2 == 0 ? "原图" : "互补")}";
    }

    private static byte[] BuildStripeLine(int length, int stripeWidth)
    {
        byte[] pixels = new byte[length];
        for (int coordinate = 0; coordinate < length; coordinate++)
        {
            pixels[coordinate] =
                (coordinate / stripeWidth) % 2 == 0 ? (byte)0 : (byte)255;
        }

        return pixels;
    }

    private static byte[] Invert(byte[] source)
    {
        byte[] inverted = new byte[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            inverted[i] = (byte)(255 - source[i]);
        }

        return inverted;
    }
}
