using System.Collections.Concurrent;

namespace AuroraStruct3D.Plcs;

public sealed class PlcConnectionManager : IPlcConnectionManager, IAsyncDisposable
{
    private sealed class ReconnectBackoffException : InvalidOperationException
    {
        public ReconnectBackoffException(DateTime nextAttempt)
            : base($"PLC 正在等待重连退避，将在 {nextAttempt:O} 后再次尝试。") { }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public IPlcConnection? Connection { get; set; }
        public PlcConnectionStatus Status { get; set; }
        public DateTime LastUsedAt { get; set; }
        public PlcConnectionOptions? Options { get; set; }
        public string? DriverId { get; set; }
        public int ReconnectDelayMs { get; set; }
        public DateTime NextReconnectAt { get; set; }
    }

    private readonly IPlcDriverRegistry _registry;
    private readonly ConcurrentDictionary<Guid, Entry> _entries = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _monitor;

    public PlcConnectionManager(IPlcDriverRegistry registry)
    {
        _registry = registry;
        _monitor = MonitorAsync(_shutdown.Token);
    }

    public async Task<IPlcConnection> GetOrConnectAsync(
        PlcConnectionOptions options,
        string driverId,
        CancellationToken cancellationToken = default
    )
    {
        Entry entry = _entries.GetOrAdd(options.DeviceId, _ => new Entry());
        await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            entry.LastUsedAt = DateTime.UtcNow;
            entry.Options = options;
            entry.DriverId = driverId;
            if (
                entry.Connection != null
                && await entry.Connection.HealthCheckAsync(cancellationToken).ConfigureAwait(false)
            )
            {
                entry.ReconnectDelayMs = options.ReconnectInitialMs;
                entry.NextReconnectAt = DateTime.MinValue;
                return entry.Connection;
            }

            if (entry.NextReconnectAt > DateTime.UtcNow)
                throw new ReconnectBackoffException(entry.NextReconnectAt);

            if (entry.Connection != null)
                await entry.Connection.DisposeAsync().ConfigureAwait(false);

            entry.Status = PlcConnectionStatus.Connecting;
            entry.Connection = await _registry
                .GetRequired(driverId)
                .ConnectAsync(options, cancellationToken)
                .ConfigureAwait(false);
            entry.Status = PlcConnectionStatus.Connected;
            entry.ReconnectDelayMs = options.ReconnectInitialMs;
            entry.NextReconnectAt = DateTime.MinValue;
            return entry.Connection;
        }
        catch (ReconnectBackoffException)
        {
            throw;
        }
        catch
        {
            entry.Status = PlcConnectionStatus.Faulted;
            int delay = entry.ReconnectDelayMs <= 0 ? options.ReconnectInitialMs : entry.ReconnectDelayMs;
            entry.NextReconnectAt = DateTime.UtcNow.AddMilliseconds(delay);
            entry.ReconnectDelayMs = Math.Min(options.ReconnectMaxMs, Math.Max(delay * 2, options.ReconnectInitialMs));
            throw;
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    public async Task DisconnectAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default
    )
    {
        if (!_entries.TryRemove(deviceId, out Entry? entry))
            return;
        await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (entry.Connection != null)
                await entry.Connection.DisposeAsync().ConfigureAwait(false);
            entry.Status = PlcConnectionStatus.Disconnected;
        }
        finally
        {
            entry.Gate.Release();
            entry.Gate.Dispose();
        }
    }

    public PlcConnectionStatus GetStatus(Guid deviceId) =>
        _entries.TryGetValue(deviceId, out Entry? entry)
            ? entry.Status
            : PlcConnectionStatus.Disconnected;

    public void Touch(Guid deviceId)
    {
        if (_entries.TryGetValue(deviceId, out Entry? entry))
            entry.LastUsedAt = DateTime.UtcNow;
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        try
        {
            await _monitor.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        foreach (Guid id in _entries.Keys.ToArray())
            await DisconnectAsync(id).ConfigureAwait(false);
        _shutdown.Dispose();
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach ((Guid id, Entry entry) in _entries.ToArray())
            {
                if (
                    entry.Options == null
                    || entry.DriverId == null
                    || !await entry.Gate.WaitAsync(0, cancellationToken).ConfigureAwait(false)
                )
                    continue;
                try
                {
                    if (
                        entry.Status == PlcConnectionStatus.Connected
                        && DateTime.UtcNow - entry.LastUsedAt
                            > TimeSpan.FromMilliseconds(entry.Options.IdleTimeoutMs)
                    )
                    {
                        if (entry.Connection != null)
                            await entry.Connection.DisposeAsync().ConfigureAwait(false);
                        entry.Connection = null;
                        entry.Status = PlcConnectionStatus.Disconnected;
                        _entries.TryRemove(id, out _);
                        continue;
                    }

                    bool healthy =
                        entry.Connection != null
                        && await entry.Connection
                            .HealthCheckAsync(cancellationToken)
                            .ConfigureAwait(false);
                    if (healthy)
                    {
                        entry.ReconnectDelayMs = entry.Options.ReconnectInitialMs;
                        continue;
                    }

                    entry.Status = PlcConnectionStatus.Reconnecting;
                    if (entry.Connection != null)
                        await entry.Connection.DisposeAsync().ConfigureAwait(false);
                    entry.Connection = null;
                    try
                    {
                        await Task.Delay(entry.ReconnectDelayMs, cancellationToken)
                            .ConfigureAwait(false);
                        entry.Connection = await _registry
                            .GetRequired(entry.DriverId)
                            .ConnectAsync(entry.Options, cancellationToken)
                            .ConfigureAwait(false);
                        entry.Status = PlcConnectionStatus.Connected;
                        entry.ReconnectDelayMs = entry.Options.ReconnectInitialMs;
                    }
                    catch when (!cancellationToken.IsCancellationRequested)
                    {
                        entry.Status = PlcConnectionStatus.Faulted;
                        entry.NextReconnectAt = DateTime.UtcNow.AddMilliseconds(entry.ReconnectDelayMs);
                        entry.ReconnectDelayMs = Math.Min(
                            entry.Options.ReconnectMaxMs,
                            entry.ReconnectDelayMs * 2
                        );
                    }
                }
                finally
                {
                    entry.Gate.Release();
                }
            }
        }
    }
}
