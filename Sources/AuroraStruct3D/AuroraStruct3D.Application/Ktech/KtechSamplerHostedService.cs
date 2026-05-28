using AuroraStruct3D.Ktech.Dtos;
using AuroraStruct3D.Motors;
using AuroraStruct3D.RS485;
using AuroraStruct3D.RS485.Ktech;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AuroraStruct3D.Ktech;

/// <summary>
/// 瓴控 KTECH 实时数据采集后台服务。
/// 周期遍历所有已注册的 KTECH 驱动，读取 State1+State2+多圈角度，
/// 通过 <see cref="IKtechMotorNotifier"/> 推送至 SignalR。
/// </summary>
public class KtechSamplerHostedService : BackgroundService
{
    /// <summary>采样周期（毫秒）。默认 250ms（4Hz），可后续提为配置项。</summary>
    private const int SamplePeriodMs = 250;

    /// <summary>启动延迟（毫秒），等待 MotorControlService 完成初始化。</summary>
    private const int StartupDelayMs = 5000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMotorControlService _motorControlService;
    private readonly IKtechMotorNotifier _notifier;
    private readonly KtechSamplerStateStore _samplerStateStore;
    private readonly ILogger<KtechSamplerHostedService> _logger;

    public KtechSamplerHostedService(
        IServiceScopeFactory scopeFactory,
        IMotorControlService motorControlService,
        IKtechMotorNotifier notifier,
        KtechSamplerStateStore samplerStateStore,
        ILogger<KtechSamplerHostedService> logger
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

        _logger.LogInformation("[KTECH-Sampler] 启动，采样周期 {Period} ms", SamplePeriodMs);

        // 缓存 SlaveId → AxisId 映射，避免每周期查库
        Dictionary<int, Guid> slaveToAxisId = new();
        DateTime lastMapRefreshUtc = DateTime.MinValue;
        TimeSpan mapRefreshInterval = TimeSpan.FromSeconds(10);

        while (!stoppingToken.IsCancellationRequested)
        {
            DateTime cycleStart = DateTime.UtcNow;

            try
            {
                // 周期刷新 SlaveId→AxisId 映射
                if (DateTime.UtcNow - lastMapRefreshUtc > mapRefreshInterval)
                {
                    slaveToAxisId = await BuildSlaveToAxisIdMapAsync(stoppingToken);
                    lastMapRefreshUtc = DateTime.UtcNow;
                }

                IReadOnlyList<KtechMotorDriver> drivers = _motorControlService.KtechDrivers;
                foreach (KtechMotorDriver driver in drivers)
                {
                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

                    if (!slaveToAxisId.TryGetValue(driver.SlaveId, out Guid axisId))
                    {
                        // 该 KTECH 驱动当前未注册到数据库（可能扫描发现但尚未保存），跳过
                        continue;
                    }

                    // 实时采样已被前端暂停，跳过本轴（不推送 SignalR）
                    if (!_samplerStateStore.IsPollingEnabled(axisId))
                    {
                        continue;
                    }

                    KtechStateSnapshotDto snapshot = await SampleOnceAsync(
                        axisId,
                        driver,
                        stoppingToken
                    );
                    await _notifier.NotifyStateAsync(snapshot);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[KTECH-Sampler] 采样周期发生异常");
            }

            // 控制节拍：每次采样 + 推送后等待到下个周期
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

        _logger.LogInformation("[KTECH-Sampler] 已停止");
    }

    /// <summary>单次采样：读取 State1+State2+多圈角度。</summary>
    private async Task<KtechStateSnapshotDto> SampleOnceAsync(
        Guid axisId,
        KtechMotorDriver driver,
        CancellationToken ct
    )
    {
        DateTime start = DateTime.UtcNow;
        KtechStateSnapshotDto dto = new()
        {
            AxisId = axisId,
            SlaveId = driver.SlaveId,
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ErrorFlags = new KtechErrorFlagsDto(),
        };

        try
        {
            KtechState1 s1 = await driver.ReadState1Async(ct);
            KtechState2 s2 = await driver.ReadState2Async(ct);
            long angle = await driver.ReadMultiAngleAsync(ct);
            uint singleAngle = await driver.ReadSingleAngleAsync(ct);

            dto.MotorTemperature = s1.MotorTemperature;
            dto.BusVoltage = s1.BusVoltage;
            dto.BusCurrent = s1.BusCurrent;
            dto.ErrorFlags = KtechMotorAppService.MapErrorFlags(s1.ErrorFlags);
            dto.TorqueOrPower = s2.TorqueOrPower;
            dto.Speed = s2.Speed;
            dto.EncoderValue = s2.EncoderValue;
            dto.MultiTurnAngle = angle / 100.0;
            // 单圈角度归一化到 0..35999（容错驱动返回值越界）
            dto.SingleTurnAngleCentideg = singleAngle % 36000u;
            dto.IsSuccess = true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            dto.IsSuccess = false;
            dto.FailureReason = ex.Message;
        }
        finally
        {
            dto.ElapsedMs = (int)(DateTime.UtcNow - start).TotalMilliseconds;
        }

        return dto;
    }

    /// <summary>从仓储构建 SlaveId → AxisId 映射，仅保留启用的 KTECH 轴。</summary>
    private async Task<Dictionary<int, Guid>> BuildSlaveToAxisIdMapAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IMotorAxisRepository repo =
            scope.ServiceProvider.GetRequiredService<IMotorAxisRepository>();

        List<MotorAxis> axes = await repo.GetListAsync(cancellationToken: ct);
        Dictionary<int, Guid> map = new();
        foreach (MotorAxis axis in axes)
        {
            if (axis.Brand == MotorBrand.KtechKtech && axis.IsEnabled)
            {
                // 同 SlaveId 冲突时取最后一个，与 MotorControlService 行为一致
                map[axis.SlaveId] = axis.Id;
            }
        }
        return map;
    }
}
