using System.Collections.Concurrent;

namespace AuroraStruct3D.Leisai;

/// <summary>
/// 雷赛实时采样开关状态存储（按 axisId 维度记录是否启用周期采样）。
/// 默认未注册的轴视为停止采样，需前端主动开启。
/// </summary>
public class LeisaiSamplerStateStore
{
    private readonly ConcurrentDictionary<Guid, bool> _states = new();

    /// <summary>查询指定轴是否启用采样（未设置时默认 false）。</summary>
    public bool IsPollingEnabled(Guid axisId) =>
        _states.TryGetValue(axisId, out bool enabled) && enabled;

    /// <summary>设置指定轴的采样开关。</summary>
    public void SetPollingEnabled(Guid axisId, bool enabled) => _states[axisId] = enabled;
}
