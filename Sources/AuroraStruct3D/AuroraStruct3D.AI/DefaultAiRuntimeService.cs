using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace AuroraStruct3D.AI;

/// <summary>
/// AI 运行时默认占位实现。
/// 一期用于完成模块接入与依赖注入，后续由平台实现替换。
/// </summary>
public sealed class DefaultAiRuntimeService : IAiRuntimeService
{
    private readonly ConcurrentDictionary<Guid, AiModelLoadStatus> _loadStatuses = new();

    /// <summary>
    /// 获取当前运行时提供程序名称。
    /// </summary>
    /// <returns>默认提供程序名称。</returns>
    public string GetProviderName()
    {
        return "Default";
    }

    /// <summary>
    /// 判断当前运行时是否可用。
    /// </summary>
    /// <returns>一期默认返回 false。</returns>
    public bool IsAvailable()
    {
        return true;
    }

    /// <summary>
    /// 获取当前运行平台能力信息。
    /// </summary>
    public async Task<AiRuntimePlatformInfo> GetPlatformInfoAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (OperatingSystem.IsLinux())
        {
            string compatiblePath = "/proc/device-tree/compatible";
            string compatibleText = string.Empty;

            if (File.Exists(compatiblePath))
            {
                compatibleText = await ReadLinuxCompatibleDescriptionAsync(
                    compatiblePath,
                    cancellationToken
                );
            }

            bool isRockchip = compatibleText.Contains(
                "rockchip",
                StringComparison.OrdinalIgnoreCase
            );
            return new AiRuntimePlatformInfo
            {
                IsSupported = isRockchip,
                OperatingSystem = "Linux",
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                PlatformName = isRockchip ? "Linux + Rockchip" : "Linux",
                UnsupportedReason = isRockchip ? null : "当前 Linux 平台不是瑞芯微平台。",
                CompatibilityDescription = compatibleText,
                SupportedExtensions = isRockchip ? ["rknn", "rkllm", "onnx"] : [],
                SupportsOnnxConversion = isRockchip,
            };
        }

        if (OperatingSystem.IsWindows())
        {
            bool isX64 = RuntimeInformation.ProcessArchitecture == Architecture.X64;
            return new AiRuntimePlatformInfo
            {
                IsSupported = isX64,
                OperatingSystem = "Windows",
                Architecture = isX64 ? "x86_64" : RuntimeInformation.ProcessArchitecture.ToString(),
                PlatformName = isX64 ? "Windows + x86-64" : "Windows",
                UnsupportedReason = isX64 ? null : "当前 Windows 平台不是 x86-64 架构。",
                SupportedExtensions = isX64 ? ["onnx"] : [],
                SupportsOnnxConversion = false,
            };
        }

        return new AiRuntimePlatformInfo
        {
            IsSupported = false,
            OperatingSystem = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            PlatformName = "Unsupported",
            UnsupportedReason = "当前平台不支持 AI 模型运行。",
        };
    }

    /// <summary>
    /// 获取指定模型列表的加载状态。
    /// </summary>
    public Task<Dictionary<Guid, AiModelLoadStatus>> GetLoadStatusesAsync(
        IEnumerable<Guid> modelIds,
        CancellationToken cancellationToken = default
    )
    {
        Dictionary<Guid, AiModelLoadStatus> result = modelIds
            .Distinct()
            .ToDictionary(
                id => id,
                id => _loadStatuses.GetValueOrDefault(id, AiModelLoadStatus.Unloaded)
            );

        return Task.FromResult(result);
    }

    /// <summary>
    /// 加载指定 AI 模型。
    /// </summary>
    /// <param name="model">模型信息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>占位实现默认成功。</returns>
    public Task<bool> LoadModelAsync(AiModel model, CancellationToken cancellationToken = default)
    {
        _loadStatuses[model.Id] = AiModelLoadStatus.Loaded;
        return Task.FromResult(true);
    }

    /// <summary>
    /// 卸载指定 AI 模型。
    /// </summary>
    /// <param name="model">模型信息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>占位实现默认成功。</returns>
    public Task<bool> UnloadModelAsync(AiModel model, CancellationToken cancellationToken = default)
    {
        _loadStatuses[model.Id] = AiModelLoadStatus.Unloaded;
        return Task.FromResult(true);
    }

    /// <summary>
    /// 读取 Linux 设备树 compatible 信息，并将 NUL 分隔内容转换为可展示文本。
    /// </summary>
    private static async Task<string> ReadLinuxCompatibleDescriptionAsync(
        string compatiblePath,
        CancellationToken cancellationToken
    )
    {
        byte[] bytes = await File.ReadAllBytesAsync(compatiblePath, cancellationToken);
        string rawText = Encoding.UTF8.GetString(bytes);

        string[] parts = rawText.Split(
            '\0',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );

        return string.Join(" | ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
    }
}
