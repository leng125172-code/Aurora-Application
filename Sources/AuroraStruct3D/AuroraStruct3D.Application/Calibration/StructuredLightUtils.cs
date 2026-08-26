using OpenCvSharp;

namespace AuroraStruct3D.Calibration;

public static class StructuredLightUtils
{
    public static Mat ComputeWrappedPhase(List<Mat> fringeImages)
    {
        return ComputeWrappedPhase(fringeImages, 2d * Math.PI / fringeImages.Count);
    }

    public static Mat ComputeWrappedPhase(
        List<Mat> fringeImages,
        double phaseStepRadians)
    {
        if (fringeImages == null || fringeImages.Count < 3)
            throw new ArgumentException("至少需要3幅条纹图像进行相位计算");
        if (!double.IsFinite(phaseStepRadians) || phaseStepRadians <= 0)
            throw new ArgumentOutOfRangeException(nameof(phaseStepRadians));

        int rows = fringeImages[0].Rows;
        int cols = fringeImages[0].Cols;
        int n = fringeImages.Count;

        Mat phase = new(rows, cols, MatType.CV_64FC1);

        double[,] cosSum = new double[rows, cols];
        double[,] sinSum = new double[rows, cols];

        for (int i = 0; i < n; i++)
        {
            using Mat gray = new();
            Cv2.CvtColor(fringeImages[i], gray, ColorConversionCodes.BGR2GRAY);
            using Mat gray64 = new();
            gray.ConvertTo(gray64, MatType.CV_64FC1, 1.0 / 255.0);

            double phaseShift = phaseStepRadians * i;

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    double intensity = gray64.At<double>(y, x);
                    cosSum[y, x] += intensity * Math.Cos(phaseShift);
                    sinSum[y, x] += intensity * Math.Sin(phaseShift);
                }
            }
        }

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                double eps = 1e-10;
                phase.Set(y, x, Math.Atan2(sinSum[y, x], cosSum[y, x] + eps));
            }
        }

        return phase;
    }

    public static Mat UnwrapPhase(Mat wrappedPhase, int periodCount)
    {
        int rows = wrappedPhase.Rows;
        int cols = wrappedPhase.Cols;

        Mat unwrapped = new(rows, cols, MatType.CV_64FC1);
        double[,] phaseData = new double[rows, cols];

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                phaseData[y, x] = wrappedPhase.At<double>(y, x);
            }
        }

        int[] shifts = new int[rows];
        for (int y = 0; y < rows; y++)
        {
            shifts[y] = 0;
            for (int x = 1; x < cols; x++)
            {
                double diff = phaseData[y, x] - phaseData[y, x - 1];
                while (diff > Math.PI)
                {
                    shifts[y]--;
                    diff -= 2 * Math.PI;
                }
                while (diff < -Math.PI)
                {
                    shifts[y]++;
                    diff += 2 * Math.PI;
                }
                phaseData[y, x] = phaseData[y, x - 1] + diff;
            }
        }

        int globalShift = 0;
        for (int y = 1; y < rows; y++)
        {
            int diff = shifts[y] - shifts[y - 1];
            if (Math.Abs(diff) > 1)
            {
                globalShift += diff > 0 ? -1 : 1;
            }
        }

        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                double totalShift = (shifts[y] + globalShift) * 2 * Math.PI;
                unwrapped.Set(y, x, phaseData[y, x] + totalShift);
            }
        }

        return unwrapped;
    }

    public static Mat ComputeAbsolutePhase(
        List<Mat> horizontalFringeImages,
        List<Mat> verticalFringeImages,
        int horizontalPeriodCount,
        int verticalPeriodCount)
    {
        Mat wrappedH = ComputeWrappedPhase(horizontalFringeImages);
        Mat wrappedV = ComputeWrappedPhase(verticalFringeImages);

        Mat unwrappedH = UnwrapPhase(wrappedH, horizontalPeriodCount);
        Mat unwrappedV = UnwrapPhase(wrappedV, verticalPeriodCount);

        Mat absolutePhase = new(wrappedH.Rows, wrappedH.Cols, MatType.CV_64FC2);

        Mat[] channels = Cv2.Split(absolutePhase);
        try
        {
            unwrappedH.CopyTo(channels[0]);
            unwrappedV.CopyTo(channels[1]);
            Cv2.Merge(channels, absolutePhase);
        }
        finally
        {
            foreach (Mat ch in channels) ch.Dispose();
        }

        wrappedH.Dispose();
        wrappedV.Dispose();
        unwrappedH.Dispose();
        unwrappedV.Dispose();

        return absolutePhase;
    }

    public static Mat ComputeProjectorCoordinate(
        Mat absolutePhase,
        int projectorWidth,
        int projectorHeight,
        int horizontalPeriodCount,
        int verticalPeriodCount)
    {
        int rows = absolutePhase.Rows;
        int cols = absolutePhase.Cols;

        Mat projCoord = new(rows, cols, MatType.CV_64FC2);

        double hPeriod = (double)projectorWidth / horizontalPeriodCount;
        double vPeriod = (double)projectorHeight / verticalPeriodCount;

        Mat[] channels = Cv2.Split(absolutePhase);
        try
        {
            Mat phaseH = channels[0];
            Mat phaseV = channels[1];

            Mat projX = new(rows, cols, MatType.CV_64FC1);
            Mat projY = new(rows, cols, MatType.CV_64FC1);

            try
            {
                for (int y = 0; y < rows; y++)
                {
                    for (int x = 0; x < cols; x++)
                    {
                        double ph = phaseH.At<double>(y, x);
                        double pv = phaseV.At<double>(y, x);

                        double px = (ph / (2 * Math.PI)) * hPeriod;
                        double py = (pv / (2 * Math.PI)) * vPeriod;

                        projX.Set(y, x, px);
                        projY.Set(y, x, py);
                    }
                }

                projX.CopyTo(channels[0]);
                projY.CopyTo(channels[1]);
                Cv2.Merge(channels, projCoord);
            }
            finally
            {
                projX.Dispose();
                projY.Dispose();
            }
        }
        finally
        {
            foreach (Mat ch in channels) ch.Dispose();
        }

        return projCoord;
    }
}
