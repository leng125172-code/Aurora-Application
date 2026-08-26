using OpenCvSharp;

namespace AuroraStruct3D.Calibration;

/// <summary>解码 Gray 周期号与相移小数坐标，并按投影仪坐标建立双目对应关系。</summary>
internal static class GrayPhaseStructuredLightDecoder
{
    private const int MinimumGrayContrast = 8;
    private const double MinimumPhaseModulation = 8d;
    private const byte SaturationLevel = 250;
    private const double MaximumRelativePhaseResidual = 0.35d;
    private const double MinimumProjectorXGapLimit = 3d;
    private const double MaximumAdaptiveProjectorXGap = 16d;
    private const double MaximumProjectorYError = 5d;
    private const double MaximumDisparity = 2048d;

    public static GrayPhaseDecodeResult Decode(
        IReadOnlyList<byte[]> images,
        int periodCount,
        int phaseCount,
        int projectorWidth,
        int projectorHeight,
        bool inverted,
        Mat mapX,
        Mat mapY,
        CancellationToken cancellationToken)
    {
        int perDirection = GrayPhasePatternLayout.GetFramesPerDirection(periodCount, phaseCount);
        int expected = perDirection * 2;
        if (images.Count != expected)
            throw new InvalidOperationException($"Gray+相移解码需要 {expected} 帧，实际 {images.Count} 帧");

        int width = mapX.Cols;
        int height = mapX.Rows;
        int pixels = checked(width * height);
        byte[] projectorXValid = Enumerable.Repeat((byte)1, pixels).ToArray();
        byte[] projectorYValid = Enumerable.Repeat((byte)1, pixels).ToArray();
        double[] projectorX = new double[pixels];
        double[] projectorY = new double[pixels];
        DecodeCounters counters = new();
        DecodeDirection(images, 0, periodCount, phaseCount, projectorHeight, inverted,
            projectorY, projectorYValid, mapX, mapY, counters, cancellationToken);
        FillSmallCoordinateHoles(projectorY, projectorYValid, width, height);
        DecodeDirection(images, perDirection, periodCount, phaseCount, projectorWidth, inverted,
            projectorX, projectorXValid, mapX, mapY, counters, cancellationToken);
        return new GrayPhaseDecodeResult(
            width,
            height,
            projectorX,
            projectorY,
            projectorXValid,
            projectorYValid,
            projectorXValid.Count(x => x != 0),
            new GrayPhaseDecodeDiagnostics(counters.Saturated, counters.LowContrast,
                counters.LowModulation, counters.PhaseResidual)
        );
    }

    public static Mat Match(
        GrayPhaseDecodeResult main,
        GrayPhaseDecodeResult secondary,
        int disparitySign,
        out GrayPhaseMatchDiagnostics diagnostics)
    {
        if (main.Width != secondary.Width || main.Height != secondary.Height)
            throw new InvalidOperationException("主从相机结构光解码尺寸不一致");

        double[] values = Enumerable.Repeat(double.NaN, checked(main.Width * main.Height)).ToArray();
        long attempted = 0, matched = 0, rejectedY = 0, rejectedGap = 0, rejectedDisparity = 0;
        for (int y = 0; y < main.Height; y++)
        {
            int row = y * main.Width;
            List<ProjectorSample> samples = [];
            for (int x = 0; x < secondary.Width; x++)
            {
                int i = row + x;
                if (secondary.Valid[i] != 0)
                    samples.Add(new ProjectorSample(
                        secondary.ProjectorX[i],
                        secondary.ProjectorY[i],
                        secondary.ProjectorYValid[i] != 0,
                        x
                    ));
            }
            samples.Sort((a, b) => a.ProjectorX.CompareTo(b.ProjectorX));
            if (samples.Count < 2) continue;
            double maximumProjectorXGap = ComputeAdaptiveProjectorXGap(samples);

            for (int x = 0; x < main.Width; x++)
            {
                int i = row + x;
                if (main.Valid[i] == 0) continue;
                attempted++;
                int upper = LowerBound(samples, main.ProjectorX[i]);
                if (upper <= 0 || upper >= samples.Count) { rejectedGap++; continue; }
                ProjectorSample left = samples[upper - 1];
                ProjectorSample right = samples[upper];
                double gap = right.ProjectorX - left.ProjectorX;
                if (gap <= 1e-6 || gap > maximumProjectorXGap) { rejectedGap++; continue; }
                double t = (main.ProjectorX[i] - left.ProjectorX) / gap;
                double secondaryX = left.CameraX + (right.CameraX - left.CameraX) * t;
                double secondaryY = left.ProjectorY + (right.ProjectorY - left.ProjectorY) * t;
                bool canValidateProjectorY = main.ProjectorYValid[i] != 0
                    && left.ProjectorYValid
                    && right.ProjectorYValid;
                if (!canValidateProjectorY
                    || Math.Abs(main.ProjectorY[i] - secondaryY) > MaximumProjectorYError)
                { rejectedY++; continue; }
                double disparity = x - secondaryX;
                double signed = disparity * disparitySign;
                if (signed <= 0.1 || signed > MaximumDisparity)
                { rejectedDisparity++; continue; }
                values[i] = disparity;
                matched++;
            }
        }

        Mat result = new(main.Height, main.Width, MatType.CV_64FC1);
        System.Runtime.InteropServices.Marshal.Copy(values, 0, result.Data, values.Length);
        diagnostics = new GrayPhaseMatchDiagnostics(attempted, matched, rejectedY, rejectedGap, rejectedDisparity);
        return result;
    }

    private static void DecodeDirection(
        IReadOnlyList<byte[]> images, int offset, int periodCount, int phaseCount,
        int projectorSize, bool inverted, double[] coordinates, byte[] valid,
        Mat mapX, Mat mapY, DecodeCounters counters, CancellationToken cancellationToken)
    {
        int bitCount = GrayPhasePatternLayout.GetGrayBitCount(periodCount);
        int[] gray = new int[valid.Length];
        for (int bit = 0; bit < bitCount; bit++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] normal = ReadRectifiedGray(images[offset + bit * 2], mapX, mapY);
            byte[] inverse = ReadRectifiedGray(images[offset + bit * 2 + 1], mapX, mapY);
            for (int i = 0; i < valid.Length; i++)
            {
                if (valid[i] == 0) continue;
                if (normal[i] >= SaturationLevel || inverse[i] >= SaturationLevel)
                { valid[i] = 0; counters.Saturated++; continue; }
                int difference = normal[i] - inverse[i];
                if (Math.Abs(difference) < MinimumGrayContrast)
                { valid[i] = 0; counters.LowContrast++; continue; }
                bool one = difference > 0;
                if (inverted) one = !one;
                gray[i] = (gray[i] << 1) | (one ? 1 : 0);
            }
        }

        byte[][] phase = new byte[phaseCount][];
        int phaseOffset = offset + bitCount * 2;
        for (int frame = 0; frame < phaseCount; frame++)
            phase[frame] = ReadRectifiedGray(images[phaseOffset + frame], mapX, mapY);

        for (int i = 0; i < valid.Length; i++)
        {
            if (valid[i] == 0) continue;
            int binary = GrayToBinary(gray[i]);
            if (binary < 0 || binary >= periodCount) { valid[i] = 0; continue; }
            double cos = 0, sin = 0;
            bool saturated = false;
            double mean = 0;
            for (int frame = 0; frame < phaseCount; frame++)
            {
                saturated |= phase[frame][i] >= SaturationLevel;
                mean += phase[frame][i];
                double angle = 2d * Math.PI * frame / phaseCount;
                cos += phase[frame][i] * Math.Cos(angle);
                sin += phase[frame][i] * Math.Sin(angle);
            }
            if (saturated) { valid[i] = 0; counters.Saturated++; continue; }
            mean /= phaseCount;
            double modulation = 2d * Math.Sqrt(cos * cos + sin * sin) / phaseCount;
            if (modulation < MinimumPhaseModulation)
            { valid[i] = 0; counters.LowModulation++; continue; }
            double residualSquared = 0;
            double fittedCos = 2d * cos / phaseCount;
            double fittedSin = 2d * sin / phaseCount;
            for (int frame = 0; frame < phaseCount; frame++)
            {
                double angle = 2d * Math.PI * frame / phaseCount;
                double fitted = mean + fittedCos * Math.Cos(angle) + fittedSin * Math.Sin(angle);
                double error = phase[frame][i] - fitted;
                residualSquared += error * error;
            }
            double residual = Math.Sqrt(residualSquared / phaseCount);
            if (residual > 4d && residual / modulation > MaximumRelativePhaseResidual)
            { valid[i] = 0; counters.PhaseResidual++; continue; }
            double wrapped = Math.Atan2(-sin, cos) - (inverted ? Math.PI : 0d);
            wrapped %= 2d * Math.PI;
            if (wrapped < 0) wrapped += 2d * Math.PI;
            coordinates[i] = (binary + wrapped / (2d * Math.PI)) * projectorSize / periodCount;
        }
    }

    private static byte[] ReadRectifiedGray(byte[] bytes, Mat mapX, Mat mapY)
    {
        using Mat source = CalibImageUtils.LoadGrayMat(bytes);
        if (source.Empty()) throw new InvalidOperationException("结构光图像解码失败");
        using Mat rectified = new();
        Cv2.Remap(source, rectified, mapX, mapY, InterpolationFlags.Linear,
            BorderTypes.Constant, Scalar.Black);
        return CalibImageUtils.CopyGrayPixels(rectified);
    }

    private static int GrayToBinary(int gray)
    {
        int binary = gray;
        for (int shifted = gray >> 1; shifted != 0; shifted >>= 1) binary ^= shifted;
        return binary;
    }

    private static void FillSmallCoordinateHoles(
        double[] coordinates,
        byte[] valid,
        int width,
        int height)
    {
        byte[] sourceValid = (byte[])valid.Clone();
        double[] sourceCoordinates = (double[])coordinates.Clone();
        for (int y = 1; y < height - 1; y++)
        for (int x = 1; x < width - 1; x++)
        {
            int index = y * width + x;
            if (sourceValid[index] != 0) continue;
            List<double> neighbors = [];
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int neighbor = (y + dy) * width + x + dx;
                if (sourceValid[neighbor] != 0) neighbors.Add(sourceCoordinates[neighbor]);
            }
            if (neighbors.Count < 3) continue;
            neighbors.Sort();
            if (neighbors[^1] - neighbors[0] > 3d) continue;
            coordinates[index] = neighbors[neighbors.Count / 2];
            valid[index] = 1;
        }
    }

    private static int LowerBound(List<ProjectorSample> samples, double value)
    {
        int low = 0, high = samples.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (samples[middle].ProjectorX < value) low = middle + 1; else high = middle;
        }
        return low;
    }

    private static double ComputeAdaptiveProjectorXGap(List<ProjectorSample> samples)
    {
        // 投影坐标在相机像素中的采样间距由成像倍率决定，不能固定为 3px。
        // 用本行有效样本的中位间距建立局部尺度；忽略重复值和跨遮挡的大跳变。
        List<double> gaps = [];
        for (int i = 1; i < samples.Count; i++)
        {
            double gap = samples[i].ProjectorX - samples[i - 1].ProjectorX;
            if (gap > 1e-6 && gap <= MaximumAdaptiveProjectorXGap)
                gaps.Add(gap);
        }
        if (gaps.Count == 0) return MinimumProjectorXGapLimit;
        gaps.Sort();
        double median = gaps[gaps.Count / 2];
        return Math.Clamp(
            median * 4d,
            MinimumProjectorXGapLimit,
            MaximumAdaptiveProjectorXGap
        );
    }

    private readonly record struct ProjectorSample(
        double ProjectorX,
        double ProjectorY,
        bool ProjectorYValid,
        int CameraX);

    private sealed class DecodeCounters
    {
        public long Saturated;
        public long LowContrast;
        public long LowModulation;
        public long PhaseResidual;
    }
}

internal sealed record GrayPhaseDecodeResult(
    int Width,
    int Height,
    double[] ProjectorX,
    double[] ProjectorY,
    byte[] Valid,
    byte[] ProjectorYValid,
    int ValidCount,
    GrayPhaseDecodeDiagnostics? Diagnostics = null);

internal sealed record GrayPhaseDecodeDiagnostics(
    long SaturatedPixels,
    long LowContrastPixels,
    long LowModulationPixels,
    long PhaseResidualPixels);

internal sealed record GrayPhaseMatchDiagnostics(
    long AttemptedPixels, long MatchedPixels, long ProjectorYRejected,
    long InterpolationGapRejected, long DisparityRejected);
