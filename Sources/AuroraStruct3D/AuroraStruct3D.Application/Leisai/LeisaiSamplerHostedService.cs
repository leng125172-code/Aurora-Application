using AuroraStruct3D.Leisai.Dtos;
using AuroraStruct3D.Motors;
using AuroraStruct3D.RS485;
using AuroraStruct3D.RS485.Leisai;
using AuroraStruct3D.RS485.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AuroraStruct3D.Leisai;

/// <summary>
/// 雷赛 iCL-RS 实时数据采集后台服务。
/// 与 KTECH Sampler 不同：按 <see cref="IRS485Port"/> 分组并行采样（不同串口可并发，
/// 同串口由 RS485Port 自带的 SemaphoreSlim 序列化）。
/// </summary>
public class LeisaiSamplerHostedService : BackgroundService
{
    /// <summary>采样周期（毫秒），约 4Hz。</summary>
    private const int SamplePeriodMs = 250;

    /// <summary>每隔多少个采样周期写一次数据库（250ms × 12 ≈ 3 秒）。</summary>
    private const int DbUpdateEvery = 12;

    /// <summary>轨迹滑动窗口最大点数（每次推送完整序列给前端 3D 图表）。</summary>
    private const int MaxTracePoints = 1200;

    /// <summary>启动延迟（毫秒），等待 MotorControlService 初始化。</summary>
    private const int StartupDelayMs = 5000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMotorControlService _motorControlService;
    private readonly ILeisaiMotorNotifier _notifier;
    private readonly LeisaiSamplerStateStore _samplerStateStore;
    private readonly ILogger<LeisaiSamplerHostedService> _logger;

    /// <summary>各轴累计成功采样次数，用于节流写库。</summary>
    private readonly Dictionary<Guid, int> _sampleCounters = new();

    /// <summary>各轴轨迹滑动窗口（实际位置 / 指令位置 / 速度），最多 MaxTracePoints 点。</summary>
    private readonly Dictionary<
        Guid,
        (List<int> Actual, List<int> Command, List<int> Speed)
    > _traceWindows = new();

    public LeisaiSamplerHostedService(
        IServiceScopeFactory scopeFactory,
        IMotorControlService motorControlService,
        ILeisaiMotorNotifier notifier,
        LeisaiSamplerStateStore samplerStateStore,
        ILogger<LeisaiSamplerHostedService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _motorControlService = motorControlService;
        _notifier = notifier;
        _samplerStateStore = samplerStateStore;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelayMs, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _logger.LogInformation("[Leisai-Sampler] 启动，采样周期 {Period} ms", SamplePeriodMs);

        Dictionary<int, Guid> slaveToAxisId = new();
        DateTime lastMapRefreshUtc = DateTime.MinValue;
        TimeSpan mapRefreshInterval = TimeSpan.FromSeconds(10);

        while (!stoppingToken.IsCancellationRequested)
        {
            DateTime cycleStart = DateTime.UtcNow;

            try
            {
                if (DateTime.UtcNow - lastMapRefreshUtc > mapRefreshInterval)
                {
                    slaveToAxisId = await BuildSlaveToAxisIdMapAsync(stoppingToken);
                    lastMapRefreshUtc = DateTime.UtcNow;
                }

                IReadOnlyList<LeisaiMotorDriver> drivers = _motorControlService.LeisaiDrivers;

                // 按 RS485 端口分组，组间并行、组内串行（端口已有 SemaphoreSlim 保护）
                IEnumerable<IGrouping<IRS485Port, LeisaiMotorDriver>> groups = drivers.GroupBy(
                    GetPortOf
                );

                List<Task> portTasks = new();
                foreach (IGrouping<IRS485Port, LeisaiMotorDriver> portGroup in groups)
                {
                    portTasks.Add(SamplePortGroupAsync(portGroup, slaveToAxisId, stoppingToken));
                }
                await Task.WhenAll(portTasks);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Leisai-Sampler] 采样周期发生异常");
            }

            TimeSpan elapsed = DateTime.UtcNow - cycleStart;
            int remainMs = SamplePeriodMs - (int)elapsed.TotalMilliseconds;
            if (remainMs > 0)
            {
                try
                {
                    await Task.Delay(remainMs, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("[Leisai-Sampler] 已停止");
    }

    /// <summary>同一端口内串行采样所有驱动并推送。</summary>
    private async Task SamplePortGroupAsync(
        IEnumerable<LeisaiMotorDriver> drivers,
        Dictionary<int, Guid> slaveToAxisId,
        CancellationToken ct
    )
    {
        foreach (LeisaiMotorDriver driver in drivers)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            if (!slaveToAxisId.TryGetValue(driver.SlaveId, out Guid axisId))
            {
                continue;
            }

            if (!_samplerStateStore.IsPollingEnabled(axisId))
            {
                continue;
            }

            try
            {
                LeisaiStateSnapshot raw = await driver.SampleStateAsync(ct);
                LeisaiStateSnapshotDto dto = LeisaiMotorAppService.MapSnapshot(axisId, raw);
                await _notifier.NotifyStateAsync(dto);

                // 每 DbUpdateEvery 次成功采样写一次数据库，同步更新设备管理页的位置/速度
                if (raw.IsSuccess)
                {
                    _sampleCounters.TryGetValue(axisId, out int cnt);
                    _sampleCounters[axisId] = cnt + 1;
                    if ((cnt + 1) % DbUpdateEvery == 0)
                    {
                        await UpdateAxisStatusAsync(axisId, raw, ct);
                    }

                    // 追加到轨迹滑动窗口
                    if (
                        !_traceWindows.TryGetValue(
                            axisId,
                            out (List<int> Actual, List<int> Command, List<int> Speed) win
                        )
                    )
                    {
                        win = (new List<int>(), new List<int>(), new List<int>());
                        _traceWindows[axisId] = win;
                    }
                    win.Actual.Add(raw.ActualPosition);
                    win.Command.Add(raw.CommandPosition);
                    win.Speed.Add(raw.EffectiveSpeed);
                    if (win.Actual.Count > MaxTracePoints)
                    {
                        int excess = win.Actual.Count - MaxTracePoints;
                        win.Actual.RemoveRange(0, excess);
                        win.Command.RemoveRange(0, excess);
                        win.Speed.RemoveRange(0, excess);
                    }

                    // 推送完整轨迹序列（前端用于 3D 相位轨迹图）
                    await _notifier.NotifyTraceAsync(
                        new LeisaiTraceDto
                        {
                            AxisId = axisId,
                            ActualPositions = win.Actual.ToArray(),
                            CommandPositions = win.Command.ToArray(),
                            Speeds = win.Speed.ToArray(),
                        }
                    );
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "[Leisai-Sampler] 轴 {AxisId} 采样推送失败：{Msg}",
                    axisId,
                    ex.Message
                );
            }
        }
    }

    /// <summary>通过反射获取 <see cref="LeisaiMotorDriver"/> 的私有 <c>_port</c> 字段，用于按端口分组。</summary>
    private static IRS485Port GetPortOf(LeisaiMotorDriver driver)
    {
        // 反射访问私有字段：兼容现有结构、不必修改驱动类公开 API
        System.Reflection.FieldInfo field =
            typeof(LeisaiMotorDriver).GetField(
                "_port",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException("LeisaiMotorDriver._port 字段未找到");
        return (IRS485Port)field.GetValue(driver)!;
    }

    /// <summary>
    /// 将本次采样快照的位置/速度/状态写回数据库，供设备管理页展示最新值。
    /// 由调用方通过 <see cref="_sampleCounters"/> 节流，约每 3 秒触发一次。
    /// </summary>
    private async Task UpdateAxisStatusAsync(
        Guid axisId,
        LeisaiStateSnapshot raw,
        CancellationToken ct
    )
    {
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            IMotorAxisRepository repo =
                scope.ServiceProvider.GetRequiredService<IMotorAxisRepository>();
            MotorAxis axis = await repo.GetAsync(axisId, cancellationToken: ct);

            // 根据状态字推导设备状态
            MotorDeviceStatus devStatus =
                (raw.StatusWord & 0x0001) != 0 ? MotorDeviceStatus.Faulted
                : (raw.StatusWord & 0x0004) != 0 ? MotorDeviceStatus.Moving
                : (raw.StatusWord & 0x0002) != 0 ? MotorDeviceStatus.Enabled
                : MotorDeviceStatus.Online;
            bool isHomed = (raw.StatusWord & 0x0020) != 0;

            axis.UpdateStatus(devStatus, raw.ActualPosition, raw.EffectiveSpeed, isHomed);
            await repo.UpdateAsync(axis, autoSave: true, cancellationToken: ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Leisai-Sampler] 轴 {AxisId} 状态写库失败", axisId);
        }
    }

    private async Task<Dictionary<int, Guid>> BuildSlaveToAxisIdMapAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IMotorAxisRepository repo =
            scope.ServiceProvider.GetRequiredService<IMotorAxisRepository>();
        List<MotorAxis> axes = await repo.GetListAsync(cancellationToken: ct);
        Dictionary<int, Guid> map = new();
        foreach (MotorAxis axis in axes)
        {
            if (axis.Brand == MotorBrand.LeisaiIclRs && axis.IsEnabled)
            {
                map[axis.SlaveId] = axis.Id;
            }
        }
        return map;
    }
}
