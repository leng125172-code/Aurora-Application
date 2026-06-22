using System.Reflection;

namespace AuroraStruct3D.OpenCV.Registry;

/// <summary>
/// 需要扫描的程序集列表，通过 <see cref="OpenCVModule.AddOpenCVServices"/> 注册。
/// 使用专用包装类型避免与 DI 容器中其他 <see cref="Assembly"/> 注册冲突。
/// </summary>
internal sealed class OperatorScanAssemblies
{
    /// <summary>去重后的程序集列表，始终包含本程序集（AuroraStruct3D.OpenCV）。</summary>
    public IReadOnlyList<Assembly> Items { get; }

    public OperatorScanAssemblies(IEnumerable<Assembly> assemblies)
    {
        Items = assemblies.Distinct().ToList().AsReadOnly();
    }
}
