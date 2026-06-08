using AuroraStruct3D.AI.Dtos;

namespace AuroraStruct3D.Hubs;

/// <summary>
/// AI 模型转换 SignalR Hub 的客户端推送接口。
/// </summary>
public interface IAiModelConversionHub
{
    /// <summary>
    /// 推送当前所有活动转换的快照。
    /// </summary>
    Task ReceiveActiveConversionsSnapshotAsync(List<AiModelConversionStateDto> states);

    /// <summary>
    /// 推送某个模型已进入排队阶段。
    /// </summary>
    Task ReceiveConversionQueuedAsync(AiModelConversionStateDto state);

    /// <summary>
    /// 推送某个模型已开始转换。
    /// </summary>
    Task ReceiveConversionStartedAsync(AiModelConversionStateDto state);

    /// <summary>
    /// 推送某个模型转换已完成。
    /// </summary>
    Task ReceiveConversionFinishedAsync(AiModelConversionStateDto state);
}
