namespace AuroraStruct3D.AI;

/// <summary>
/// AI 运行时服务接口，统一抽象模型加载、卸载与运行状态查询能力。
/// 一期先提供占位实现，后续再接入 RK3588 NPU 与 Windows GPU。
/// </summary>
public interface IAiRuntimeService
{
    /// <summary>
    /// 获取当前运行时提供程序名称。
    /// </summary>
    /// <returns>提供程序名称。</returns>
    string GetProviderName();

    /// <summary>
    /// 判断当前运行时是否可用。
    /// </summary>
    /// <returns>可用返回 true，否则返回 false。</returns>
    bool IsAvailable();

    /// <summary>
    /// 获取当前运行平台能力信息。
    /// </summary>
    Task<AiRuntimePlatformInfo> GetPlatformInfoAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取指定模型列表的运行时加载状态。
    /// </summary>
    Task<Dictionary<Guid, AiModelLoadStatus>> GetLoadStatusesAsync(
        IEnumerable<Guid> modelIds,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 加载指定 AI 模型。
    /// </summary>
    /// <param name="model">模型信息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功返回 true，否则返回 false。</returns>
    Task<bool> LoadModelAsync(AiModel model, CancellationToken cancellationToken = default);

    /// <summary>
    /// 卸载指定 AI 模型。
    /// </summary>
    /// <param name="model">模型信息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功返回 true，否则返回 false。</returns>
    Task<bool> UnloadModelAsync(AiModel model, CancellationToken cancellationToken = default);
}
