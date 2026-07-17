using System.Runtime.InteropServices;
using System.Text.Json;
using OpenCvSharp;
using SkiaSharp;

namespace AuroraStruct3D.Calibration;

public static class CalibImageUtils
{
    public static Mat LoadGrayMatWithRotation(byte[] imageBytes, int rotationAngle)
    {
        using Mat mat = Cv2.ImDecode(imageBytes, ImreadModes.Grayscale);
        if (mat.Empty())
        {
            try
            {
                using SKBitmap? skBitmap = SKBitmap.Decode(imageBytes);
                if (skBitmap == null || skBitmap.IsNull)
                    return new Mat();

                using SKBitmap gray8 = skBitmap.Copy(SKColorType.Gray8);
                byte[] grayBytes = gray8.Bytes;
                mat.Dispose();
                using Mat grayMat = new(gray8.Height, gray8.Width, MatType.CV_8UC1);
                Marshal.Copy(grayBytes, 0, grayMat.Data, grayBytes.Length);
                return ApplyRotation(grayMat, rotationAngle);
            }
            catch
            {
                return new Mat();
            }
        }

        return ApplyRotation(mat, rotationAngle);
    }

    public static Mat LoadGrayMat(byte[] imageBytes)
    {
        using Mat mat = Cv2.ImDecode(imageBytes, ImreadModes.Grayscale);
        if (!mat.Empty())
            return mat.Clone();

        try
        {
            using SKBitmap? skBitmap = SKBitmap.Decode(imageBytes);
            if (skBitmap == null || skBitmap.IsNull)
                return new Mat();

            using SKBitmap gray8 = skBitmap.Copy(SKColorType.Gray8);
            byte[] grayBytes = gray8.Bytes;
            Mat grayMat = new(gray8.Height, gray8.Width, MatType.CV_8UC1);
            Marshal.Copy(grayBytes, 0, grayMat.Data, grayBytes.Length);
            return grayMat;
        }
        catch
        {
            return new Mat();
        }
    }

    public static Mat LoadBgrMat(byte[] imageBytes)
    {
        using Mat mat = Cv2.ImDecode(imageBytes, ImreadModes.Color);
        if (!mat.Empty())
        {
            return mat.Clone();
        }
        return SkiaBytesToBgrMat(imageBytes);
    }

    public static Mat SkiaBytesToBgrMat(byte[] imageBytes)
    {
        try
        {
            using SKBitmap? skBitmap = SKBitmap.Decode(imageBytes);
            if (skBitmap == null || skBitmap.IsNull)
                return new Mat();

            using SKBitmap bgra =
                skBitmap.ColorType == SKColorType.Bgra8888
                    ? skBitmap.Copy()
                    : skBitmap.Copy(SKColorType.Bgra8888);

            int width = bgra.Width;
            int height = bgra.Height;
            byte[] bgraBytes = bgra.Bytes;

            byte[] bgrBytes = new byte[width * height * 3];
            for (int i = 0, src = 0; src < bgraBytes.Length; i += 3, src += 4)
            {
                bgrBytes[i + 0] = bgraBytes[src + 0];
                bgrBytes[i + 1] = bgraBytes[src + 1];
                bgrBytes[i + 2] = bgraBytes[src + 2];
            }

            Mat mat = new(height, width, MatType.CV_8UC3);
            Marshal.Copy(bgrBytes, 0, mat.Data, bgrBytes.Length);
            return mat;
        }
        catch
        {
            return new Mat();
        }
    }

    public static Mat ApplyRotation(Mat grayMat, int rotationAngle)
    {
        if (rotationAngle == 0)
            return grayMat.Clone();

        Mat rotated = new();
        switch (rotationAngle)
        {
            case 90:
                Cv2.Transpose(grayMat, rotated);
                Cv2.Flip(rotated, rotated, FlipMode.X);
                break;
            case 180:
                Cv2.Flip(grayMat, rotated, FlipMode.XY);
                break;
            case 270:
                Cv2.Transpose(grayMat, rotated);
                Cv2.Flip(rotated, rotated, FlipMode.Y);
                break;
            default:
                grayMat.CopyTo(rotated);
                break;
        }

        return rotated;
    }

    public static string SerializeMatToJson(Mat mat)
    {
        if (mat.Empty())
            return "[]";

        double[] data = new double[mat.Rows * mat.Cols];
        for (int i = 0; i < mat.Rows; i++)
        {
            for (int j = 0; j < mat.Cols; j++)
            {
                data[i * mat.Cols + j] = mat.At<double>(i, j);
            }
        }

        return JsonSerializer.Serialize(data);
    }

    public static string SerializeVecToJson(Mat mat)
    {
        if (mat.Empty())
            return "[]";

        double[] data = new double[mat.Rows * mat.Cols];
        int idx = 0;
        for (int i = 0; i < mat.Rows; i++)
        {
            for (int j = 0; j < mat.Cols; j++)
            {
                data[idx++] = mat.At<double>(i, j);
            }
        }

        return JsonSerializer.Serialize(data);
    }

    public static Mat DeserializeMatrix(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Mat();

        double[]? data = JsonSerializer.Deserialize<double[]>(json);
        if (data == null || data.Length == 0)
            return new Mat();

        int cols = (int)Math.Sqrt(data.Length);
        int rows = data.Length / cols;

        Mat mat = new(rows, cols, MatType.CV_64F);
        for (int i = 0; i < rows; i++)
        {
            for (int j = 0; j < cols; j++)
            {
                mat.Set(i, j, data[i * cols + j]);
            }
        }

        return mat;
    }

    public static Mat DeserializeVector(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Mat();

        double[]? data = JsonSerializer.Deserialize<double[]>(json);
        if (data == null || data.Length == 0)
            return new Mat();

        Mat mat = new(data.Length, 1, MatType.CV_64F);
        for (int i = 0; i < data.Length; i++)
        {
            mat.Set(i, 0, data[i]);
        }

        return mat;
    }

    public static string SerializeTransformToJson(Mat rotation, Mat translation)
    {
        return JsonSerializer.Serialize(new
        {
            R = SerializeMatToJson(rotation),
            T = SerializeVecToJson(translation),
        });
    }

    public static string SerializeInverseTransformToJson(Mat rotation, Mat translation)
    {
        using Mat rInv = rotation.T();
        using Mat tInv = -rInv * translation;

        return JsonSerializer.Serialize(new
        {
            R = SerializeMatToJson(rInv),
            T = SerializeVecToJson(tInv),
        });
    }

    public static byte[] SerializeFloatMapToBinary(Mat map)
    {
        if (map.Empty())
            return Array.Empty<byte>();

        int size = (int)(map.Rows * map.Cols * sizeof(float));
        byte[] result = new byte[size];

        unsafe
        {
            byte* src = (byte*)map.Data.ToPointer();
            fixed (byte* dst = result)
            {
                Buffer.MemoryCopy(src, dst, size, size);
            }
        }

        return result;
    }

    public static string? GenerateThumbnailBase64(byte[] imageBytes)
    {
        try
        {
            using Mat bgr = LoadBgrMat(imageBytes);
            if (bgr.Empty())
                return null;

            const int maxSide = 200;
            int longSide = Math.Max(bgr.Cols, bgr.Rows);

            if (longSide <= maxSide)
            {
                using Mat rgbMat = new();
                Cv2.CvtColor(bgr, rgbMat, ColorConversionCodes.BGR2RGB);
                using SKImage skImage = SKImage.FromPixels(
                    new SKImageInfo(rgbMat.Cols, rgbMat.Rows, SKColorType.Rgba8888, SKAlphaType.Opaque),
                    rgbMat.Data,
                    (int)rgbMat.Step()
                );
                using SKData skData = skImage.Encode(SKEncodedImageFormat.Jpeg, 80);
                return "data:image/jpeg;base64," + Convert.ToBase64String(skData.ToArray());
            }

            double scale = maxSide / (double)longSide;
            int w = Math.Max(1, (int)Math.Round(bgr.Cols * scale));
            int h = Math.Max(1, (int)Math.Round(bgr.Rows * scale));
            using Mat resized = new();
            Cv2.Resize(bgr, resized, new Size(w, h), interpolation: InterpolationFlags.Area);

            using Mat rgbMat2 = new();
            Cv2.CvtColor(resized, rgbMat2, ColorConversionCodes.BGR2RGB);
            using SKImage skImage2 = SKImage.FromPixels(
                new SKImageInfo(rgbMat2.Cols, rgbMat2.Rows, SKColorType.Rgba8888, SKAlphaType.Opaque),
                rgbMat2.Data,
                (int)rgbMat2.Step()
            );
            using SKData skData2 = skImage2.Encode(SKEncodedImageFormat.Jpeg, 80);
            return "data:image/jpeg;base64," + Convert.ToBase64String(skData2.ToArray());
        }
        catch
        {
            return null;
        }
    }

    public static Point2d? DetectCrossCenter(byte[] imageBytes, int rotationAngle = 0)
    {
        try
        {
            using Mat gray = LoadGrayMatWithRotation(imageBytes, rotationAngle);
            if (gray.Empty())
                return null;

            using Mat blurred = new();
            Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);

            using Mat edges = new();
            Cv2.Canny(blurred, edges, 50, 150);

            LineSegmentPoint[] lines = Cv2.HoughLinesP(
                edges,
                1,
                Math.PI / 180,
                50,
                minLineLength: 50,
                maxLineGap: 10
            );

            if (lines.Length < 2)
                return null;

            List<(double angle, LineSegmentPoint line)> angleLines = new();
            foreach (LineSegmentPoint line in lines)
            {
                double dx = line.P2.X - line.P1.X;
                double dy = line.P2.Y - line.P1.Y;
                double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
                angleLines.Add((angle, line));
            }

            angleLines.Sort((a, b) => a.angle.CompareTo(b.angle));

            List<LineSegmentPoint> horizontalLines = new();
            List<LineSegmentPoint> verticalLines = new();

            foreach ((double angle, LineSegmentPoint line) in angleLines)
            {
                if (Math.Abs(angle) < 30 || Math.Abs(angle - 180) < 30)
                    horizontalLines.Add(line);
                else if (Math.Abs(angle - 90) < 30 || Math.Abs(angle + 90) < 30)
                    verticalLines.Add(line);
            }

            if (horizontalLines.Count == 0 || verticalLines.Count == 0)
                return null;

            LineSegmentPoint hLine = horizontalLines[0];
            LineSegmentPoint vLine = verticalLines[0];

            double hx1 = hLine.P1.X, hy1 = hLine.P1.Y, hx2 = hLine.P2.X, hy2 = hLine.P2.Y;
            double vx1 = vLine.P1.X, vy1 = vLine.P1.Y, vx2 = vLine.P2.X, vy2 = vLine.P2.Y;

            double denom = (hx2 - hx1) * (vy2 - vy1) - (hy2 - hy1) * (vx2 - vx1);
            if (Math.Abs(denom) < 1e-10)
                return null;

            double t = ((vx1 - hx1) * (vy2 - vy1) - (vy1 - hy1) * (vx2 - vx1)) / denom;
            double u = -((vx1 - hx1) * (hy2 - hy1) - (vy1 - hy1) * (hx2 - hx1)) / denom;

            if (t < -0.5 || t > 1.5 || u < -0.5 || u > 1.5)
                return null;

            double cx = hx1 + t * (hx2 - hx1);
            double cy = hy1 + t * (hy2 - hy1);

            int halfWin = 5;
            double refinedX = SubPixelCenter1D(gray, false, (int)Math.Round(cx), halfWin);
            double refinedY = SubPixelCenter1D(gray, true, (int)Math.Round(cy), halfWin);

            return new Point2d(refinedX, refinedY);
        }
        catch
        {
            return null;
        }
    }

    public static double SubPixelCenter1D(Mat vec, bool isRow, int center, int halfWin)
    {
        int len = isRow ? vec.Cols : vec.Rows;
        int start = Math.Max(0, center - halfWin);
        int end = Math.Min(len - 1, center + halfWin);

        if (end - start < 2)
            return center;

        double sum = 0, weightedSum = 0;

        for (int i = start; i <= end; i++)
        {
            double val = isRow ? vec.At<byte>(center, i) : vec.At<byte>(i, center);
            sum += val;
            weightedSum += val * i;
        }

        return sum > 0 ? weightedSum / sum : center;
    }
}