using System.Buffers;
using System.Security.Cryptography;
using AuroraStruct3D.OperatorFile.Dtos;
using AuroraStruct3D.OpenCV.Registry;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;

namespace AuroraStruct3D.OperatorFile;

/// <summary>
/// 算子文件上传应用服务实现。
/// 根据算子 GUID 校验输入端口类型与文件格式匹配后，将文件保存到 BLOB 存储。
/// </summary>
[Authorize]
public class OperatorFileAppService : AuroraStruct3DAppService, IOperatorFileAppService
{
    private readonly IOperatorRegistry _operatorRegistry;
    private readonly IBlobContainer<OperatorFileBlobContainer> _blobContainer;

    public OperatorFileAppService(
        IOperatorRegistry operatorRegistry,
        IBlobContainer<OperatorFileBlobContainer> blobContainer
    )
    {
        _operatorRegistry = operatorRegistry;
        _blobContainer = blobContainer;
    }

    /// <inheritdoc/>
    public async Task<UploadOperatorFileResultDto> UploadAsync(
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
            throw new UserFriendlyException($"未找到算子：{operatorId}。请确认算子 GUID 是否正确。");
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

            return new UploadOperatorFileResultDto
            {
                Success = true,
                OperatorId = operatorId,
                OperatorDisplayName = operatorDescriptor.DisplayName,
                OriginalFileName = originalFileName,
                BlobName = blobName,
                FileSizeBytes = fileSizeBytes,
                Md5 = md5,
            };
        }
        finally
        {
            DeleteTempFileQuietly(tempFilePath);
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
}