using System.Text.Json;

namespace AuroraStruct3D.OpenCV.PointCloudOps;

[Guid("c8b5e2a1-3d4f-5c6d-7e8f-9a0b1c2d3e4f")]
[Category("3D点云预处理")]
[DisplayName("ROI区域点云着色")]
[Description("根据2D ROI掩膜，将3D点云中对应区域着色为指定颜色，方便验证选区是否正确。")]
public class colorize_point_cloud_by_roi : IOperator
{
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData { ParameterName = "input_point_cloud", DisplayName = "输入点云" },
            new MatImg { ParameterName = "roi_mask", DisplayName = "ROI掩膜" },
            new VisionParameter<string>
            {
                ParameterName = "roi_metadata",
                ParameterType = typeof(string),
                DisplayName = "ROI元数据",
                ControlType = PortControlType.Variable,
            },
        };

    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new PointCloudData { ParameterName = "output_point_cloud", DisplayName = "输出点云" },
        };

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "colorR",
                DisplayName = "红色分量",
                ParameterType = typeof(int),
                DefaultValue = "255",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "colorG",
                DisplayName = "绿色分量",
                ParameterType = typeof(int),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "colorB",
                DisplayName = "蓝色分量",
                ParameterType = typeof(int),
                DefaultValue = "0",
                Required = false,
                ControlType = PortControlType.Input,
            },
            new ConfigParameter
            {
                Name = "blendMode",
                DisplayName = "混合模式",
                ParameterType = typeof(string),
                DefaultValue = "replace",
                ValueLimit = new[] { "replace", "overlay" },
                Required = false,
                ControlType = PortControlType.Select,
            },
        };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly int _colorR;
    private readonly int _colorG;
    private readonly int _colorB;
    private readonly string _blendMode;
    private bool _disposed;

    public colorize_point_cloud_by_roi(
        int colorR = 255,
        int colorG = 0,
        int colorB = 0,
        string blendMode = "replace"
    )
    {
        _colorR = Math.Clamp(colorR, 0, 255);
        _colorG = Math.Clamp(colorG, 0, 255);
        _colorB = Math.Clamp(colorB, 0, 255);
        _blendMode = blendMode;
    }

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
            ?? throw new InvalidOperationException("输入点云为空，无法执行着色。");
        if (pointCloud.Empty())
        {
            throw new InvalidOperationException("输入点云为空，无法执行着色。");
        }

        Mat? mask = context.Get<Mat>("roi_mask");
        if (mask is null || mask.Empty())
            throw new InvalidOperationException("ROI掩膜为空。");

        string? metadataJson = context.Get<string>("roi_metadata");
        if (string.IsNullOrWhiteSpace(metadataJson))
            throw new InvalidOperationException("ROI元数据为空。");

        var metadata = JsonSerializer.Deserialize<RoiOps.RoiPartitionMetadata>(metadataJson, JsonOptions);
        var mapping = metadata?.ProjectionMapping;
        if (mapping is null)
            throw new InvalidOperationException("ROI元数据中未找到投影映射。");

        int pointCount = pointCloud.Rows;
        Mat outputColors = input.HasColors
            ? input.Colors!.Clone()
            : new Mat(pointCount, 3, MatType.CV_8UC1);

        int maskWidth = mask.Width;
        int maskHeight = mask.Height;

        double worldMinX = mapping.WorldMinX;
        double worldMaxX = mapping.WorldMaxX;
        double worldMinY = mapping.WorldMinY;
        double worldMaxY = mapping.WorldMaxY;

        double worldRangeX = worldMaxX - worldMinX;
        double worldRangeY = worldMaxY - worldMinY;

        for (int i = 0; i < pointCount; i++)
        {
            float worldX = pointCloud.Get<float>(i, 0);
            float worldY = pointCloud.Get<float>(i, 1);

            int px = (int)((worldX - worldMinX) / worldRangeX * (maskWidth - 1));
            int py = (int)((worldY - worldMinY) / worldRangeY * (maskHeight - 1));

            if (px >= 0 && px < maskWidth && py >= 0 && py < maskHeight)
            {
                if (mask.Get<byte>(py, px) > 0)
                {
                    if (_blendMode == "replace")
                    {
                        outputColors.Set(i, 0, (byte)_colorB);
                        outputColors.Set(i, 1, (byte)_colorG);
                        outputColors.Set(i, 2, (byte)_colorR);
                    }
                    else
                    {
                        byte r = (byte)((_colorR + outputColors.Get<byte>(i, 2)) / 2);
                        byte g = (byte)((_colorG + outputColors.Get<byte>(i, 1)) / 2);
                        byte b = (byte)((_colorB + outputColors.Get<byte>(i, 0)) / 2);
                        outputColors.Set(i, 0, b);
                        outputColors.Set(i, 1, g);
                        outputColors.Set(i, 2, r);
                    }
                }
            }
        }

        PointCloudData output = new();
        output.Value = pointCloud.Clone();
        output.SetColors(outputColors);
        context.Set("output_point_cloud", output);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
