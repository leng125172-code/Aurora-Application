using AuroraStruct3D.OpenCV.File.PointCloud;
using AuroraStruct3D.OpenCV.VisionParameters;

namespace AuroraStruct3D.OpenCV.File.Images;

/// <summary>
/// 将 Mat 图像导出为 PNG 并保存到 BLOB 存储。
/// </summary>
[Guid("4b8af0f5-c0fb-45df-97a6-5ab2462cfd01")]
[Category("数据存储")]
[DisplayName("图片存 Blob")]
[Description("将图像导出成 PNG 文件并保存到 BLOB，输出 blob 键和下载地址。")]
public class save_image_to_blob : IOperator
{
    public static Guid OperatorId { get; } = Guid.Parse("4b8af0f5-c0fb-45df-97a6-5ab2462cfd01");

    public static List<IVisionParameter>? InputVisionParameters =>
        new()
        {
            new MatImg { ParameterName = "input_mat", DisplayName = "输入图像" },
        };

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

    public static List<IConfigParameter>? ConfigParameters =>
        new()
        {
            new ConfigParameter
            {
                Name = "fileName",
                DisplayName = "文件名",
                ParameterType = typeof(string),
                DefaultValue = "workflow-result.png",
                Required = false,
                ControlType = PortControlType.Input,
            },
        };

    private readonly string _fileName;
    private bool _disposed;

    public save_image_to_blob(string? fileName = null)
    {
        _fileName = string.IsNullOrWhiteSpace(fileName) ? "workflow-result.png" : fileName.Trim();
    }

    public void Execute(IWorkflowContext context)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Mat inputMat =
            context.Get<Mat>("input_mat")
            ?? throw new InvalidOperationException(
                "上下文变量 'input_mat' 为空，请确认输入绑定已正确设置。"
            );
        if (inputMat.Empty())
        {
            throw new InvalidOperationException("输入图像为空，无法导出 PNG。");
        }

        if (!Cv2.ImEncode(".png", inputMat, out byte[] content))
        {
            throw new InvalidOperationException("PNG 编码失败，无法导出图像。");
        }

        IOperatorFileBlobStore store = OperatorFileBlobStoreAmbient.Current;
        string blobName = store
            .SaveImagePngAsync(NormalizeFileName(_fileName), content)
            .GetAwaiter()
            .GetResult();
        string downloadUrl = store.BuildPreviewUrl(blobName);

        context.Set("blob_name", blobName);
        context.Set("download_url", downloadUrl);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static string NormalizeFileName(string fileName)
    {
        string normalized = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = "workflow-result.png";
        }

        if (!normalized.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            normalized += ".png";
        }

        return normalized;
    }
}
