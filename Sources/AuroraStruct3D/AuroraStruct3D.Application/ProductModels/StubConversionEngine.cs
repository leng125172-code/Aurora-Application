using AuroraStruct3D.ProductModels;
using Microsoft.Extensions.Logging;

namespace AuroraStruct3D.ProductModels;

/// <summary>
/// 转换引擎的临时 Stub 实现（已停用，仅保留作参考）。
/// 当前由 InProcessConversionEngine 替代（支持 STL/GLB/PCD → PLY 的纯托管代码转换）。
/// STEP/IGES 转换仍等待 OCC NativeAOT 封装完成。
/// </summary>
public class StubConversionEngine : IConversionEngine
{
    private readonly ILogger<StubConversionEngine> _logger;

    public StubConversionEngine(ILogger<StubConversionEngine> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<Stream> ConvertToPlyAsync(
        Stream input,
        ProductModelFormat format,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogWarning(
            "[StubConversionEngine] 格式 {Format} 的转换尚未实现（OCC NativeAOT wrapper 待接入）。",
            format
        );

        throw new NotSupportedException(
            $"格式 {format} 的转换引擎尚未实现，请等待 OCC NativeAOT 封装完成后再使用此功能。"
        );
    }
}
