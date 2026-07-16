namespace AuroraStruct3D.OpenCV.PointCloudProjection;

/// <summary>
/// 工作流算子：将带颜色的点云绕 Y 轴、X 轴和 Z 轴旋转后投影到 2D 图像。
/// <para>
/// 与 <see cref="colored_point_cloud_to_image"/> 不同，本算子依次将点云绕 Y 轴、X 轴、Z 轴旋转，
/// 然后将旋转后的点云投影到 XY 平面，生成一张带三轴透视效果的倾斜视图。
/// 常用于辅助展示高度差异（Z 方向的变化在倾斜视角下更直观）。
/// </para>
/// <para>
/// 端口约定：
/// <list type="bullet">
///   <item>输入 <c>input_point_cloud</c>（PointCloudData）— 输入点云（建议为已着色）</item>
///   <item>输出 <c>output_image</c>（Mat）— 倾斜投影后的 2D 图像（CV_8UC4，空白区域透明）</item>
/// </list>
/// </para>
/// </summary>
[Guid("7e3a2b1c-9d4e-4f5a-8b6c-1d2e3f4a5b6c")]
[Category("3D重建分割")]
[DisplayName("彩色点云转倾斜视图")]
[Description("把带颜色的点云绕Y轴、X轴和Z轴旋转后投影成2D彩色图，展示三轴透视的高度差异。")]
public class colored_point_cloud_to_tilted_image : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData { ParameterName = "input_point_cloud", DisplayName = "输入点云" },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new MatImg { ParameterName = "output_image", DisplayName = "输出图像" },
            new VisionParameter<string>
            {
                ParameterName = "projection_mapping",
                ParameterType = typeof(string),
                DisplayName = "投影映射",
            },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "tiltAngleY",
                DisplayName = "Y轴倾斜角度(度)",
                ParameterType = typeof(double),
                DefaultValue = "30",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "tiltAngleX",
                DisplayName = "X轴倾斜角度(度)",
                ParameterType = typeof(double),
                DefaultValue = "30",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "tiltAngleZ",
                DisplayName = "Z轴倾斜角度(度)",
                ParameterType = typeof(double),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "imageResolution",
                DisplayName = "图像分辨率",
                ParameterType = typeof(int),
                DefaultValue = "512",
                ValueLimit = new[] { "256", "512", "1024", "2048" },
                Required = false,
                ControlType = PortControlType.Select,
            },
        };

    private readonly double _tiltAngleYDeg;
    private readonly double _tiltAngleXDeg;
    private readonly double _tiltAngleZDeg;
    private readonly int _imageResolution;
    private bool _disposed;

    /// <summary>
    /// 初始化彩色点云转倾斜视图算子。
    /// </summary>
    /// <param name="tiltAngleY">绕 Y 轴旋转的角度（度），正值表示俯视方向倾斜。</param>
    /// <param name="tiltAngleX">绕 X 轴旋转的角度（度），正值表示右侧下倾。</param>
    /// <param name="tiltAngleZ">绕 Z 轴旋转的角度（度），正值表示顺时针旋转。</param>
    /// <param name="imageResolution">输出图像最长边像素数。</param>
    public colored_point_cloud_to_tilted_image(
        double tiltAngleY = 30,
        double tiltAngleX = 30,
        double tiltAngleZ = 0,
        int imageResolution = 512
    )
    {
        _tiltAngleYDeg = tiltAngleY;
        _tiltAngleXDeg = tiltAngleX;
        _tiltAngleZDeg = tiltAngleZ;
        _imageResolution = imageResolution;
    }

    /// <inheritdoc/>
    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        PointCloudData input =
            context.Get<PointCloudData>("input_point_cloud")
            ?? throw new InvalidOperationException(
                "上下文变量 'input_point_cloud' 为空，请确认输入绑定已正确设置。"
            );

        Mat pointCloud =
            input.PointCloud
            ?? throw new InvalidOperationException("输入点云为空，无法执行倾斜投影。");
        if (pointCloud.Empty())
        {
            throw new InvalidOperationException("输入点云为空，无法执行倾斜投影。");
        }

        int pointCount = pointCloud.Rows;
        int res = Math.Max(1, _imageResolution);

        // 绕 Y 轴旋转：x' = x*cosθy + z*sinθy, y' = y, z' = -x*sinθy + z*cosθy
        double thetaY = _tiltAngleYDeg * Math.PI / 180.0;
        double cosTY = Math.Cos(thetaY);
        double sinTY = Math.Sin(thetaY);

        // 绕 X 轴旋转：x'' = x', y'' = y'*cosθx - z'*sinθx, z'' = y'*sinθx + z'*cosθx
        double thetaX = _tiltAngleXDeg * Math.PI / 180.0;
        double cosTX = Math.Cos(thetaX);
        double sinTX = Math.Sin(thetaX);

        // 绕 Z 轴旋转：x''' = x''*cosθz - y''*sinθz, y''' = x''*sinθz + y''*cosθz
        double thetaZ = _tiltAngleZDeg * Math.PI / 180.0;
        double cosTZ = Math.Cos(thetaZ);
        double sinTZ = Math.Sin(thetaZ);

        // 第一遍：计算旋转后的 XY 范围
        double minX = double.MaxValue;
        double maxX = double.MinValue;
        double minY = double.MaxValue;
        double maxY = double.MinValue;

        // 预分配旋转后的坐标数组，避免二次计算
        float[] rotX = new float[pointCount];
        float[] rotY = new float[pointCount];

        for (int i = 0; i < pointCount; i++)
        {
            float x = pointCloud.Get<float>(i, 0);
            float y = pointCloud.Get<float>(i, 1);
            float z = pointCloud.Get<float>(i, 2);

            // 绕 Y 轴
            float rxY = (float)(x * cosTY + z * sinTY);
            float rzY = (float)(-x * sinTY + z * cosTY);

            // 绕 X 轴
            float rxX = rxY;
            float ryX = (float)(y * cosTX - rzY * sinTX);
            float rzX = (float)(y * sinTX + rzY * cosTX);

            // 绕 Z 轴
            float rx = (float)(rxX * cosTZ - ryX * sinTZ);
            float ry = (float)(rxX * sinTZ + ryX * cosTZ);

            rotX[i] = rx;
            rotY[i] = ry;

            if (rx < minX)
                minX = rx;
            if (rx > maxX)
                maxX = rx;
            if (ry < minY)
                minY = ry;
            if (ry > maxY)
                maxY = ry;
        }

        double rangeX = maxX - minX;
        double rangeY = maxY - minY;
        if (Math.Abs(rangeX) < 1e-6)
            rangeX = 1;
        if (Math.Abs(rangeY) < 1e-6)
            rangeY = 1;

        // 计算输出图像尺寸（保持长宽比）
        int width;
        int height;
        if (rangeX >= rangeY)
        {
            width = res;
            height = Math.Max(1, (int)Math.Round(res * (rangeY / rangeX)));
        }
        else
        {
            height = res;
            width = Math.Max(1, (int)Math.Round(res * (rangeX / rangeY)));
        }

        // 第二遍：渲染到图像
        using Mat accumR = Mat.Zeros(height, width, MatType.CV_64FC1);
        using Mat accumG = Mat.Zeros(height, width, MatType.CV_64FC1);
        using Mat accumB = Mat.Zeros(height, width, MatType.CV_64FC1);
        using Mat count = Mat.Zeros(height, width, MatType.CV_32SC1);

        Mat? colors = input.HasColors ? input.Colors : null;
        bool useDedicatedColors =
            colors is not null && !colors.Empty() && colors.Rows == pointCount && colors.Cols >= 3;

        for (int i = 0; i < pointCount; i++)
        {
            int px = (int)((rotX[i] - minX) / rangeX * (width - 1));
            int py = (int)((rotY[i] - minY) / rangeY * (height - 1));
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
                    continue;

                byte b = (byte)Math.Clamp(accumB.Get<double>(y, x) / pixelCount, 0, 255);
                byte g = (byte)Math.Clamp(accumG.Get<double>(y, x) / pixelCount, 0, 255);
                byte r = (byte)Math.Clamp(accumR.Get<double>(y, x) / pixelCount, 0, 255);
                image.Set(y, x, new Vec4b(b, g, r, byte.MaxValue));
            }
        }

        using Mat gray = new Mat();
        Cv2.CvtColor(image, gray, ColorConversionCodes.BGRA2GRAY);
        using Mat mask = new Mat();
        Cv2.Threshold(gray, mask, 1, 255, ThresholdTypes.Binary);

        Cv2.GaussianBlur(image, image, new Size(7, 7), 0);

        using Mat kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
        Cv2.MorphologyEx(image, image, MorphTypes.Close, kernel);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (mask.Get<byte>(y, x) == 0)
                {
                    image.Set(y, x, new Vec4b(0, 0, 0, 0));
                }
            }
        }

        context.Set("output_image", image);

        var projectionMapping = new RoiOps.RoiProjectionMapping
        {
            ViewLabel = "TILTED",
            WorldMinX = minX,
            WorldMaxX = maxX,
            WorldMinY = minY,
            WorldMaxY = maxY,
            ImageWidth = width,
            ImageHeight = height,
        };
        context.Set(
            "projection_mapping",
            System.Text.Json.JsonSerializer.Serialize(projectionMapping)
        );
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
