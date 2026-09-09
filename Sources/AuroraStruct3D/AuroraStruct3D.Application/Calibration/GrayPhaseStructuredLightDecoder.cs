using OpenCvSharp;

namespace AuroraStruct3D.Calibration;

/// <summary>解码 Gray 周期号与相移小数坐标，并按投影仪坐标建立双目对应关系。</summary>
internal static class GrayPhaseStructuredLightDecoder
{
    // 暗色金属表面的有效调制度常落在 6~8 灰阶。保留较低门限获取细节，
    // 周期错码由后续双向一致性和竖向薄片过滤处理。
    private const int MinimumGrayContrast = 6;
    private const double MinimumPhaseModulation = 6d;
    private const byte SaturationLevel = 250;
    private const double MaximumRelativePhaseResidual = 0.45d;
    private const double MinimumProjectorXGapLimit = 3d;
    private const double MaximumAdaptiveProjectorXGap = 16d;
    private const double MaximumProjectorYError = 5d;
    private const int VerticalCorrespondenceSearchRadius = 2;
    private const double MaximumDisparity = 2048d;
    private const double MaximumBidirectionalError = 2d;
    private const int VerticalStripeFilterRadius = 12;
    private const int MinimumSideSupport = 2;
    private const double MinimumNeighborAgreement = 1.5d;
    private const double MinimumOutlierDifference = 8d;
    private const int VerticalSupportRadius = 32;
    private const int MinimumVerticalSupport = 33;

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
        FillSmallCoordinateHoles(projectorX, projectorXValid, width, height);
        return new GrayPhaseDecodeResult(
            width,
            height,
            projectorX,
            projectorY,
            projectorXValid,
            projectorYValid,
            projectorXValid.Count(x => x != 0),
            new GrayPhaseDecodeDiagnostics(counters.Saturated, counters.LowContrast,
                counters.LowModulation, counters.PhaseResidual, counters.GrayRecovered)
        );
    }

    public static Mat Match(
        GrayPhaseDecodeResult main,
        GrayPhaseDecodeResult secondary,
        int disparitySign,
        double principalPointDifference,
        out GrayPhaseMatchDiagnostics diagnostics)
    {
        Mat disparity = Match(
            main,
            secondary,
            disparitySign,
            principalPointDifference,
            out _,
            out diagnostics
        );
        return disparity;
    }

    public static Mat Match(
        GrayPhaseDecodeResult main,
        GrayPhaseDecodeResult secondary,
        int disparitySign,
        double principalPointDifference,
        out double[] secondaryYCoordinates,
        out GrayPhaseMatchDiagnostics diagnostics)
    {
        if (main.Width != secondary.Width || main.Height != secondary.Height)
            throw new InvalidOperationException("主从相机结构光解码尺寸不一致");

        OneWayMatchResult forward = MatchOneWay(
            main,
            secondary,
            disparitySign,
            principalPointDifference
        );
        OneWayMatchResult reverse = MatchOneWay(
            secondary,
            main,
            -disparitySign,
            -principalPointDifference
        );

        long bidirectionalRejected = ApplyBidirectionalConsistency(
            forward.Values,
            forward.SecondaryY,
            reverse.Values,
            reverse.SecondaryY,
            main.Width,
            main.Height
        );
        long verticalStripeRejected = RemoveNarrowVerticalDisparityOutliers(
            forward.Values,
            main.Width,
            main.Height,
            disparitySign,
            principalPointDifference
        );

        Mat result = new(main.Height, main.Width, MatType.CV_64FC1);
        System.Runtime.InteropServices.Marshal.Copy(
            forward.Values,
            0,
            result.Data,
            forward.Values.Length
        );
        for (int i = 0; i < forward.Values.Length; i++)
        {
            if (!double.IsFinite(forward.Values[i]))
                forward.SecondaryY[i] = double.NaN;
        }
        secondaryYCoordinates = forward.SecondaryY;
        diagnostics = new GrayPhaseMatchDiagnostics(
            forward.AttemptedPixels,
            forward.MatchedPixels - bidirectionalRejected - verticalStripeRejected,
            forward.ProjectorYRejected,
            forward.InterpolationGapRejected,
            forward.DisparityRejected,
            bidirectionalRejected,
            verticalStripeRejected
        );
        return result;
    }

    private static OneWayMatchResult MatchOneWay(
        GrayPhaseDecodeResult main,
        GrayPhaseDecodeResult secondary,
        int disparitySign,
        double principalPointDifference)
    {
        double[] values = Enumerable.Repeat(double.NaN, checked(main.Width * main.Height)).ToArray();
        double[] secondaryYCoordinates = Enumerable.Repeat(
            double.NaN,
            checked(main.Width * main.Height)
        ).ToArray();
        long attempted = 0, matched = 0, rejectedY = 0, rejectedGap = 0, rejectedDisparity = 0;
        List<ProjectorSample>[] samplesByRow = new List<ProjectorSample>[secondary.Height];
        double[] maximumGaps = new double[secondary.Height];
        for (int secondaryRow = 0; secondaryRow < secondary.Height; secondaryRow++)
        {
            List<ProjectorSample> samples = [];
            for (int x = 0; x < secondary.Width; x++)
            {
                int i = secondaryRow * secondary.Width + x;
                if (secondary.Valid[i] != 0)
                    samples.Add(new ProjectorSample(
                        secondary.ProjectorX[i],
                        secondary.ProjectorY[i],
                        secondary.ProjectorYValid[i] != 0,
                        x
                    ));
            }
            samples.Sort((a, b) => a.ProjectorX.CompareTo(b.ProjectorX));
            samplesByRow[secondaryRow] = samples;
            maximumGaps[secondaryRow] = samples.Count >= 2
                ? ComputeAdaptiveProjectorXGap(samples)
                : 0d;
        }

        for (int y = 0; y < main.Height; y++)
        {
            int row = y * main.Width;
            Span<RowCorrespondence> candidates = stackalloc RowCorrespondence[
                VerticalCorrespondenceSearchRadius * 2 + 1
            ];
            for (int x = 0; x < main.Width; x++)
            {
                int i = row + x;
                if (main.Valid[i] == 0) continue;
                attempted++;
                int candidateCount = 0;
                int firstRow = Math.Max(0, y - VerticalCorrespondenceSearchRadius);
                int lastRow = Math.Min(main.Height - 1, y + VerticalCorrespondenceSearchRadius);
                for (int candidateRow = firstRow; candidateRow <= lastRow; candidateRow++)
                {
                    if (TryInterpolateRow(
                            samplesByRow[candidateRow],
                            maximumGaps[candidateRow],
                            main.ProjectorX[i],
                            candidateRow,
                            out RowCorrespondence candidate
                        ))
                        candidates[candidateCount++] = candidate;
                }
                if (candidateCount == 0) { rejectedGap++; continue; }
                if (!TrySelectCorrespondence(
                        candidates[..candidateCount],
                        y,
                        main.ProjectorY[i],
                        main.ProjectorYValid[i] != 0,
                        out RowCorrespondence correspondence
                    ))
                { rejectedY++; continue; }

                double disparity = x - correspondence.CameraX;
                // 非 ZeroDisparity 整平时 P1.Cx 与 P2.Cx 可能相差很大。此时用于判断
                // 方向和范围的是几何视差 d-(cx1-cx2)，但深度计算仍需要保留原始 d。
                double signedGeometricDisparity =
                    (disparity - principalPointDifference) * disparitySign;
                if (signedGeometricDisparity <= 0.1
                    || signedGeometricDisparity > MaximumDisparity)
                { rejectedDisparity++; continue; }
                values[i] = disparity;
                secondaryYCoordinates[i] = correspondence.CameraY;
                matched++;
            }
        }

        return new OneWayMatchResult(
            values,
            secondaryYCoordinates,
            attempted,
            matched,
            rejectedY,
            rejectedGap,
            rejectedDisparity
        );
    }

    private static bool TryInterpolateRow(
        List<ProjectorSample> samples,
        double maximumProjectorXGap,
        double projectorX,
        int cameraY,
        out RowCorrespondence correspondence)
    {
        correspondence = default;
        if (samples.Count < 2) return false;
        int upper = LowerBound(samples, projectorX);
        if (upper <= 0 || upper >= samples.Count) return false;
        ProjectorSample left = samples[upper - 1];
        ProjectorSample right = samples[upper];
        double gap = right.ProjectorX - left.ProjectorX;
        if (gap <= 1e-6 || gap > maximumProjectorXGap) return false;
        double t = (projectorX - left.ProjectorX) / gap;
        correspondence = new RowCorrespondence(
            left.CameraX + (right.CameraX - left.CameraX) * t,
            cameraY,
            left.ProjectorY + (right.ProjectorY - left.ProjectorY) * t,
            left.ProjectorYValid && right.ProjectorYValid
        );
        return true;
    }

    private static bool TrySelectCorrespondence(
        ReadOnlySpan<RowCorrespondence> candidates,
        int mainY,
        double mainProjectorY,
        bool mainProjectorYValid,
        out RowCorrespondence correspondence)
    {
        correspondence = default;
        int sameRow = -1;
        for (int i = 0; i < candidates.Length; i++)
        {
            if (Math.Abs(candidates[i].CameraY - mainY) < 0.1d)
            {
                sameRow = i;
                break;
            }
        }

        if (!mainProjectorYValid)
        {
            if (sameRow < 0) return false;
            correspondence = candidates[sameRow];
            return true;
        }

        int nearest = -1;
        double nearestError = double.PositiveInfinity;
        for (int i = 0; i < candidates.Length; i++)
        {
            if (!candidates[i].ProjectorYValid) continue;
            double error = Math.Abs(candidates[i].ProjectorY - mainProjectorY);
            if (error < nearestError)
            {
                nearest = i;
                nearestError = error;
            }
            if (i == 0 || !candidates[i - 1].ProjectorYValid) continue;
            RowCorrespondence first = candidates[i - 1];
            RowCorrespondence second = candidates[i];
            double firstError = first.ProjectorY - mainProjectorY;
            double secondError = second.ProjectorY - mainProjectorY;
            if (firstError * secondError > 0d
                || Math.Abs(second.ProjectorY - first.ProjectorY) < 1e-9)
                continue;
            double t = -firstError / (secondError - firstError);
            correspondence = new RowCorrespondence(
                first.CameraX + (second.CameraX - first.CameraX) * t,
                first.CameraY + (second.CameraY - first.CameraY) * t,
                mainProjectorY,
                true
            );
            return true;
        }

        if (nearest >= 0 && nearestError <= MaximumProjectorYError)
        {
            correspondence = candidates[nearest];
            return true;
        }
        // Preserve the former X-only fallback where the secondary Y pattern is unavailable.
        if (nearest < 0 && sameRow >= 0)
        {
            correspondence = candidates[sameRow];
            return true;
        }
        return false;
    }

    private static long ApplyBidirectionalConsistency(
        double[] forward,
        double[] forwardSecondaryY,
        double[] reverse,
        double[] reverseSecondaryY,
        int width,
        int height)
    {
        long rejected = 0;
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                int index = row + x;
                double disparity = forward[index];
                if (!double.IsFinite(disparity)) continue;

                double secondaryX = x - disparity;
                double secondaryY = forwardSecondaryY[index];
                // 反向结果在暗部、遮挡边界和细小结构上可能为空。缺失不等于错误，
                // 只有双向结果同时存在且明确矛盾时才剔除。
                if (TrySampleField(
                        reverse,
                        width,
                        height,
                        secondaryX,
                        secondaryY,
                        out double reverseDisparity
                    ))
                {
                    double returnedX = secondaryX - reverseDisparity;
                    bool inconsistent = Math.Abs(returnedX - x) > MaximumBidirectionalError;
                    if (TrySampleField(
                            reverseSecondaryY,
                            width,
                            height,
                            secondaryX,
                            secondaryY,
                            out double returnedY
                        ))
                        inconsistent |= Math.Abs(returnedY - y) > MaximumBidirectionalError;
                    if (inconsistent)
                    {
                        forward[index] = double.NaN;
                        forwardSecondaryY[index] = double.NaN;
                        rejected++;
                    }
                }
            }
        }
        return rejected;
    }

    private static bool TrySampleField(
        double[] values,
        int width,
        int height,
        double x,
        double y,
        out double value)
    {
        value = double.NaN;
        if (!double.IsFinite(x) || !double.IsFinite(y)
            || x < 0d || x > width - 1d || y < 0d || y > height - 1d)
            return false;

        int left = (int)Math.Floor(x);
        int right = (int)Math.Ceiling(x);
        int upper = (int)Math.Floor(y);
        int lower = (int)Math.Ceiling(y);
        double upperLeft = values[upper * width + left];
        double upperRight = values[upper * width + right];
        double lowerLeft = values[lower * width + left];
        double lowerRight = values[lower * width + right];
        if (double.IsFinite(upperLeft) && double.IsFinite(upperRight)
            && double.IsFinite(lowerLeft) && double.IsFinite(lowerRight))
        {
            double top = upperLeft + (upperRight - upperLeft) * (x - left);
            double bottom = lowerLeft + (lowerRight - lowerLeft) * (x - left);
            value = top + (bottom - top) * (y - upper);
            return true;
        }

        int nearest = -1;
        double nearestDistance = double.PositiveInfinity;
        int roundedX = (int)Math.Round(x);
        int roundedY = (int)Math.Round(y);
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int candidateX = roundedX + dx;
            int candidateY = roundedY + dy;
            if (candidateX < 0 || candidateX >= width
                || candidateY < 0 || candidateY >= height)
                continue;
            int candidate = candidateY * width + candidateX;
            double candidateValue = values[candidate];
            double distance = Math.Sqrt(
                Math.Pow(candidateX - x, 2d) + Math.Pow(candidateY - y, 2d)
            );
            if (!double.IsFinite(candidateValue) || distance >= nearestDistance) continue;
            nearest = candidate;
            nearestDistance = distance;
        }
        if (nearest < 0 || nearestDistance > 1d) return false;
        value = values[nearest];
        return true;
    }

    internal static long RemoveNarrowVerticalDisparityOutliers(
        double[] disparities,
        int width,
        int height,
        int disparitySign,
        double principalPointDifference)
    {
        if (disparities.Length != checked(width * height))
            throw new ArgumentException("视差数组尺寸与图像尺寸不一致", nameof(disparities));

        bool[] candidates = new bool[disparities.Length];
        List<double> leftValues = new(VerticalStripeFilterRadius);
        List<double> rightValues = new(VerticalStripeFilterRadius);
        for (int y = 0; y < height; y++)
        {
            int row = y * width;
            for (int x = 1; x < width - 1; x++)
            {
                int index = row + x;
                double center = ToGeometricDisparity(disparities[index]);
                if (!double.IsFinite(center)) continue;

                leftValues.Clear();
                rightValues.Clear();
                for (int offset = 1; offset <= VerticalStripeFilterRadius; offset++)
                {
                    if (x - offset >= 0)
                    {
                        double value = ToGeometricDisparity(disparities[row + x - offset]);
                        if (double.IsFinite(value)) leftValues.Add(value);
                    }
                    if (x + offset < width)
                    {
                        double value = ToGeometricDisparity(disparities[row + x + offset]);
                        if (double.IsFinite(value)) rightValues.Add(value);
                    }
                }
                if (leftValues.Count < MinimumSideSupport
                    || rightValues.Count < MinimumSideSupport)
                    continue;

                double leftMedian = Median(leftValues);
                double rightMedian = Median(rightValues);
                double reference = (leftMedian + rightMedian) / 2d;
                double agreementThreshold = Math.Max(
                    MinimumNeighborAgreement,
                    Math.Abs(reference) * 0.02d
                );
                if (Math.Abs(leftMedian - rightMedian) > agreementThreshold)
                    continue;

                double outlierThreshold = Math.Max(
                    MinimumOutlierDifference,
                    Math.Abs(reference) * 0.12d
                );
                // 用户现场的伪点位于金属板背后，对应更小的几何视差。这里只处理
                // “向远处飞”的窄带，避免删除凸起、螺钉等真实的近景细节。
                if (center < leftMedian - outlierThreshold
                    && center < rightMedian - outlierThreshold)
                    candidates[index] = true;
            }
        }

        List<int> rejected = [];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int index = y * width + x;
            if (!candidates[index]) continue;

            int supportingRows = 0;
            int y0 = Math.Max(0, y - VerticalSupportRadius);
            int y1 = Math.Min(height - 1, y + VerticalSupportRadius);
            for (int neighborY = y0; neighborY <= y1; neighborY++)
            {
                int neighborRow = neighborY * width;
                bool rowHasSupport = false;
                for (int neighborX = Math.Max(0, x - 1);
                     neighborX <= Math.Min(width - 1, x + 1);
                     neighborX++)
                {
                    if (!candidates[neighborRow + neighborX]) continue;
                    rowHasSupport = true;
                    break;
                }
                if (rowHasSupport) supportingRows++;
            }
            if (supportingRows >= MinimumVerticalSupport)
                rejected.Add(index);
        }

        foreach (int index in rejected)
            disparities[index] = double.NaN;
        return rejected.Count;

        double ToGeometricDisparity(double value) =>
            double.IsFinite(value)
                ? (value - principalPointDifference) * disparitySign
                : double.NaN;

        static double Median(List<double> values)
        {
            values.Sort();
            int middle = values.Count / 2;
            return values.Count % 2 == 0
                ? (values[middle - 1] + values[middle]) / 2d
                : values[middle];
        }
    }

    private static void DecodeDirection(
        IReadOnlyList<byte[]> images, int offset, int periodCount, int phaseCount,
        int projectorSize, bool inverted, double[] coordinates, byte[] valid,
        Mat mapX, Mat mapY, DecodeCounters counters, CancellationToken cancellationToken)
    {
        int bitCount = GrayPhasePatternLayout.GetGrayBitCount(periodCount);
        int[] gray = new int[valid.Length];
        byte[] grayValid = (byte[])valid.Clone();
        for (int bit = 0; bit < bitCount; bit++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            float[] normal = ReadRectifiedGray(images[offset + bit * 2], mapX, mapY);
            float[] inverse = ReadRectifiedGray(images[offset + bit * 2 + 1], mapX, mapY);
            for (int i = 0; i < grayValid.Length; i++)
            {
                if (grayValid[i] == 0) continue;
                // 正反 Gray 帧只有同时削顶才无法判断。单帧饱和时差值方向仍然可靠，
                // 直接作废会在喷漆金属高光区域产生大面积孔洞。
                if (normal[i] >= SaturationLevel && inverse[i] >= SaturationLevel)
                { grayValid[i] = 0; counters.Saturated++; continue; }
                double difference = normal[i] - inverse[i];
                if (Math.Abs(difference) < MinimumGrayContrast)
                { grayValid[i] = 0; counters.LowContrast++; continue; }
                bool one = difference > 0;
                if (inverted) one = !one;
                gray[i] = (gray[i] << 1) | (one ? 1 : 0);
            }
        }

        float[][] phase = new float[phaseCount][];
        int phaseOffset = offset + bitCount * 2;
        for (int frame = 0; frame < phaseCount; frame++)
            phase[frame] = ReadRectifiedGray(images[phaseOffset + frame], mapX, mapY);

        byte[] phaseValid = new byte[valid.Length];
        double[] phaseFractions = new double[valid.Length];
        Array.Clear(valid, 0, valid.Length);
        for (int i = 0; i < valid.Length; i++)
        {
            if (!TryFitPhase(
                    phase,
                    i,
                    out double fittedCos,
                    out double fittedSin,
                    out double modulation,
                    out double residual
                ))
            {
                if (grayValid[i] != 0) counters.Saturated++;
                continue;
            }
            if (modulation < MinimumPhaseModulation)
            {
                if (grayValid[i] != 0) counters.LowModulation++;
                continue;
            }
            if (residual > 4d && residual / modulation > MaximumRelativePhaseResidual)
            {
                if (grayValid[i] != 0) counters.PhaseResidual++;
                continue;
            }
            double wrapped = Math.Atan2(-fittedSin, fittedCos)
                - (inverted ? Math.PI : 0d);
            wrapped %= 2d * Math.PI;
            if (wrapped < 0) wrapped += 2d * Math.PI;
            double phaseFraction = wrapped / (2d * Math.PI);
            phaseFractions[i] = phaseFraction;
            phaseValid[i] = 1;
            if (grayValid[i] == 0) continue;

            int binary = GrayToBinary(gray[i]);
            if (binary < 0 || binary >= periodCount) continue;
            coordinates[i] = (binary + phaseFraction) * projectorSize / periodCount;
            valid[i] = 1;
        }

        counters.GrayRecovered += RecoverGrayInvalidCoordinates(
            coordinates,
            valid,
            grayValid,
            phaseFractions,
            phaseValid,
            mapX.Cols,
            mapX.Rows,
            projectorSize,
            periodCount
        );
    }

    /// <summary>
    /// Recovers an isolated Gray-code failure only when its fitted phase selects a period
    /// consistent with four independent, opposing-neighbor center predictions.
    /// </summary>
    internal static int RecoverGrayInvalidCoordinates(
        double[] coordinates,
        byte[] valid,
        byte[] grayValid,
        double[] phaseFractions,
        byte[] phaseValid,
        int width,
        int height,
        int projectorSize,
        int periodCount)
    {
        if (coordinates.Length != valid.Length
            || grayValid.Length != valid.Length
            || phaseFractions.Length != valid.Length
            || phaseValid.Length != valid.Length
            || valid.Length != checked(width * height))
            throw new ArgumentException("Coordinate recovery buffers must have matching dimensions.");
        if (periodCount <= 0 || projectorSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(periodCount));

        byte[] sourceValid = (byte[])valid.Clone();
        double[] sourceCoordinates = (double[])coordinates.Clone();
        double periodSize = projectorSize / (double)periodCount;
        int recovered = 0;
        Span<double> centerPredictions = stackalloc double[4];
        for (int y = 1; y < height - 1; y++)
        for (int x = 1; x < width - 1; x++)
        {
            int index = y * width + x;
            if (sourceValid[index] != 0 || grayValid[index] != 0 || phaseValid[index] == 0)
                continue;

            int upperLeft = index - width - 1;
            int upper = index - width;
            int upperRight = index - width + 1;
            int left = index - 1;
            int right = index + 1;
            int lowerLeft = index + width - 1;
            int lower = index + width;
            int lowerRight = index + width + 1;
            if (sourceValid[upperLeft] == 0 || sourceValid[upper] == 0
                || sourceValid[upperRight] == 0 || sourceValid[left] == 0
                || sourceValid[right] == 0 || sourceValid[lowerLeft] == 0
                || sourceValid[lower] == 0 || sourceValid[lowerRight] == 0)
                continue;

            centerPredictions[0] = (sourceCoordinates[left] + sourceCoordinates[right]) / 2d;
            centerPredictions[1] = (sourceCoordinates[upper] + sourceCoordinates[lower]) / 2d;
            centerPredictions[2] = (sourceCoordinates[upperLeft] + sourceCoordinates[lowerRight]) / 2d;
            centerPredictions[3] = (sourceCoordinates[upperRight] + sourceCoordinates[lowerLeft]) / 2d;
            centerPredictions.Sort();
            double predictionSpread = centerPredictions[3] - centerPredictions[0];
            if (!double.IsFinite(predictionSpread) || predictionSpread > 1.5d)
                continue;

            double predictedCenter = (centerPredictions[1] + centerPredictions[2]) / 2d;
            double fraction = phaseFractions[index];
            int period = (int)Math.Round(predictedCenter / periodSize - fraction);
            if (period < 0 || period >= periodCount)
                continue;
            double candidate = (period + fraction) * periodSize;
            double maximumCenterError = Math.Min(periodSize * 0.2d, 1.5d);
            if (!double.IsFinite(candidate)
                || Math.Abs(candidate - predictedCenter) > maximumCenterError)
                continue;

            coordinates[index] = candidate;
            valid[index] = 1;
            recovered++;
        }
        return recovered;
    }

    /// <summary>
    /// 使用完整、等间隔相移序列提取傅里叶基波。削顶样本仍作为截断观测参与
    /// 基波计算；将它们直接丢弃会使采样角度集合随条纹相位周期变化，并把平面
    /// 重建成与条纹同周期的波纹。只有明确孤立的非饱和瞬态毛刺才做留一拟合。
    /// </summary>
    internal static bool TryFitPhase(
        IReadOnlyList<float[]> phase,
        int pixelIndex,
        out double fittedCos,
        out double fittedSin,
        out double modulation,
        out double residual)
    {
        fittedCos = 0d;
        fittedSin = 0d;
        modulation = 0d;
        residual = double.PositiveInfinity;

        int phaseCount = phase.Count;
        if (phaseCount > 64)
            throw new ArgumentOutOfRangeException(nameof(phase), "相移帧数不能超过 64。");
        if (phaseCount < 3)
            return false;

        int minimumUnsaturatedSamples = Math.Max(3, (phaseCount + 1) / 2);
        Span<double> values = stackalloc double[phaseCount];
        Span<double> cosines = stackalloc double[phaseCount];
        Span<double> sines = stackalloc double[phaseCount];
        Span<double> weights = stackalloc double[phaseCount];
        Span<double> absoluteResiduals = stackalloc double[phaseCount];
        double sum = 0d;
        double cosineSum = 0d;
        double sineSum = 0d;
        int unsaturatedCount = 0;
        for (int frame = 0; frame < phaseCount; frame++)
        {
            double value = phase[frame][pixelIndex];
            if (!double.IsFinite(value))
                return false;

            double angle = 2d * Math.PI * frame / phaseCount;
            double cosine = Math.Cos(angle);
            double sine = Math.Sin(angle);
            values[frame] = value;
            cosines[frame] = cosine;
            sines[frame] = sine;
            weights[frame] = 1d;
            sum += value;
            cosineSum += value * cosine;
            sineSum += value * sine;
            if (value < SaturationLevel)
                unsaturatedCount++;
        }

        if (unsaturatedCount < minimumUnsaturatedSamples)
            return false;

        double mean = sum / phaseCount;
        fittedCos = 2d * cosineSum / phaseCount;
        fittedSin = 2d * sineSum / phaseCount;

        // Only an entirely unsaturated sequence is eligible for transient recovery.
        // Clipping is phase-dependent by nature; treating its largest residual as a
        // removable outlier would reintroduce the periodic phase bias fixed above.
        if (phaseCount >= 6 && unsaturatedCount == phaseCount)
        {
            double fullScore = ComputeTrimmedResidualScore(
                values,
                cosines,
                sines,
                mean,
                fittedCos,
                fittedSin,
                absoluteResiduals
            );
            double bestScore = fullScore;
            double bestMean = mean;
            double bestCos = fittedCos;
            double bestSin = fittedSin;
            int bestExcluded = -1;
            for (int excluded = 0; excluded < phaseCount; excluded++)
            {
                weights.Fill(1d);
                weights[excluded] = 0d;
                if (!TrySolveWeightedPhaseModel(
                        values,
                        cosines,
                        sines,
                        weights,
                        out double candidateMean,
                        out double candidateCos,
                        out double candidateSin
                    ))
                    continue;
                double score = ComputeTrimmedResidualScore(
                    values,
                    cosines,
                    sines,
                    candidateMean,
                    candidateCos,
                    candidateSin,
                    absoluteResiduals
                );
                if (score >= bestScore) continue;
                bestScore = score;
                bestMean = candidateMean;
                bestCos = candidateCos;
                bestSin = candidateSin;
                bestExcluded = excluded;
            }

            if (bestScore < fullScore * 0.25d)
            {
                mean = bestMean;
                fittedCos = bestCos;
                fittedSin = bestSin;
                weights.Fill(1d);
                weights[bestExcluded] = 0d;
            }
            else
                weights.Fill(1d);
        }

        modulation = Math.Sqrt(fittedCos * fittedCos + fittedSin * fittedSin);
        double weightedResidualSquared = 0d;
        double weightSum = 0d;
        for (int sample = 0; sample < phaseCount; sample++)
        {
            double fitted = mean
                + fittedCos * cosines[sample]
                + fittedSin * sines[sample];
            double error = values[sample] - fitted;
            weightedResidualSquared += weights[sample] * error * error;
            weightSum += weights[sample];
        }
        residual = Math.Sqrt(weightedResidualSquared / Math.Max(weightSum, 1e-12));
        return double.IsFinite(modulation) && double.IsFinite(residual);
    }

    private static bool TrySolveWeightedPhaseModel(
        ReadOnlySpan<double> values,
        ReadOnlySpan<double> cosines,
        ReadOnlySpan<double> sines,
        ReadOnlySpan<double> weights,
        out double mean,
        out double fittedCos,
        out double fittedSin)
    {
        double sumW = 0d, sumC = 0d, sumS = 0d;
        double sumCC = 0d, sumCS = 0d, sumSS = 0d;
        double sumV = 0d, sumVC = 0d, sumVS = 0d;
        for (int i = 0; i < values.Length; i++)
        {
            double weight = weights[i];
            double cosine = cosines[i];
            double sine = sines[i];
            double value = values[i];
            sumW += weight;
            sumC += weight * cosine;
            sumS += weight * sine;
            sumCC += weight * cosine * cosine;
            sumCS += weight * cosine * sine;
            sumSS += weight * sine * sine;
            sumV += weight * value;
            sumVC += weight * value * cosine;
            sumVS += weight * value * sine;
        }

        double determinant = sumW * (sumCC * sumSS - sumCS * sumCS)
            - sumC * (sumC * sumSS - sumS * sumCS)
            + sumS * (sumC * sumCS - sumS * sumCC);
        if (Math.Abs(determinant) < 1e-9)
        {
            mean = fittedCos = fittedSin = 0d;
            return false;
        }

        mean = (
            sumV * (sumCC * sumSS - sumCS * sumCS)
            - sumC * (sumVC * sumSS - sumCS * sumVS)
            + sumS * (sumVC * sumCS - sumCC * sumVS)
        ) / determinant;
        fittedCos = (
            sumW * (sumVC * sumSS - sumCS * sumVS)
            - sumV * (sumC * sumSS - sumS * sumCS)
            + sumS * (sumC * sumVS - sumVC * sumS)
        ) / determinant;
        fittedSin = (
            sumW * (sumCC * sumVS - sumVC * sumCS)
            - sumC * (sumC * sumVS - sumVC * sumS)
            + sumV * (sumC * sumCS - sumCC * sumS)
        ) / determinant;
        return double.IsFinite(mean)
            && double.IsFinite(fittedCos)
            && double.IsFinite(fittedSin);
    }

    private static double ComputeTrimmedResidualScore(
        ReadOnlySpan<double> values,
        ReadOnlySpan<double> cosines,
        ReadOnlySpan<double> sines,
        double mean,
        double fittedCos,
        double fittedSin,
        Span<double> scratch)
    {
        for (int i = 0; i < values.Length; i++)
        {
            double fitted = mean + fittedCos * cosines[i] + fittedSin * sines[i];
            scratch[i] = Math.Abs(values[i] - fitted);
        }
        scratch.Sort();
        double score = 0d;
        for (int i = 0; i < values.Length - 1; i++)
            score += scratch[i] * scratch[i];
        return score;
    }

    private static double Median(Span<double> values)
    {
        values.Sort();
        int middle = values.Length / 2;
        return values.Length % 2 == 0
            ? (values[middle - 1] + values[middle]) / 2d
            : values[middle];
    }

    private static float[] ReadRectifiedGray(byte[] bytes, Mat mapX, Mat mapY)
    {
        using Mat source = CalibImageUtils.LoadGrayMat(bytes);
        if (source.Empty()) throw new InvalidOperationException("结构光图像解码失败");
        using Mat sourceFloat = new();
        using Mat rectified = new();
        source.ConvertTo(sourceFloat, MatType.CV_32FC1);
        Cv2.Remap(sourceFloat, rectified, mapX, mapY, InterpolationFlags.Linear,
            BorderTypes.Constant, Scalar.Black);
        float[] pixels = new float[rectified.Rows * rectified.Cols];
        System.Runtime.InteropServices.Marshal.Copy(rectified.Data, pixels, 0, pixels.Length);
        return pixels;
    }

    private static int GrayToBinary(int gray)
    {
        int binary = gray;
        for (int shifted = gray >> 1; shifted != 0; shifted >>= 1) binary ^= shifted;
        return binary;
    }

    internal static int FillSmallCoordinateHoles(
        double[] coordinates,
        byte[] valid,
        int width,
        int height)
    {
        byte[] sourceValid = (byte[])valid.Clone();
        double[] sourceCoordinates = (double[])coordinates.Clone();
        int filledCount = 0;
        for (int y = 1; y < height - 1; y++)
        for (int x = 1; x < width - 1; x++)
        {
            int index = y * width + x;
            if (sourceValid[index] != 0) continue;
            bool hasHorizontalSupport = sourceValid[index - 1] != 0
                && sourceValid[index + 1] != 0;
            bool hasVerticalSupport = sourceValid[index - width] != 0
                && sourceValid[index + width] != 0;
            if (!hasHorizontalSupport && !hasVerticalSupport) continue;
            List<double> neighbors = [];
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int neighbor = (y + dy) * width + x + dx;
                if (sourceValid[neighbor] != 0) neighbors.Add(sourceCoordinates[neighbor]);
            }
            if (neighbors.Count != 8) continue;
            neighbors.Sort();
            if (neighbors[^1] - neighbors[0] > 3d) continue;
            coordinates[index] = neighbors[neighbors.Count / 2];
            valid[index] = 1;
            filledCount++;
        }
        return filledCount;
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

    private readonly record struct RowCorrespondence(
        double CameraX,
        double CameraY,
        double ProjectorY,
        bool ProjectorYValid);

    private readonly record struct OneWayMatchResult(
        double[] Values,
        double[] SecondaryY,
        long AttemptedPixels,
        long MatchedPixels,
        long ProjectorYRejected,
        long InterpolationGapRejected,
        long DisparityRejected);

    private sealed class DecodeCounters
    {
        public long Saturated;
        public long LowContrast;
        public long LowModulation;
        public long PhaseResidual;
        public long GrayRecovered;
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
    long PhaseResidualPixels,
    long GrayRecoveredPixels = 0);

internal sealed record GrayPhaseMatchDiagnostics(
    long AttemptedPixels, long MatchedPixels, long ProjectorYRejected,
    long InterpolationGapRejected, long DisparityRejected,
    long BidirectionalRejected, long VerticalStripeRejected);
