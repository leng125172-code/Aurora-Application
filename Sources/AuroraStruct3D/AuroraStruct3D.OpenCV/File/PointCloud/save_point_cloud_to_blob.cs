using AuroraStruct3D.OpenCV.VisionParameters;

namespace AuroraStruct3D.OpenCV.File.PointCloud;

/// <summary>
/// 将点云导出为 PLY 并保存到 BLOB 存储。
/// </summary>
[Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567891")]
[Category("数据存储")]
[DisplayName("点云存 Blob")]
[Description("将点云导出成 PLY 文件并保存到 BLOB，输出 blob 键和下载地址。")]
public class save_point_cloud_to_blob : IOperator
{
    /// <summary>
    /// 算子唯一标识。
    /// </summary>
    public static Guid OperatorId { get; } = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567891");

    /// <summary>
    /// 输入端口定义。
    /// </summary>
    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new PointCloudData { ParameterName = "input_point_cloud", DisplayName = "输入点云" },
            new MatImg { ParameterName = "input_mat", DisplayName = "点云矩阵" },
        };

    /// <summary>
    /// 输出端口定义。
    /// </summary>
    public static List<IVisionParameter>? OutputVisionParameters =>
        new()
        {
            new VisionParameter<string>
            {
                ParameterName = "blob_name",
                DisplayName = "Blob 键",
                ParameterType = typeof(string),
                ControlType = PortControlType.Input,
            },
            new VisionParameter<string>
            {
                ParameterName = "download_url",
                DisplayName = "下载地址",
                ParameterType = typeof(string),
                ControlType = PortControlType.Download,
            },
        };

    /// <summary>
    /// 配置参数定义。
    /// </summary>
    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "fileName",
                DisplayName = "文件名",
                ParameterType = typeof(string),
                DefaultValue = "point-cloud-export.ply",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly string _fileName;
    private bool _disposed;

    /// <summary>
    /// 初始化导出算子。
    /// </summary>
    /// <param name="fileName">目标文件名。</param>
    public save_point_cloud_to_blob(string? fileName = null)
    {
        _fileName = string.IsNullOrWhiteSpace(fileName)
            ? "point-cloud-export.ply"
            : fileName.Trim();
    }

    /// <inheritdoc/>
    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        PointCloudData cloud = ResolveInputCloud(
            context.Get("input_point_cloud"),
            context.Get("input_mat")
        );
        byte[] content = PointCloudPlyWriter.WriteAscii(cloud);

        IOperatorFileBlobStore store = OperatorFileBlobStoreAmbient.Current;
        string blobName = store
            .SavePointCloudPlyAsync(NormalizeFileName(_fileName), content)
            .GetAwaiter()
            .GetResult();
        string downloadUrl = store.BuildDownloadUrl(blobName);

        context.Set("blob_name", blobName);
        context.Set("download_url", downloadUrl);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static PointCloudData ResolveInputCloud(object? pointCloudValue, object? matValue)
    {
        if (pointCloudValue is PointCloudData cloud && cloud.PointCloud is not null)
        {
            return cloud;
        }

        if (pointCloudValue is Mat pointCloudMat)
        {
            return new PointCloudData { Value = pointCloudMat };
        }

        if (matValue is Mat inputMat)
        {
            return new PointCloudData { Value = inputMat };
        }

        if (matValue is MatImg matImg && matImg.Mat is not null)
        {
            return new PointCloudData { Value = matImg.Mat };
        }

        object? actualValue = pointCloudValue ?? matValue;
        if (actualValue is null)
        {
            throw new InvalidOperationException(
                "上下文变量 'input_point_cloud' 或 'input_mat' 为空，请确认输入绑定已正确设置。"
            );
        }

        throw new InvalidOperationException(
            $"输入点云类型无效，期望 PointCloudData 或 Mat，实际 {actualValue.GetType().FullName}。"
        );
    }

    private static string NormalizeFileName(string fileName)
    {
        string normalized = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "point-cloud-export.ply";
        }

        if (!normalized.EndsWith(".ply", StringComparison.OrdinalIgnoreCase))
        {
            normalized += ".ply";
        }

        return normalized;
    }
}
