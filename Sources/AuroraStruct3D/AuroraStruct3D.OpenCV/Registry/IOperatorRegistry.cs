namespace AuroraStruct3D.OpenCV.Registry;

/// <summary>
/// 算子注册表服务接口。
/// <para>
/// 提供两类查询能力：
/// <list type="number">
///   <item>获取全量算子描述列表（含 GUID、Category、DisplayName、Description）</item>
///   <item>根据算子 GUID 获取该算子的完整入参和出参描述</item>
/// </list>
/// 结果在程序启动时自动扫描程序集并写入 Redis，后续请求直接读缓存。
/// </para>
/// </summary>
public interface IOperatorRegistry
{
    /// <summary>
    /// 获取所有已注册算子的描述列表，结果按 Category / DisplayName 排序。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>算子描述列表，顺序固定。</returns>
    Task<IReadOnlyList<OperatorDescriptor>> GetAllOperatorsAsync(
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 根据算子 GUID 获取其入参和出参完整描述。
    /// </summary>
    /// <param name="operatorId">算子唯一标识（来自 [Guid] 特性）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>端口描述对象；若找不到对应算子则返回 null。</returns>
    Task<OperatorParametersDescriptor?> GetParametersAsync(
        Guid operatorId,
        CancellationToken cancellationToken = default
    );
}
