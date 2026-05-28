using AuroraStruct3D.ProductModels;

namespace AuroraStruct3D.ProductModels;

/// <summary>
/// 三维文件格式转换引擎接口。
/// 负责将非 PLY/OBJ 格式（STEP/IGES/STL/GLB/GLTF/PCD）转换为 PLY 格式。
/// 当前由 StubConversionEngine 实现（Stub），后期替换为 OCC NativeAOT 实现。
/// </summary>
public interface IConversionEngine
{
    /// <summary>
    /// 将输入文件流转换为 PLY 格式输出流。
    /// </summary>
    /// <param name="input">原始文件流（调用方负责关闭）</param>
    /// <param name="format">原始文件格式</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>PLY 格式的文件流（调用方负责关闭）</returns>
    Task<Stream> ConvertToPlyAsync(
        Stream input,
        ProductModelFormat format,
        CancellationToken cancellationToken = default
    );
}
