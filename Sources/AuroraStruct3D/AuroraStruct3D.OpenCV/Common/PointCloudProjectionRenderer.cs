namespace AuroraStruct3D.OpenCV.Common;

/// <summary>
/// 点云投影渲染辅助方法。
/// </summary>
public static class PointCloudProjectionRenderer
{
    /// <summary>
    /// 将带颜色的点云投影到 XY 平面，生成 BGRA 图像（空白区域透明）。
    /// 优先使用 <see cref="PointCloudData.Colors"/>，兼容回退到坐标矩阵第 4-6 列。
    /// </summary>
    /// <param name="cloud">输入点云。</param>
    /// <param name="resolution">输出图像最长边尺寸。</param>
    /// <returns>投影结果图像。</returns>
    public static Mat RenderColorImage(PointCloudData cloud, int resolution)
    {
        ArgumentNullException.ThrowIfNull(cloud);

        Mat pointCloud =
            cloud.PointCloud
            ?? throw new InvalidOperationException("输入点云为空，无法执行彩色投影。");
        if (pointCloud.Empty())
        {
            throw new InvalidOperationException("输入点云为空，无法执行彩色投影。");
        }

        int pointCount = pointCloud.Rows;
        (float minX, float maxX, float minY, float maxY) = ComputeXyBounds(pointCloud, pointCount);

        return RenderColorImage(cloud, resolution, minX, maxX, minY, maxY);
    }

    /// <summary>
    /// 将带颜色的点云投影到给定 XY 范围内，生成 BGRA 图像（空白区域透明）。
    /// </summary>
    public static Mat RenderColorImage(
        PointCloudData cloud,
        int resolution,
        double minX,
        double maxX,
        double minY,
        double maxY
    )
    {
        ArgumentNullException.ThrowIfNull(cloud);

        Mat pointCloud =
            cloud.PointCloud
            ?? throw new InvalidOperationException("输入点云为空，无法执行彩色投影。");
        if (pointCloud.Empty())
        {
            throw new InvalidOperationException("输入点云为空，无法执行彩色投影。");
        }

        int pointCount = pointCloud.Rows;

        float rangeX = (float)(maxX - minX);
        float rangeY = (float)(maxY - minY);
        if (Math.Abs(rangeX) < 1e-6f)
        {
            rangeX = 1f;
        }

        if (Math.Abs(rangeY) < 1e-6f)
        {
            rangeY = 1f;
        }

        int longSide = Math.Max(1, resolution);
        (int width, int height) = ComputeImageSize(rangeX, rangeY, longSide);
        using Mat accumR = Mat.Zeros(height, width, MatType.CV_64FC1);
        using Mat accumG = Mat.Zeros(height, width, MatType.CV_64FC1);
        using Mat accumB = Mat.Zeros(height, width, MatType.CV_64FC1);
        using Mat count = Mat.Zeros(height, width, MatType.CV_32SC1);

        Mat? colors = cloud.HasColors ? cloud.Colors : null;
        bool useDedicatedColors =
            colors is not null && !colors.Empty() && colors.Rows == pointCount && colors.Cols >= 3;

        for (int i = 0; i < pointCount; i++)
        {
            int px = (int)((pointCloud.Get<float>(i, 0) - minX) / rangeX * (width - 1));
            int py = (int)((pointCloud.Get<float>(i, 1) - minY) / rangeY * (height - 1));
            px = Math.Clamp(px, 0, width - 1);
            py = Math.Clamp(py, 0, height - 1);

            double b;
            double g;
            double r;
            if (useDedicatedColors && colors is not null)
            {
                b = colors.Get<byte>(i, 0);
                g = colors.Get<byte>(i, 1);
                r = colors.Get<byte>(i, 2);
            }
            else if (pointCloud.Cols >= 6)
            {
                r = pointCloud.Get<float>(i, 3);
                g = pointCloud.Get<float>(i, 4);
                b = pointCloud.Get<float>(i, 5);
            }
            else
            {
                throw new InvalidOperationException("输入点云缺少颜色信息，无法执行彩色投影。");
            }

            accumB.Set(py, px, accumB.Get<double>(py, px) + b);
            accumG.Set(py, px, accumG.Get<double>(py, px) + g);
            accumR.Set(py, px, accumR.Get<double>(py, px) + r);
            count.Set(py, px, count.Get<int>(py, px) + 1);
        }

        Mat image = new Mat(height, width, MatType.CV_8UC4, new Scalar(0, 0, 0, 0));
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int pixelCount = count.Get<int>(y, x);
                if (pixelCount <= 0)
                {
                    continue;
                }

                byte b = (byte)Math.Clamp(accumB.Get<double>(y, x) / pixelCount, 0, 255);
                byte g = (byte)Math.Clamp(accumG.Get<double>(y, x) / pixelCount, 0, 255);
                byte r = (byte)Math.Clamp(accumR.Get<double>(y, x) / pixelCount, 0, 255);
                image.Set(y, x, new Vec4b(b, g, r, byte.MaxValue));
            }
        }

        return image;
    }

    private static (int width, int height) ComputeImageSize(
        float rangeX,
        float rangeY,
        int longSide
    )
    {
        if (rangeX >= rangeY)
        {
            int width = longSide;
            int height = Math.Max(1, (int)Math.Round(longSide * (rangeY / rangeX)));
            return (width, height);
        }

        int h = longSide;
        int w = Math.Max(1, (int)Math.Round(longSide * (rangeX / rangeY)));
        return (w, h);
    }

    private static (float minX, float maxX, float minY, float maxY) ComputeXyBounds(
        Mat pointCloud,
        int pointCount
    )
    {
        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minY = float.MaxValue;
        float maxY = float.MinValue;

        for (int i = 0; i < pointCount; i++)
        {
            float x = pointCloud.Get<float>(i, 0);
            float y = pointCloud.Get<float>(i, 1);
            if (x < minX)
            {
                minX = x;
            }

            if (x > maxX)
            {
                maxX = x;
            }

            if (y < minY)
            {
                minY = y;
            }

            if (y > maxY)
            {
                maxY = y;
            }
        }

        return (minX, maxX, minY, maxY);
    }
}
