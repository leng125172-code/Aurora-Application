using System.Text.Json;
using AuroraStruct3D.Plcs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.Workflow.Runtime;

/// <summary>Background PLC trigger evaluator. Matching is edge-triggered in process memory.</summary>
internal sealed class WorkflowPlcTriggerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WorkflowPlcTriggerHostedService> _logger;
    private readonly Dictionary<Guid, bool> _matched = [];
    private readonly Dictionary<Guid, DateTime> _nextReadAttempt = [];
    public WorkflowPlcTriggerHostedService(IServiceScopeFactory scopeFactory, ILogger<WorkflowPlcTriggerHostedService> logger) { _scopeFactory = scopeFactory; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await PollAsync(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "PLC workflow trigger polling failed"); }
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = _scopeFactory.CreateScope();
        var triggers = await scope.ServiceProvider.GetRequiredService<IRepository<WorkflowPlcTrigger, Guid>>().GetListAsync(x => x.IsEnabled, cancellationToken: cancellationToken);
        var tags = scope.ServiceProvider.GetRequiredService<IRepository<PlcTag, Guid>>();
        var accessor = scope.ServiceProvider.GetRequiredService<IPlcTagAccessor>();
        var runtime = scope.ServiceProvider.GetRequiredService<WorkflowRuntimeAppService>();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<WorkflowPlcTrigger, Guid>>();
        foreach (WorkflowPlcTrigger trigger in triggers)
        {
            if (_nextReadAttempt.TryGetValue(trigger.PlcDeviceId, out DateTime nextAttempt)
                && nextAttempt > DateTime.UtcNow)
                continue;
            PlcTag? tag = await tags.FindAsync(trigger.PlcTagId, cancellationToken: cancellationToken);
            if (tag is null || !tag.IsEnabled) { trigger.MarkSkipped("触发点位不存在或已禁用。"); await repository.UpdateAsync(trigger, autoSave: true, cancellationToken); continue; }
            try
            {
                PlcTagValueDto? value = (await accessor.ReadByCodesAsync(trigger.PlcDeviceId, [tag.Code], cancellationToken)).SingleOrDefault();
                bool matches = value is not null && value.Error is null && Matches(value.EngineeringValue, trigger.ExpectedValueJson, trigger.Tolerance);
                bool previous = _matched.GetValueOrDefault(trigger.Id);
                _matched[trigger.Id] = matches;
                if (!matches || previous) continue;
                try
                {
                    await runtime.EnqueueProjectRunAsync(new Dtos.WorkflowProjectRunEnqueueInput { ProjectId = trigger.ProjectId, Name = $"PLC {tag.Code}", StartType = WorkflowProjectRunStartType.Immediate });
                    trigger.MarkTriggered();
                }
                catch (Exception ex) { trigger.MarkSkipped(ex.Message); }
                await repository.UpdateAsync(trigger, autoSave: true, cancellationToken);
            }
            catch (Exception ex)
            {
                // PLC 掉线时每 500ms 轮询会造成连接风暴；同设备冷却 10 秒且不重复写库。
                _nextReadAttempt[trigger.PlcDeviceId] = DateTime.UtcNow.AddSeconds(10);
                trigger.MarkSkipped(ex.Message);
                await repository.UpdateAsync(trigger, autoSave: true, cancellationToken);
                _logger.LogWarning(ex, "PLC workflow trigger read failed for device {DeviceId}; retry suppressed for 10 seconds", trigger.PlcDeviceId);
            }
        }
    }

    private static bool Matches(object? actual, string expectedJson, double tolerance)
    {
        using JsonDocument expected = JsonDocument.Parse(expectedJson);
        JsonElement e = expected.RootElement;
        if (actual is null) return e.ValueKind == JsonValueKind.Null;
        if (actual is IConvertible && e.ValueKind == JsonValueKind.Number && double.TryParse(actual.ToString(), out double a) && e.TryGetDouble(out double b)) return Math.Abs(a - b) <= tolerance;
        if (actual is byte[] bytes && e.ValueKind == JsonValueKind.String) return Convert.ToBase64String(bytes) == e.GetString();
        return string.Equals(actual.ToString(), e.ValueKind == JsonValueKind.String ? e.GetString() : e.GetRawText(), StringComparison.Ordinal);
    }
}
