using System.Buffers;
using System.Security.Cryptography;
using AuroraStruct3D.OpenCV.Registry;
using AuroraStruct3D.OperatorFile.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using OpenCvSharp;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;

namespace AuroraStruct3D.OperatorFile;

/// <summary>
/// 算子文件上传应用服务实现。
/// 根据算子 GUID 校验输入端口类型与文件格式匹配后，将文件保存到 BLOB 存储，
/// 并自动生成预览图（图片→灰度图，点云→三视图灰度图）。
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

    /// <summary>文件默认有效期（小时）。</summary>
    private const int DefaultExpirationHours = 24;

    /// <summary>read_image 算子的 GUID。</summary>
    private static readonly Guid ReadImageOperatorId = Guid.Parse(
        "7544f3f3-040d-4571-b0f2-741c8f17ab41"
    );

    /// <summary>read_point_cloud 算子的 GUID。</summary>
    private static readonly Guid ReadPointCloudOperatorId = Guid.Parse(
        "a1b2c3d4-e5f6-7890-abcd-ef1234567890"
    );

    public OperatorFileAppService(
        IOperatorRegistry operatorRegistry,
        IBlobContainer<OperatorFileBlobContainer> blobContainer,
        IHttpContextAccessor httpContextAccessor,
        IOperatorFileRecordRepository recordRepository
    )
    {
        _operatorRegistry = operatorRegistry;
        _blobContainer = blobContainer;
        _httpContextAccessor = httpContextAccessor;
        _recordRepository = recordRepository;
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

            string previewBlobNames = string.Join(
                ",",
                previewImages.Select(p => ExtractBlobNameFromUrl(p.DownloadUrl))
            );

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
    /// 从预览图下载 URL 中提取 BLOB 名称。
    /// </summary>
    private static string ExtractBlobNameFromUrl(string downloadUrl)
    {
        int index = downloadUrl.IndexOf("blobName=", StringComparison.Ordinal);
        if (index < 0)
            return string.Empty;

        string encoded = downloadUrl[(index + "blobName=".Length)..];
        int ampIndex = encoded.IndexOf('&');
        if (ampIndex >= 0)
            encoded = encoded[..ampIndex];

        return Uri.UnescapeDataString(encoded);
    }

    /// <summary>
    /// 根据算子类型生成预览图。
    /// 图片算子生成灰度图，点云算子生成三视图灰度图。
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

        if (operatorId == ReadPointCloudOperatorId)
        {
            // 点云：生成三视图灰度预览
            return await GeneratePointCloudThreeViewPreviewsAsync(
                tempFilePath,
                previewPrefix,
                baseName
            );
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

            string downloadUrl = BuildPreviewUrl(blobName);

            return new List<PreviewImageDto>
            {
                new PreviewImageDto { Label = "灰度图", DownloadUrl = downloadUrl },
            };
        }
        finally
        {
            gray.Dispose();
            src.Dispose();
        }
    }

    /// <summary>
    /// 生成点云的三视图灰度预览图（俯视、正视、侧视）。
    /// </summary>
    private async Task<List<PreviewImageDto>> GeneratePointCloudThreeViewPreviewsAsync(
        string tempFilePath,
        string previewPrefix,
        string baseName
    )
    {
        // 读取点云
        (Mat pointCloud, _) = ReadPointCloudFile(tempFilePath);

        if (pointCloud is null || pointCloud.Empty())
        {
            pointCloud?.Dispose();
            return new List<PreviewImageDto>();
        }

        try
        {
            int pointCount = pointCloud.Rows;

            // 提取坐标范围
            float minX = float.MaxValue,
                maxX = float.MinValue;
            float minY = float.MaxValue,
                maxY = float.MinValue;
            float minZ = float.MaxValue,
                maxZ = float.MinValue;

            for (int i = 0; i < pointCount; i++)
            {
                float x = pointCloud.Get<float>(i, 0);
                float y = pointCloud.Get<float>(i, 1);
                float z = pointCloud.Get<float>(i, 2);

                if (x < minX)
                    minX = x;
                if (x > maxX)
                    maxX = x;
                if (y < minY)
                    minY = y;
                if (y > maxY)
                    maxY = y;
                if (z < minZ)
                    minZ = z;
                if (z > maxZ)
                    maxZ = z;
            }

            float rangeX = maxX - minX;
            float rangeY = maxY - minY;
            float rangeZ = maxZ - minZ;

            const int imageSize = 512;

            // 俯视图（XY 平面，从上往下看）：X 横轴，Y 纵轴
            var topView = RenderPointCloudView(
                pointCloud,
                pointCount,
                imageSize,
                minX,
                maxX,
                rangeX,
                minY,
                maxY,
                rangeY,
                0,
                1
            );

            // 正视图（XZ 平面，从前往后看）：X 横轴，Z 纵轴
            var frontView = RenderPointCloudView(
                pointCloud,
                pointCount,
                imageSize,
                minX,
                maxX,
                rangeX,
                minZ,
                maxZ,
                rangeZ,
                0,
                2
            );

            // 侧视图（YZ 平面，从右往左看）：Y 横轴，Z 纵轴
            var sideView = RenderPointCloudView(
                pointCloud,
                pointCount,
                imageSize,
                minY,
                maxY,
                rangeY,
                minZ,
                maxZ,
                rangeZ,
                1,
                2
            );

            var result = new List<PreviewImageDto>();

            result.Add(
                await SavePreviewBlobAsync(topView, previewPrefix, baseName, "top", "俯视图")
            );
            result.Add(
                await SavePreviewBlobAsync(frontView, previewPrefix, baseName, "front", "正视图")
            );
            result.Add(
                await SavePreviewBlobAsync(sideView, previewPrefix, baseName, "side", "侧视图")
            );

            return result;
        }
        finally
        {
            pointCloud.Dispose();
        }
    }

    /// <summary>
    /// 将点云投影到指定两轴平面，渲染为灰度图像。
    /// </summary>
    private static Mat RenderPointCloudView(
        Mat pointCloud,
        int pointCount,
        int imageSize,
        float minA,
        float maxA,
        float rangeA,
        float minB,
        float maxB,
        float rangeB,
        int colA,
        int colB
    )
    {
        // 创建累加器（浮点精度，避免多次叠加丢失暗部细节）
        float[,] accumulator = new float[imageSize, imageSize];

        // 缩放因子
        float scaleA = (rangeA > 0) ? (imageSize - 1) / rangeA : 1f;
        float scaleB = (rangeB > 0) ? (imageSize - 1) / rangeB : 1f;

        float invScaleA = 1f / scaleA;
        float invScaleB = 1f / scaleB;

        for (int i = 0; i < pointCount; i++)
        {
            float a = pointCloud.Get<float>(i, colA);
            float b = pointCloud.Get<float>(i, colB);

            int ia = (int)((a - minA) * scaleA);
            int ib = (int)((b - minB) * scaleB);

            // 反向映射：a = minA + ia * invScaleA，b = minB + ib * invScaleB
            // 点到像素中心的距离越近，权重越大（双线性插值风格）
            float fa = (a - minA) * scaleA;
            float fb = (b - minB) * scaleB;

            int ia0 = (int)Math.Floor(fa);
            int ib0 = (int)Math.Floor(fb);
            int ia1 = ia0 + 1;
            int ib1 = ib0 + 1;

            float wa1 = fa - ia0;
            float wb1 = fb - ib0;
            float wa0 = 1f - wa1;
            float wb0 = 1f - wb1;

            AddWeight(accumulator, ia0, ib0, imageSize, wa0 * wb0);
            AddWeight(accumulator, ia1, ib0, imageSize, wa1 * wb0);
            AddWeight(accumulator, ia0, ib1, imageSize, wa0 * wb1);
            AddWeight(accumulator, ia1, ib1, imageSize, wa1 * wb1);
        }

        // 找到最大值，用于归一化
        float maxVal = 0;
        for (int y = 0; y < imageSize; y++)
        for (int x = 0; x < imageSize; x++)
            if (accumulator[y, x] > maxVal)
                maxVal = accumulator[y, x];

        // 创建灰度图
        Mat gray = new Mat(imageSize, imageSize, MatType.CV_8UC1);

        if (maxVal > 0)
        {
            float invMax = 255f / maxVal;
            for (int y = 0; y < imageSize; y++)
            for (int x = 0; x < imageSize; x++)
            {
                byte val = (byte)Math.Clamp(accumulator[y, x] * invMax, 0, 255);
                gray.Set(y, x, val);
            }
        }
        else
        {
            gray.SetTo(Scalar.Black);
        }

        return gray;
    }

    private static void AddWeight(float[,] acc, int x, int y, int size, float weight)
    {
        if (x >= 0 && x < size && y >= 0 && y < size)
            acc[y, x] += weight;
    }

    /// <summary>
    /// 编码灰度 Mat 为 PNG 并保存到 BLOB 存储。
    /// </summary>
    private async Task<PreviewImageDto> SavePreviewBlobAsync(
        Mat gray,
        string previewPrefix,
        string baseName,
        string viewSuffix,
        string label
    )
    {
        Cv2.ImEncode(".png", gray, out byte[] pngBytes);
        gray.Dispose();

        string blobName = $"{previewPrefix}_{baseName}_{viewSuffix}.png";

        using MemoryStream ms = new MemoryStream(pngBytes);
        await _blobContainer.SaveAsync(blobName, ms, overrideExisting: true);

        string downloadUrl = BuildPreviewUrl(blobName);

        return new PreviewImageDto { Label = label, DownloadUrl = downloadUrl };
    }

    /// <summary>
    /// 读取点云文件，返回坐标 Mat 和可选的 Mat? colors。
    /// 支持 PLY、PCD、XYZ、TXT、ASC、PTS 格式。
    /// </summary>
    private static (Mat pointCloud, Mat? colors) ReadPointCloudFile(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".ply" => ReadPlyFile(filePath),
            ".pcd" => ReadPcdFile(filePath),
            ".xyz" or ".txt" or ".asc" => ReadXyzFile(filePath),
            ".pts" => ReadPtsFile(filePath),
            _ => (new Mat(), null),
        };
    }

    private static (Mat, Mat?) ReadPlyFile(string filePath)
    {
        var points = new List<float[]>();
        bool hasNormal = false;
        int vertexCount = 0;

        using var reader = new StreamReader(filePath);
        string? line;
        bool inHeader = true;

        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (inHeader)
            {
                if (line.StartsWith("element vertex", StringComparison.OrdinalIgnoreCase))
                    vertexCount = int.Parse(line.Split(' ')[^1]);
                else if (line.StartsWith("property", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(' ');
                    string propName = parts[^1].ToLower();
                    if (propName is "nx" or "ny" or "nz")
                        hasNormal = true;
                }
                else if (line == "end_header")
                    inHeader = false;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                var values = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (values.Length >= 3)
                {
                    int columnCount = 3 + (hasNormal ? 3 : 0);
                    var point = new float[columnCount];
                    point[0] = float.Parse(values[0]);
                    point[1] = float.Parse(values[1]);
                    point[2] = float.Parse(values[2]);
                    if (hasNormal && values.Length >= 6 + 3)
                    {
                        point[3] = float.Parse(values[6]);
                        point[4] = float.Parse(values[7]);
                        point[5] = float.Parse(values[8]);
                    }
                    points.Add(point);
                }
                if (vertexCount > 0 && points.Count >= vertexCount)
                    break;
            }
        }

        return (ConvertToMat(points), null);
    }

    private static (Mat, Mat?) ReadPcdFile(string filePath)
    {
        var points = new List<float[]>();
        int pointCount = 0;

        using var reader = new StreamReader(filePath);
        string? line;
        bool inData = false;

        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.StartsWith("POINTS", StringComparison.OrdinalIgnoreCase))
                pointCount = int.Parse(line.Split(' ')[^1]);
            else if (line.StartsWith("DATA", StringComparison.OrdinalIgnoreCase))
                inData = true;
            else if (inData && !string.IsNullOrWhiteSpace(line))
            {
                var values = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (values.Length >= 3)
                {
                    var point = new float[values.Length];
                    for (int i = 0; i < values.Length; i++)
                        point[i] = float.Parse(values[i]);
                    points.Add(point);
                }
                if (pointCount > 0 && points.Count >= pointCount)
                    break;
            }
        }

        return (ConvertToMat(points), null);
    }

    private static (Mat, Mat?) ReadXyzFile(string filePath)
    {
        var points = new List<float[]>();

        using var reader = new StreamReader(filePath);
        string? line;

        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                continue;

            var values = line.Split(
                new[] { ' ', '\t', ',' },
                StringSplitOptions.RemoveEmptyEntries
            );
            if (values.Length >= 3)
            {
                var point = new float[values.Length];
                for (int i = 0; i < values.Length; i++)
                    point[i] = float.Parse(values[i]);
                points.Add(point);
            }
        }

        return (ConvertToMat(points), null);
    }

    private static (Mat, Mat?) ReadPtsFile(string filePath)
    {
        var points = new List<float[]>();

        using var reader = new StreamReader(filePath);
        string? line;
        bool firstLine = true;
        int pointCount = 0;

        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;
            if (firstLine)
            {
                pointCount = int.Parse(line);
                firstLine = false;
                continue;
            }
            var values = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (values.Length >= 3)
            {
                var point = new float[3];
                point[0] = float.Parse(values[0]);
                point[1] = float.Parse(values[1]);
                point[2] = float.Parse(values[2]);
                points.Add(point);
            }
            if (pointCount > 0 && points.Count >= pointCount)
                break;
        }

        return (ConvertToMat(points), null);
    }

    private static Mat ConvertToMat(List<float[]> points)
    {
        if (points.Count == 0)
            return new Mat();

        int columns = points[0].Length;
        Mat mat = new Mat(points.Count, columns, MatType.CV_32FC1);
        for (int i = 0; i < points.Count; i++)
        for (int j = 0; j < columns; j++)
            mat.Set(i, j, points[i][j]);
        return mat;
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
