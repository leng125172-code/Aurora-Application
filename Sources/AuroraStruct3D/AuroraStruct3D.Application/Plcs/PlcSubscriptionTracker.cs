using System.Collections.Concurrent;

namespace AuroraStruct3D.Plcs;

public sealed class PlcSubscriptionTracker : IPlcSubscriptionTracker
{
    private readonly ConcurrentDictionary<
        string,
        (string ConnectionId, Guid DeviceId)
    > _subscriptions = new();

    public void Add(string connectionId, Guid deviceId, string subscriptionId) =>
        _subscriptions[subscriptionId] = (connectionId, deviceId);

    public void Remove(string subscriptionId) => _subscriptions.TryRemove(subscriptionId, out _);

    public IReadOnlyList<PlcTrackedSubscription> TakeByConnection(string connectionId)
    {
        var result = new List<PlcTrackedSubscription>();
        foreach ((string id, (string owner, Guid deviceId)) in _subscriptions.ToArray())
        {
            if (!string.Equals(owner, connectionId, StringComparison.Ordinal))
                continue;
            if (_subscriptions.TryRemove(id, out _))
                result.Add(new PlcTrackedSubscription(deviceId, id));
        }
        return result;
    }
}
