using AuroraStruct3D.AI.Dtos;

namespace AuroraStruct3D.AI;

/// <summary>
/// AI 模型转换阶段通知器。
/// 由 HttpApi.Host 通过 SignalR Hub 实现，用于向前端实时推送转换快照和阶段变化。
/// </summary>
public interface IAiModelConversionNotifier
{
    /// <summary>
    /// 通知前端指定模型已进入排队阶段。
    /// </summary>
    Task NotifyQueuedAsync(AiModelConversionStateDto state);

    /// <summary>
    /// 通知前端指定模型已开始转换。
    /// </summary>
    Task NotifyStartedAsync(AiModelConversionStateDto state);

    /// <summary>
    /// 通知前端指定模型转换已完成。
    /// </summary>
    Task NotifyFinishedAsync(AiModelConversionStateDto state);
}
