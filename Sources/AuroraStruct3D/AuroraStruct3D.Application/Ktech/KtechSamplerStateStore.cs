using System.Collections.Concurrent;

namespace AuroraStruct3D.Ktech;

/// <summary>
/// KTECH 电机采样状态存储（单例）。
/// 记录每个电机轴的实时采样开关，供 <see cref="KtechSamplerHostedService"/> 和
/// <see cref="KtechMotorAppService"/> 共享读写。
/// </summary>
public class KtechSamplerStateStore
{
    /// <summary>各轴采样开关；Key=AxisId，Value=是否启用（默认 true）。</summary>
    private readonly ConcurrentDictionary<Guid, bool> _pollingEnabled = new();

    /// <summary>设置指定轴的采样开关。</summary>
    /// <param name="axisId">电机轴 UUID</param>
    /// <param name="enabled">true=启用，false=暂停</param>
    public void SetPollingEnabled(Guid axisId, bool enabled) => _pollingEnabled[axisId] = enabled;

    /// <summary>
    /// 获取指定轴的采样开关。
    /// 若该轴尚未显式设置，返回默认值 <c>false</c>（停止），需前端主动开启。
    /// </summary>
    /// <param name="axisId">电机轴 UUID</param>
    public bool IsPollingEnabled(Guid axisId) =>
        _pollingEnabled.GetValueOrDefault(axisId, defaultValue: false);
}
