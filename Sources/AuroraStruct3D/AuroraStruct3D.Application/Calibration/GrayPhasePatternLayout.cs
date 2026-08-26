namespace AuroraStruct3D.Calibration;

/// <summary>Gray 周期编码 + 正弦相移的统一图案布局。</summary>
public static class GrayPhasePatternLayout
{
    public const string EncodingName = "GrayPhaseV1";
    public const byte DefaultDarkLevel = 24;
    public const byte DefaultBrightLevel = 220;
    public const byte DarkLevel = DefaultDarkLevel;
    public const byte BrightLevel = DefaultBrightLevel;

    public static int GetGrayBitCount(int periodCount)
    {
        if (periodCount <= 0) throw new ArgumentOutOfRangeException(nameof(periodCount));
        int bits = 0;
        for (int values = 1; values < periodCount; values <<= 1) bits++;
        return Math.Max(bits, 1);
    }

    public static int GetFramesPerDirection(int periodCount, int phaseCount) =>
        checked(GetGrayBitCount(periodCount) * 2 + phaseCount);

    public static int GetTotalFrameCount(int periodCount, int phaseCount) =>
        checked(GetFramesPerDirection(periodCount, phaseCount) * 2);

    public static byte[][] BuildFrames(
        int width,
        int height,
        int periodCount,
        int phaseCount,
        bool inverted,
        byte darkLevel = DefaultDarkLevel,
        byte brightLevel = DefaultBrightLevel)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (phaseCount < 3) throw new ArgumentOutOfRangeException(nameof(phaseCount));
        if (darkLevel >= brightLevel)
            throw new ArgumentException("暗部灰阶必须小于亮部灰阶");

        int perDirection = GetFramesPerDirection(periodCount, phaseCount);
        byte[][] frames = new byte[checked(perDirection * 2)][];
        BuildDirection(frames, 0, height, periodCount, phaseCount, inverted, darkLevel, brightLevel);
        BuildDirection(frames, perDirection, width, periodCount, phaseCount, inverted, darkLevel, brightLevel);
        return frames;
    }

    public static string GetFrameLabel(int index, int periodCount, int phaseCount)
    {
        int perDirection = GetFramesPerDirection(periodCount, phaseCount);
        string direction = index < perDirection ? "横条纹" : "竖条纹";
        int local = index % perDirection;
        int grayFrames = GetGrayBitCount(periodCount) * 2;
        return local < grayFrames
            ? $"{direction} Gray B{local / 2 + 1} {(local % 2 == 0 ? "正" : "反")}"
            : $"{direction} 相移 {local - grayFrames + 1}/{phaseCount}";
    }

    private static void BuildDirection(
        byte[][] frames,
        int frameOffset,
        int pixelCount,
        int periodCount,
        int phaseCount,
        bool inverted,
        byte darkLevel,
        byte brightLevel)
    {
        int bitCount = GetGrayBitCount(periodCount);
        for (int bit = 0; bit < bitCount; bit++)
        {
            byte[] normal = new byte[pixelCount];
            byte[] inverse = new byte[pixelCount];
            int shift = bitCount - bit - 1;
            for (int p = 0; p < pixelCount; p++)
            {
                int period = Math.Min(periodCount - 1, (int)((long)p * periodCount / pixelCount));
                int gray = period ^ (period >> 1);
                bool on = ((gray >> shift) & 1) != 0;
                if (inverted) on = !on;
                normal[p] = on ? brightLevel : darkLevel;
                inverse[p] = on ? darkLevel : brightLevel;
            }
            frames[frameOffset + bit * 2] = normal;
            frames[frameOffset + bit * 2 + 1] = inverse;
        }

        int phaseOffset = frameOffset + bitCount * 2;
        double polarityPhase = inverted ? Math.PI : 0d;
        for (int frame = 0; frame < phaseCount; frame++)
        {
            byte[] pixels = new byte[pixelCount];
            double temporalPhase = 2d * Math.PI * frame / phaseCount;
            for (int p = 0; p < pixelCount; p++)
            {
                double spatialPhase = 2d * Math.PI * periodCount * p / pixelCount;
                double center = (brightLevel + darkLevel) / 2d;
                double amplitude = (brightLevel - darkLevel) / 2d;
                double value = center + amplitude * Math.Cos(
                    spatialPhase + temporalPhase + polarityPhase
                );
                pixels[p] = (byte)Math.Clamp(
                    Math.Round(value),
                    darkLevel,
                    brightLevel
                );
            }
            frames[phaseOffset + frame] = pixels;
        }
    }
}
