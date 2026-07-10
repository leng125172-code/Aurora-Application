using System.Buffers;
using System.Security.Cryptography;
using AuroraStruct3D.OpenCV.Registry;
using AuroraStruct3D.OperatorFile.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using OpenCvSharp;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;

namespace AuroraStruct3D.OperatorFile;

/// <summary>
/// 算子文件上传应用服务实现。
/// 根据算子 GUID 校验输入端口类型与文件格式匹配后，将文件保存到 BLOB 存储。
/// 图片自动生成灰度预览图；点云不再在上传阶段生成预览（ROI 底图改由工作流预运行按实际入参动态生成）。
/// <para>
/// 文件有效期：上传后默认 24 小时过期，若前端保存工作流时调用 <see cref="ConfirmAsync"/>，
/// 则标记为已使用，取消过期时间，避免被自动清理。
/// </para>
/// </summary>
[Authorize]
public class OperatorFileAppService : AuroraStruct3DAppService, IOperatorFileAppService
{
    private readonly IOperatorRegistry _operatorRegistry;
    private readonly IBlobContainer<OperatorFileBlobContainer> _blobContainer;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOperatorFileRecordRepository _recordRepository;
    private readonly IConfiguration _configuration;

    /// <summary>文件默认有效期（小时）。</summary>
    private const int DefaultExpirationHours = 24;

    /// <summary>read_image 算子的 GUID。</summary>
    private static readonly Guid ReadImageOperatorId = Guid.Parse(
        "7544f3f3-040d-4571-b0f2-741c8f17ab41"
    );

    public OperatorFileAppService(
        IOperatorRegistry operatorRegistry,
        IBlobContainer<OperatorFileBlobContainer> blobContainer,
        IHttpContextAccessor httpContextAccessor,
        IOperatorFileRecordRepository recordRepository,
        IConfiguration configuration
    )
    {
        _operatorRegistry = operatorRegistry;
        _blobContainer = blobContainer;
        _httpContextAccessor = httpContextAccessor;
        _recordRepository = recordRepository;
        _configuration = configuration;
    }

    /// <inheritdoc/>
    public async Task<UploadOperatorFileResultDto> UploadAsync(
        Guid projectId,
        Guid operatorId,
        IRemoteStreamContent file
    )
    {
        Check.NotNull(file, nameof(file));

        // ① 查找算子注册信息
        OperatorDescriptor? operatorDescriptor = (
            await _operatorRegistry.GetAllOperatorsAsync()
        ).FirstOrDefault(op => op.Id == operatorId);

        if (operatorDescriptor is null)
        {
            throw new UserFriendlyException(
                $"未找到算子：{operatorId}。请确认算子 GUID 是否正确。"
            );
        }

        OperatorParametersDescriptor? parameters = await _operatorRegistry.GetParametersAsync(
            operatorId
        );

        // ② 根据算子输入端口类型校验文件扩展名
        string originalFileName = file.FileName ?? "unknown";
        string extension = Path.GetExtension(originalFileName).TrimStart('.').ToLowerInvariant();

        ValidateFileExtension(parameters, extension, operatorDescriptor.DisplayName);

        // ③ 保存文件到临时路径并计算 MD5
        string tempFilePath = Path.Combine(
            Path.GetTempPath(),
            $"operator-file-{Guid.NewGuid():N}.upload"
        );
        long fileSizeBytes = 0;
        string md5;

        try
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);

            try
            {
                await using (Stream source = file.GetStream())
                await using (
                    FileStream target = new(
                        tempFilePath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        1024 * 1024,
                        FileOptions.Asynchronous | FileOptions.SequentialScan
                    )
                )
                {
                    while (true)
                    {
                        int read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length));
                        if (read == 0)
                            break;

                        await target.WriteAsync(buffer.AsMemory(0, read));
                        hash.AppendData(buffer, 0, read);
                        fileSizeBytes += read;
                    }
                }

                md5 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            // ④ 保存到 BLOB 存储
            string blobName = BuildBlobName(operatorId, originalFileName);

            await using (
                FileStream blobStream = new(
                    tempFilePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    1024 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan
                )
            )
            {
                await _blobContainer.SaveAsync(blobName, blobStream, overrideExisting: true);
            }

            // ⑤ 生成预览图并保存到 BLOB
            List<PreviewImageDto> previewImages = await GeneratePreviewsAsync(
                operatorId,
                tempFilePath,
                originalFileName
            );

            // ⑥ 写入数据库记录，设置 24 小时过期
            DateTime now = DateTime.Now;
            DateTime expiresAt = now.AddHours(DefaultExpirationHours);

            string previewBlobNames = string.Join(",", previewImages.Select(p => p.BlobName));

            var record = new OperatorFileRecord
            {
                ProjectId = projectId,
                OperatorId = operatorId,
                OperatorDisplayName = operatorDescriptor.DisplayName,
                OriginalFileName = originalFileName,
                BlobName = blobName,
                PreviewBlobNames = previewBlobNames,
                FileSizeBytes = fileSizeBytes,
                Md5 = md5,
                ExpiresAt = expiresAt,
                IsUsed = false,
                CreatedAt = now,
            };

            await _recordRepository.InsertAsync(record);

            return new UploadOperatorFileResultDto
            {
                Success = true,
                ProjectId = projectId,
                OperatorId = operatorId,
                OperatorDisplayName = operatorDescriptor.DisplayName,
                OriginalFileName = originalFileName,
                BlobName = blobName,
                FileSizeBytes = fileSizeBytes,
                Md5 = md5,
                ExpiresAt = expiresAt,
                IsUsed = false,
                PreviewImages = previewImages,
            };
        }
        finally
        {
            DeleteTempFileQuietly(tempFilePath);
        }
    }

    /// <inheritdoc/>
    public async Task<IRemoteStreamContent> GetPreviewAsync(string blobName)
    {
        Check.NotNullOrWhiteSpace(blobName, nameof(blobName));

        Stream? stream = await _blobContainer.GetAsync(blobName);
        if (stream is null)
        {
            throw new UserFriendlyException($"预览图不存在或已过期：{blobName}");
        }

        return new RemoteStreamContent(stream, blobName, "image/png");
    }

    /// <inheritdoc/>
    [HttpGet("download")]
    public async Task<IRemoteStreamContent> DownloadAsync(string blobName)
    {
        Check.NotNullOrWhiteSpace(blobName, nameof(blobName));

        Stream? stream = await _blobContainer.GetAsync(blobName);
        if (stream is null)
        {
            throw new UserFriendlyException($"文件不存在或已过期：{blobName}");
        }

        string fileName = Path.GetFileName(blobName);
        return new RemoteStreamContent(stream, fileName, ResolveContentType(fileName));
    }

    /// <inheritdoc/>
    public async Task ConfirmAsync(ConfirmOperatorFileInput input)
    {
        Check.NotNull(input, nameof(input));
        Check.NotNullOrWhiteSpace(input.BlobName, nameof(input.BlobName));

        OperatorFileRecord? record = await _recordRepository.FindAsync(r =>
            r.BlobName == input.BlobName
            && r.ProjectId == input.ProjectId
            && r.OperatorId == input.OperatorId
        );

        if (record is null)
        {
            throw new UserFriendlyException(
                $"未找到文件记录：{input.BlobName}。文件可能已被清理或记录不存在。"
            );
        }

        if (record.IsUsed)
        {
            return; // 已确认，幂等
        }

        record.MarkAsUsed();
        await _recordRepository.UpdateAsync(record);
    }

    /// <summary>
    /// 根据算子类型生成预览图。
    /// 图片算子生成灰度图；点云不再在上传阶段生成预览图。
    /// </summary>
    private async Task<List<PreviewImageDto>> GeneratePreviewsAsync(
        Guid operatorId,
        string tempFilePath,
        string originalFileName
    )
    {
        string baseName = Path.GetFileNameWithoutExtension(originalFileName);
        string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss_fff");
        string previewPrefix = $"{operatorId:N}/{timestamp}";

        if (operatorId == ReadImageOperatorId)
        {
            // 图片：生成灰度预览图
            return await GenerateImageGrayscalePreviewAsync(tempFilePath, previewPrefix, baseName);
        }

        return new List<PreviewImageDto>();
    }

    /// <summary>
    /// 生成图片的灰度预览图。
    /// </summary>
    private async Task<List<PreviewImageDto>> GenerateImageGrayscalePreviewAsync(
        string tempFilePath,
        string previewPrefix,
        string baseName
    )
    {
        Mat src = Cv2.ImRead(tempFilePath, ImreadModes.Color);
        if (src.Empty())
        {
            src.Dispose();
            return new List<PreviewImageDto>();
        }

        Mat gray = new Mat();
        try
        {
            if (src.Channels() == 1)
            {
                gray = src.Clone();
            }
            else
            {
                Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
            }

            // 编码为 PNG 字节
            Cv2.ImEncode(".png", gray, out byte[] pngBytes);

            string blobName = $"{previewPrefix}_{baseName}_gray.png";

            using MemoryStream ms = new MemoryStream(pngBytes);
            await _blobContainer.SaveAsync(blobName, ms, overrideExisting: true);

            return new List<PreviewImageDto>
            {
                new PreviewImageDto { Label = "灰度图", BlobName = blobName },
            };
        }
        finally
        {
            gray.Dispose();
            src.Dispose();
        }
    }

    /// <summary>
    /// 根据算子输入端口类型校验文件扩展名。
    /// </summary>
    private static void ValidateFileExtension(
        OperatorParametersDescriptor? parameters,
        string extension,
        string operatorDisplayName
    )
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            throw new UserFriendlyException("无法识别上传文件的扩展名。");
        }

        if (parameters is null || parameters.Inputs.Count == 0)
        {
            return;
        }

        // 查找输入端口中的文件路径类型参数，获取其允许的扩展名列表
        foreach (ParameterDescriptor input in parameters.Inputs)
        {
            if (input.ValueLimit is not string[] allowedExtensions)
                continue;

            if (allowedExtensions.Length == 0)
                continue;

            if (
                !allowedExtensions.Any(ext =>
                    ext.TrimStart('.').Equals(extension, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                throw new UserFriendlyException(
                    $"算子「{operatorDisplayName}」不支持文件格式 .{extension}。"
                        + $"支持的格式：{string.Join(", ", allowedExtensions)}。"
                );
            }

            return;
        }
    }

    private static string BuildBlobName(Guid operatorId, string originalFileName)
    {
        string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss_fff");
        string safeFileName = Path.GetFileName(originalFileName);
        return $"{operatorId:N}/{timestamp}_{safeFileName}";
    }

    private static string ResolveContentType(string fileName)
    {
        string extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".bmp" => "image/bmp",
            ".tif" or ".tiff" => "image/tiff",
            ".webp" => "image/webp",
            ".txt" or ".asc" or ".xyz" or ".pts" => "text/plain",
            _ => "application/octet-stream",
        };
    }

    private static void DeleteTempFileQuietly(string? tempFilePath)
    {
        if (string.IsNullOrWhiteSpace(tempFilePath) || !File.Exists(tempFilePath))
            return;

        try
        {
            File.Delete(tempFilePath);
        }
        catch { }
    }

    /// <summary>
    /// 根据当前 HTTP 请求构建预览图的完整下载 URL。
    /// </summary>
    private string BuildPreviewUrl(string blobName)
    {
        HttpContext? httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return $"/api/app/operator-file/preview?blobName={Uri.EscapeDataString(blobName)}";
        }

        string scheme = httpContext.Request.Scheme;
        HostString host = httpContext.Request.Host;

        return $"{scheme}://{host}/api/app/operator-file/preview?blobName={Uri.EscapeDataString(blobName)}";
    }
}
