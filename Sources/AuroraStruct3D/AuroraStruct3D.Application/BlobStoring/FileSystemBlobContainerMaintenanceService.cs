using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;

namespace AuroraStruct3D.BlobStoring;

/// <summary>
/// 文件系统 BLOB 容器维护辅助服务。
/// 负责解析容器根目录、枚举物理文件以及清理空目录。
/// </summary>
public class FileSystemBlobContainerMaintenanceService : ITransientDependency
{
    private const string BasePathConfigurationKey = "FileSystem.BasePath";
    private const string AppendContainerNameConfigurationKey =
        "FileSystem.AppendContainerNameToBasePath";

    private readonly IBlobContainerConfigurationProvider _blobContainerConfigurationProvider;
    private readonly ICurrentTenant _currentTenant;

    public FileSystemBlobContainerMaintenanceService(
        IBlobContainerConfigurationProvider blobContainerConfigurationProvider,
        ICurrentTenant currentTenant
    )
    {
        _blobContainerConfigurationProvider = blobContainerConfigurationProvider;
        _currentTenant = currentTenant;
    }

    /// <summary>
    /// 获取指定容器在当前租户下的物理根目录。
    /// </summary>
    public string GetContainerRootPath<TContainer>()
    {
        BlobContainerConfiguration configuration =
            _blobContainerConfigurationProvider.Get<TContainer>();
        string? basePath = configuration.GetConfigurationOrDefault<string>(
            BasePathConfigurationKey
        );
        if (string.IsNullOrWhiteSpace(basePath))
        {
            throw new UserFriendlyException(
                "当前 BLOB 容器未配置文件系统存储路径，无法执行孤立记录清理。"
            );
        }

        string containerRootPath = _currentTenant.Id.HasValue
            ? Path.Combine(basePath, "tenants", _currentTenant.Id.Value.ToString("D"))
            : Path.Combine(basePath, "host");

        bool appendContainerName = configuration.GetConfigurationOrDefault(
            AppendContainerNameConfigurationKey,
            true
        );
        if (appendContainerName)
        {
            containerRootPath = Path.Combine(
                containerRootPath,
                BlobContainerNameAttribute.GetContainerName<TContainer>()
            );
        }

        return containerRootPath;
    }

    /// <summary>
    /// 枚举容器目录下的全部物理文件。
    /// </summary>
    public List<FileSystemBlobFile> GetBlobFiles(string containerRootPath)
    {
        if (!Directory.Exists(containerRootPath))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(containerRootPath, "*", SearchOption.AllDirectories)
            .Select(fullPath => new FileSystemBlobFile(
                NormalizeBlobName(Path.GetRelativePath(containerRootPath, fullPath)),
                fullPath
            ))
            .Where(x => !string.IsNullOrWhiteSpace(x.BlobName))
            .ToList();
    }

    /// <summary>
    /// 删除容器目录下的空子目录，不删除容器根目录本身。
    /// </summary>
    public int DeleteEmptyDirectories(string containerRootPath)
    {
        if (!Directory.Exists(containerRootPath))
        {
            return 0;
        }

        int cleaned = 0;
        List<string> directories = Directory
            .EnumerateDirectories(containerRootPath, "*", SearchOption.AllDirectories)
            .OrderByDescending(x => x.Length)
            .ToList();

        foreach (string directory in directories)
        {
            if (Directory.EnumerateFileSystemEntries(directory).Any())
            {
                continue;
            }

            Directory.Delete(directory, recursive: false);
            cleaned++;
        }

        return cleaned;
    }

    /// <summary>
    /// 统一物理文件相对路径对应的 BLOB 键名格式。
    /// </summary>
    public static string NormalizeBlobName(string blobName)
    {
        return blobName.Replace('\\', '/').TrimStart('/');
    }
}

/// <summary>
/// 文件系统 BLOB 文件快照。
/// </summary>
/// <param name="BlobName">标准化后的 BLOB 键名</param>
/// <param name="FullPath">物理文件绝对路径</param>
public sealed record FileSystemBlobFile(string BlobName, string FullPath);
