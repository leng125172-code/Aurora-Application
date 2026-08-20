using System.Collections.Concurrent;

namespace AuroraStruct3D.Realtime;

/// <summary>Tracks active realtime UI connections so hardware polling can idle when nobody is listening.</summary>
public sealed class RealtimeSubscriberTracker
{
    public const string Dashboard = "dashboard";
    public const string KtechMotor = "ktech-motor";
    public const string LeisaiMotor = "leisai-motor";

    private readonly ConcurrentDictionary<string, int> _counts = new();

    public bool HasSubscribers(string channel) => _counts.GetValueOrDefault(channel) > 0;

    public void Connected(string channel) => _counts.AddOrUpdate(channel, 1, (_, count) => count + 1);

    public void Disconnected(string channel)
    {
        _counts.AddOrUpdate(channel, 0, (_, count) => Math.Max(0, count - 1));
    }
}
