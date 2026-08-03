using AuroraStruct3D.Leisai.Catalog;
using AuroraStruct3D.Leisai.Dtos;
using AuroraStruct3D.Motors;
using AuroraStruct3D.RS485;
using AuroraStruct3D.RS485.Leisai;
using Microsoft.Extensions.Logging;
using Volo.Abp;

namespace AuroraStruct3D.Leisai;

/// <summary>
/// 雷赛 iCL-RS 电机操作台应用服务实现。
/// 通过 <see cref="IMotorControlService"/> 获取已注册的 <see cref="LeisaiMotorDriver"/>，
/// 仅服务 LeisaiIclRs 品牌轴。
/// </summary>
[Authorize]
public class LeisaiMotorAppService : AuroraStruct3DAppService, ILeisaiMotorAppService
{
    private readonly IMotorAxisRepository _motorAxisRepository;
    private readonly IMotorControlService _motorControlService;
    private readonly ILeisaiMotorNotifier _notifier;
    private readonly LeisaiSamplerStateStore _samplerStateStore;
    private readonly ILogger<LeisaiMotorAppService> _logger;

    // ===== 寄存器地址常量（DI/DO 功能配置） =====
    // Phase 1：每路功能码占 2 个 Modbus 寄存器（参数为 32 位），低字字段保存功能值，bit7=NC 标志。

    /// <summary>DI1 功能码（参数低字）起始地址</summary>
    private const ushort RegDiFunctionStart = 0x0145;

    /// <summary>DI 功能码总字数（7 路 × 2 = 14）</summary>
    private const ushort DiFunctionRegCount = 14;

    /// <summary>DO1 功能码起始地址</summary>
    private const ushort RegDoFunctionStart = 0x0157;

    /// <summary>DO 功能码总字数（3 路 × 2 = 6）</summary>
    private const ushort DoFunctionRegCount = 6;

    /// <summary>DO 输出位图寄存器</summary>
    private const ushort RegDoBitmap = 0x017B;

    /// <summary>故障/参数保存命令寄存器</summary>
    private const ushort RegFaultControl = 0x1801;

    /// <summary>命令字：清除当前故障</summary>
    private const ushort CmdClearCurrentFault = 0x1111;

    /// <summary>命令字：清除全部历史故障</summary>
    private const ushort CmdClearAllFaults = 0x1122;

    /// <summary>命令字：保存参数到 EEPROM</summary>
    private const ushort CmdSaveToEeprom = 0x2211;

    public LeisaiMotorAppService(
        IMotorAxisRepository motorAxisRepository,
        IMotorControlService motorControlService,
        ILeisaiMotorNotifier notifier,
        LeisaiSamplerStateStore samplerStateStore,
        ILogger<LeisaiMotorAppService> logger
    )
    {
        _motorAxisRepository = motorAxisRepository;
        _motorControlService = motorControlService;
        _notifier = notifier;
        _samplerStateStore = samplerStateStore;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<LeisaiStateSnapshotDto> GetStateAsync(Guid id)
    {
        (MotorAxis axis, LeisaiMotorDriver driver) = await ResolveAsync(id);
        LeisaiStateSnapshot raw = await driver.SampleStateAsync();
        return MapSnapshot(axis.Id, raw);
    }

    /// <inheritdoc/>
    public async Task<LeisaiIoConfigDto> GetIoConfigAsync(Guid id)
    {
        (_, LeisaiMotorDriver driver) = await ResolveAsync(id);

        // 一次性批量读取 DI/DO 功能码
        ushort[] diRegs = await driver.ReadRegistersAsync(RegDiFunctionStart, DiFunctionRegCount);
        ushort[] doRegs = await driver.ReadRegistersAsync(RegDoFunctionStart, DoFunctionRegCount);

        LeisaiIoConfigDto dto = new() { AxisId = id };
        // 每路占 2 个寄存器，取第一个（低字）作为功能码
        for (int i = 0; i < 7 && i * 2 < diRegs.Length; i++)
        {
            dto.DiFunctionCodes[i] = diRegs[i * 2];
        }
        for (int i = 0; i < 3 && i * 2 < doRegs.Length; i++)
        {
            dto.DoFunctionCodes[i] = doRegs[i * 2];
        }
        return dto;
    }

    /// <inheritdoc/>
    public async Task WriteOutputAsync(Guid id, LeisaiWriteOutputInputDto input)
    {
        (_, LeisaiMotorDriver driver) = await ResolveAsync(id);

        // 先读取当前 DO 位图，修改对应位后写回
        ushort current = await driver.ReadRegisterAsync(RegDoBitmap);
        ushort mask = (ushort)(1 << (input.DoIndex - 1));
        ushort next = input.Value ? (ushort)(current | mask) : (ushort)(current & ~mask);
        await driver.WriteRegisterAsync(RegDoBitmap, next);

        _logger.LogInformation(
            "[Leisai] 轴 {AxisId} DO{Idx}={Val} (0x{Prev:X4} → 0x{Next:X4})",
            id,
            input.DoIndex,
            input.Value,
            current,
            next
        );
    }

    /// <inheritdoc/>
    public async Task ClearFaultAsync(Guid id, LeisaiClearFaultInputDto input)
    {
        (_, LeisaiMotorDriver driver) = await ResolveAsync(id);
        ushort cmd = input.Mode?.ToLowerInvariant() switch
        {
            "all" => CmdClearAllFaults,
            "current" => CmdClearCurrentFault,
            _ => throw new UserFriendlyException("清除故障模式无效，可选：current / all"),
        };
        await driver.WriteRegisterAsync(RegFaultControl, cmd);
        _logger.LogInformation("[Leisai] 轴 {AxisId} 故障清除：mode={Mode}", id, input.Mode);
    }

    /// <inheritdoc/>
    public async Task SaveToEepromAsync(Guid id)
    {
        (_, LeisaiMotorDriver driver) = await ResolveAsync(id);
        await driver.WriteRegisterAsync(RegFaultControl, CmdSaveToEeprom);
        _logger.LogInformation("[Leisai] 轴 {AxisId} 参数已保存至 EEPROM", id);
    }

    /// <inheritdoc/>
    public async Task<LeisaiRawModbusResultDto> RawReadAsync(
        Guid id,
        LeisaiRawModbusReadInputDto input
    )
    {
        if (input.FunctionCode != 3)
        {
            throw new UserFriendlyException("当前仅支持 FC03 读保持寄存器");
        }
        (_, LeisaiMotorDriver driver) = await ResolveAsync(id);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        LeisaiRawModbusResultDto result = new();
        try
        {
            ushort[] values = await driver.ReadRegistersAsync(input.StartAddress, input.Quantity);
            result.Success = true;
            result.ParsedValues = values;
            result.RawRequest = $"FC03 addr=0x{input.StartAddress:X4} qty={input.Quantity}";
            result.RawResponse = string.Join(' ', values.Select(v => v.ToString("X4")));
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }
        finally
        {
            sw.Stop();
            result.ElapsedMs = (int)sw.ElapsedMilliseconds;
        }
        return result;
    }

    /// <inheritdoc/>
    public async Task<LeisaiRawModbusResultDto> RawWriteAsync(
        Guid id,
        LeisaiRawModbusWriteInputDto input
    )
    {
        (_, LeisaiMotorDriver driver) = await ResolveAsync(id);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        LeisaiRawModbusResultDto result = new();
        try
        {
            if (input.FunctionCode == 6)
            {
                await driver.WriteRegisterAsync(input.StartAddress, input.Values[0]);
                result.RawRequest =
                    $"FC06 addr=0x{input.StartAddress:X4} value=0x{input.Values[0]:X4}";
            }
            else if (input.FunctionCode == 16)
            {
                await driver.WriteRegistersAsync(input.StartAddress, input.Values);
                result.RawRequest =
                    $"FC16 addr=0x{input.StartAddress:X4} qty={input.Values.Length}";
            }
            else
            {
                throw new UserFriendlyException("写操作仅支持 FC06 / FC16");
            }
            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }
        finally
        {
            sw.Stop();
            result.ElapsedMs = (int)sw.ElapsedMilliseconds;
        }
        return result;
    }

    /// <inheritdoc/>
    public Task<LeisaiSamplingStateDto> GetSamplingStateAsync(Guid id) =>
        Task.FromResult(
            new LeisaiSamplingStateDto
            {
                IsPollingEnabled = _samplerStateStore.IsPollingEnabled(id),
            }
        );

    /// <inheritdoc/>
    public Task EnableSamplingAsync(Guid id)
    {
        _samplerStateStore.SetPollingEnabled(id, true);
        _logger.LogInformation("[Leisai] 轴 {AxisId} 实时采样已启用", id);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DisableSamplingAsync(Guid id)
    {
        _samplerStateStore.SetPollingEnabled(id, false);
        _logger.LogInformation("[Leisai] 轴 {AxisId} 实时采样已暂停", id);
        return Task.CompletedTask;
    }

    // ===== Phase 2：参数配置 =====

    /// <inheritdoc/>
    public async Task<List<LeisaiParameterMetadataDto>> GetParameterMetadataAsync(
        Guid id,
        int? group = null
    )
    {
        IEnumerable<LeisaiParameterMetadataDto> source = LeisaiParameterCatalog.All;
        if (group.HasValue)
        {
            source = source.Where(p => p.Group == group.Value);
        }

        if (!group.HasValue)
        {
            // 不带分组时返回全量目录，CurrentValue 均为 null
            return source.ToList();
        }

        // 带分组时：创建副本并批量读取当前值
        List<LeisaiParameterMetadataDto> result = source
            .Select(p => new LeisaiParameterMetadataDto
            {
                Pr = p.Pr,
                Group = p.Group,
                AddressLow = p.AddressLow,
                Name = p.Name,
                Description = p.Description,
                Unit = p.Unit,
                RangeMin = p.RangeMin,
                RangeMax = p.RangeMax,
                DefaultValue = p.DefaultValue,
                DataKind = p.DataKind,
            })
            .ToList();

        try
        {
            LeisaiBatchReadResultDto batchResult = await BatchReadParametersAsync(
                id,
                new LeisaiBatchReadInputDto
                {
                    AddressLows = result.Select(p => p.AddressLow).ToArray(),
                }
            );
            Dictionary<ushort, LeisaiParameterReadResultDto> valueMap =
                batchResult.Items.ToDictionary(v => v.AddressLow);
            foreach (LeisaiParameterMetadataDto dto in result)
            {
                if (
                    valueMap.TryGetValue(dto.AddressLow, out LeisaiParameterReadResultDto? val)
                    && val.IsSuccess
                )
                {
                    dto.CurrentValue = val.CurrentValue;
                }
            }
        }
        catch
        {
            // 读取失败时 CurrentValue 保持 null，不影响元数据正常返回
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<LeisaiBatchReadResultDto> BatchReadParametersAsync(
        Guid id,
        LeisaiBatchReadInputDto input
    )
    {
        (_, LeisaiMotorDriver driver) = await ResolveAsync(id);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // 保持输入顺序，结果按地址回填
        ushort[] addresses = input.AddressLows ?? [];
        Dictionary<ushort, LeisaiParameterReadResultDto> map = addresses
            .Distinct()
            .ToDictionary(
                a => a,
                a => new LeisaiParameterReadResultDto { AddressLow = a, IsSuccess = false }
            );

        // 优化：按连续奇数地址分块（步长 2），单次 FC03 一并读取
        // 每个参数占 2 个寄存器（高 + 低），起始 = 低地址 - 1（高地址）
        List<ushort> ordered = map.Keys.OrderBy(a => a).ToList();
        for (int i = 0; i < ordered.Count; )
        {
            int j = i + 1;
            while (j < ordered.Count && ordered[j] - ordered[j - 1] == 2 && j - i < 60)
            {
                j++;
            }
            int chunkLen = j - i;
            ushort firstLow = ordered[i];
            try
            {
                if (chunkLen == 1)
                {
                    ushort v = await driver.ReadRegisterAsync(firstLow);
                    LeisaiParameterReadResultDto dto = map[firstLow];
                    dto.CurrentValue = v;
                    dto.IsSuccess = true;
                }
                else
                {
                    // 起始高地址 = firstLow - 1；数量 = chunkLen * 2
                    ushort firstHigh = (ushort)(firstLow - 1);
                    ushort[] regs = await driver.ReadRegistersAsync(
                        firstHigh,
                        (ushort)(chunkLen * 2)
                    );
                    for (int k = 0; k < chunkLen; k++)
                    {
                        // 奇数偏移为低字
                        int idx = k * 2 + 1;
                        if (idx < regs.Length)
                        {
                            LeisaiParameterReadResultDto dto = map[ordered[i + k]];
                            dto.CurrentValue = regs[idx];
                            dto.IsSuccess = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                for (int k = 0; k < chunkLen; k++)
                {
                    LeisaiParameterReadResultDto dto = map[ordered[i + k]];
                    dto.IsSuccess = false;
                    dto.ErrorMessage = ex.Message;
                }
                _logger.LogWarning(
                    ex,
                    "[Leisai] 轴 {AxisId} 批量读 0x{Addr:X4} 长度 {Len} 失败",
                    id,
                    firstLow,
                    chunkLen
                );
            }
            i = j;
        }

        sw.Stop();
        // 按输入顺序输出
        LeisaiParameterReadResultDto[] items = addresses
            .Select(a =>
                map.TryGetValue(a, out LeisaiParameterReadResultDto? v)
                    ? v
                    : new LeisaiParameterReadResultDto
                    {
                        AddressLow = a,
                        IsSuccess = false,
                        ErrorMessage = "未读取",
                    }
            )
            .ToArray();
        return new LeisaiBatchReadResultDto
        {
            Items = items,
            ElapsedMs = (int)sw.ElapsedMilliseconds,
            SuccessCount = items.Count(x => x.IsSuccess),
            FailureCount = items.Count(x => !x.IsSuccess),
        };
    }

    /// <inheritdoc/>
    public async Task<LeisaiBatchResultDto> BatchWriteParametersAsync(
        Guid id,
        LeisaiBatchWriteInputDto input
    )
    {
        (_, LeisaiMotorDriver driver) = await ResolveAsync(id);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        List<LeisaiParameterValueDto> results = new(input.Items.Length);
        foreach (LeisaiBatchWriteItemDto item in input.Items)
        {
            LeisaiParameterValueDto dto = new()
            {
                AddressLow = item.AddressLow,
                Value = item.Value,
            };
            try
            {
                await driver.WriteRegisterAsync(item.AddressLow, item.Value);
                dto.IsSuccess = true;
            }
            catch (Exception ex)
            {
                dto.IsSuccess = false;
                dto.ErrorMessage = ex.Message;
                _logger.LogWarning(
                    ex,
                    "[Leisai] 轴 {AxisId} 写 0x{Addr:X4}=0x{Val:X4} 失败",
                    id,
                    item.AddressLow,
                    item.Value
                );
            }
            results.Add(dto);
        }
        sw.Stop();
        int ok = results.Count(x => x.IsSuccess);
        _logger.LogInformation(
            "[Leisai] 轴 {AxisId} 批量写参数 {Total} 项：成功 {Ok}",
            id,
            results.Count,
            ok
        );
        return new LeisaiBatchResultDto
        {
            Items = results.ToArray(),
            ElapsedMs = (int)sw.ElapsedMilliseconds,
            SuccessCount = ok,
            FailureCount = results.Count - ok,
        };
    }

    /// <inheritdoc/>
    public Task<LeisaiBatchReadResultDto> ReadGroupParametersAsync(Guid id, int group)
    {
        ushort[] addrs = LeisaiParameterCatalog
            .All.Where(p => p.Group == group)
            .Select(p => p.AddressLow)
            .ToArray();
        if (addrs.Length == 0)
        {
            throw new UserFriendlyException($"分组 Pr{group} 没有参数");
        }
        return BatchReadParametersAsync(id, new LeisaiBatchReadInputDto { AddressLows = addrs });
    }

    /// <inheritdoc/>
    public Task<LeisaiBatchResultDto> ResetParametersToDefaultAsync(
        Guid id,
        LeisaiBatchReadInputDto input
    )
    {
        // 查找目录中存在且有默认值的项
        Dictionary<ushort, LeisaiParameterMetadataDto> dict =
            LeisaiParameterCatalog.All.ToDictionary(p => p.AddressLow, p => p);
        List<LeisaiBatchWriteItemDto> items = [];
        foreach (ushort addr in input.AddressLows ?? [])
        {
            if (
                dict.TryGetValue(addr, out LeisaiParameterMetadataDto? meta)
                && meta.DefaultValue.HasValue
            )
            {
                items.Add(
                    new LeisaiBatchWriteItemDto
                    {
                        AddressLow = addr,
                        Value = unchecked((ushort)meta.DefaultValue.Value),
                    }
                );
            }
        }
        if (items.Count == 0)
        {
            throw new UserFriendlyException("所选参数均未提供出厂默认值，无法重置");
        }
        return BatchWriteParametersAsync(
            id,
            new LeisaiBatchWriteInputDto { Items = items.ToArray() }
        );
    }

    // ===== 标定专用：回原 / 限位配置 =====

    /// <inheritdoc/>
    public async Task SaveHomingConfigAsync(Guid id, LeisaiHomingConfigInputDto input)
    {
        (_, LeisaiMotorDriver driver) = await ResolveAsync(id);

        // 写回原参数寄存器
        await WriteHomingParamsAsync(driver, input);

        // Pr4.00（0x6000）bit2 → 1，启用回原
        await UpdateControlRegisterAsync(driver, current => (ushort)(current | 0x0004));

        // 保存至 EEPROM
        await driver.WriteRegisterAsync(RegFaultControl, CmdSaveToEeprom);

        _logger.LogInformation(
            "[Leisai] 轴 {AxisId} 回原配置已保存：direction={Dir} mode={Mode}",
            id,
            input.HomingDirection,
            input.HomingMode
        );
    }

    /// <inheritdoc/>
    public async Task DisableHomingAsync(Guid id)
    {
        (_, LeisaiMotorDriver driver) = await ResolveAsync(id);

        // Pr4.00（0x6000）bit2 → 0，禁用回原
        await UpdateControlRegisterAsync(driver, current => (ushort)(current & ~0x0004));

        // 保存至 EEPROM
        await driver.WriteRegisterAsync(RegFaultControl, CmdSaveToEeprom);

        _logger.LogInformation("[Leisai] 轴 {AxisId} 回原功能已禁用", id);
    }

    /// <inheritdoc/>
    public async Task<LeisaiHomingTestResultDto> TestHomingAsync(
        Guid id,
        LeisaiHomingConfigInputDto input
    )
    {
        (_, LeisaiMotorDriver driver) = await ResolveAsync(id);

        // 写回原参数寄存器（临时写入，不保存 EEPROM）
        await WriteHomingParamsAsync(driver, input);

        // 启用实时采样
        _samplerStateStore.SetPollingEnabled(id, true);

        // 使能驱动
        await driver.EnableAsync();

        // 触发回原：Pr8.02 / 0x6002 = 0x0020
        await driver.WriteRegisterAsync(0x6002, 0x0020);

        _logger.LogInformation("[Leisai] 轴 {AxisId} 回原测试已触发", id);

        // 轮询回原完成位（0x1003 bit5 = 0x0020），最多 30 秒
        const int TimeoutMs = 30_000;
        const int IntervalMs = 200;
        bool isCompleted = false;
        var deadline = DateTime.UtcNow.AddMilliseconds(TimeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(IntervalMs);
            ushort statusWord = await driver.ReadRegisterAsync(0x1003);
            if ((statusWord & 0x0020) != 0)
            {
                isCompleted = true;
                break;
            }
        }

        // 关使能
        await driver.DisableAsync();

        if (isCompleted)
        {
            _logger.LogInformation("[Leisai] 轴 {AxisId} 回原测试完成", id);
        }
        else
        {
            _logger.LogWarning("[Leisai] 轴 {AxisId} 回原测试超时（{Timeout}ms）", id, TimeoutMs);
        }

        return new LeisaiHomingTestResultDto { IsCompleted = isCompleted, TimedOut = !isCompleted };
    }

    /// <inheritdoc/>
    public async Task SaveLimitConfigAsync(Guid id, LeisaiLimitConfigInputDto input)
    {
        (_, LeisaiMotorDriver driver) = await ResolveAsync(id);

        // 写正向软限位（Int32 拆高低字：高字 → 0x6006，低字 → 0x6007）
        if (input.PositiveSoftLimit.HasValue)
        {
            await WriteInt32Async(driver, 0x6007, input.PositiveSoftLimit.Value);
        }

        // 写负向软限位（高字 → 0x6008，低字 → 0x6009）
        if (input.NegativeSoftLimit.HasValue)
        {
            await WriteInt32Async(driver, 0x6009, input.NegativeSoftLimit.Value);
        }

        // Pr4.00（0x6000）bit1 → 限位开关
        if (input.LimitEnabled)
        {
            await UpdateControlRegisterAsync(driver, current => (ushort)(current | 0x0002));
        }
        else
        {
            await UpdateControlRegisterAsync(driver, current => (ushort)(current & ~0x0002));
        }

        // 保存至 EEPROM
        await driver.WriteRegisterAsync(RegFaultControl, CmdSaveToEeprom);

        _logger.LogInformation(
            "[Leisai] 轴 {AxisId} 限位配置已保存：enabled={Enabled}",
            id,
            input.LimitEnabled
        );
    }

    // ===== 私有辅助 =====

    /// <summary>
    /// 将 Int32 值拆为高低两个 ushort 写入相邻寄存器对。
    /// addressLow 为低字地址，addressLow-1 为高字地址（雷赛 Int32 编址惯例）。
    /// </summary>
    private static async Task WriteInt32Async(
        LeisaiMotorDriver driver,
        ushort addressLow,
        int value
    )
    {
        (ushort high, ushort low) = SplitInt32(value);
        await driver.WriteRegisterAsync((ushort)(addressLow - 1), high);
        await driver.WriteRegisterAsync(addressLow, low);
    }

    internal static (ushort High, ushort Low) SplitInt32(int value)
    {
        uint unsigned = unchecked((uint)value);
        return ((ushort)(unsigned >> 16), (ushort)(unsigned & 0xFFFF));
    }

    /// <summary>
    /// 读取 Pr4.00（0x6000）控制寄存器，经 updater 修改后写回（仅在值发生变化时写）。
    /// </summary>
    private static async Task UpdateControlRegisterAsync(
        LeisaiMotorDriver driver,
        Func<ushort, ushort> updater
    )
    {
        ushort current = await driver.ReadRegisterAsync(0x6000);
        ushort next = updater(current);
        if (next != current)
        {
            await driver.WriteRegisterAsync(0x6000, next);
        }
    }

    /// <summary>
    /// 将 <see cref="LeisaiHomingConfigInputDto"/> 中的语义字段写入对应寄存器（不写 EEPROM）。
    /// </summary>
    private static async Task WriteHomingParamsAsync(
        LeisaiMotorDriver driver,
        LeisaiHomingConfigInputDto input
    )
    {
        // 拼装 0x600A：
        //   Bit0 = HomingDirection (0=Negative/反向, 1=Positive/正向)
        //   Bit1 = MoveAfterHome
        //   Bit2 = HomingMode (0=Limit, 1=Origin)
        //   Bit8 = WithZSignal
        ushort reg600A = 0;
        if (input.HomingDirection == LeisaiHomingDirection.Positive)
            reg600A |= 0x0001;
        if (input.MoveAfterHome)
            reg600A |= 0x0002;
        if (input.HomingMode == LeisaiHomingMode.Origin)
            reg600A |= 0x0004;
        if (input.WithZSignal)
            reg600A |= 0x0100;
        await driver.WriteRegisterAsync(0x600A, reg600A);

        // 回零停止位（Int32）→ 0x600D（高字）/ 0x600E（低字）
        if (input.MoveAfterHome && input.HomeStopPosition.HasValue)
        {
            await WriteInt32Async(driver, 0x600E, input.HomeStopPosition.Value);
        }

        // 回原速度：同时写高速（0x600F）和低速（0x6010）
        if (input.HomeSpeedRpm.HasValue)
        {
            ushort speed = (ushort)(input.HomeSpeedRpm.Value & 0xFFFF);
            await driver.WriteRegisterAsync(0x600F, speed);
            await driver.WriteRegisterAsync(0x6010, speed);
        }

        // 回原加速度：同时写加速（0x6011）和减速（0x6012）
        if (input.HomeAccelerationRpm.HasValue)
        {
            ushort accel = (ushort)(input.HomeAccelerationRpm.Value & 0xFFFF);
            await driver.WriteRegisterAsync(0x6011, accel);
            await driver.WriteRegisterAsync(0x6012, accel);
        }
    }

    private async Task<(MotorAxis Axis, LeisaiMotorDriver Driver)> ResolveAsync(Guid id)
    {
        MotorAxis axis = await _motorAxisRepository.GetAsync(id);
        if (axis.Brand != MotorBrand.LeisaiIclRs)
        {
            throw new UserFriendlyException($"电机 [{axis.Name}] 不是雷赛品牌，操作台不可用");
        }

        LeisaiMotorDriver? driver = _motorControlService.GetLeisaiMotorDriver(axis.SlaveId);
        if (driver is null)
        {
            throw new UserFriendlyException(
                $"未找到从机地址 {axis.SlaveId} 对应的雷赛驱动，请确认串口已打开"
            );
        }
        return (axis, driver);
    }

    /// <summary>
    /// 将驱动层快照映射为对外 DTO，并解码 0x1003 状态字各 bit。
    /// </summary>
    internal static LeisaiStateSnapshotDto MapSnapshot(Guid axisId, LeisaiStateSnapshot raw)
    {
        ushort sw = raw.StatusWord;
        return new LeisaiStateSnapshotDto
        {
            AxisId = axisId,
            SlaveId = raw.SlaveId,
            TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            ElapsedMs = raw.ElapsedMs,
            IsSuccess = raw.IsSuccess,
            FailureReason = raw.FailureReason,
            StatusWord = sw,
            IsFault = (sw & 0x0001) != 0,
            IsEnabled = (sw & 0x0002) != 0,
            IsRunning = (sw & 0x0004) != 0,
            IsCommandDone = (sw & 0x0008) != 0,
            IsPathDone = (sw & 0x0010) != 0,
            IsHomeDone = (sw & 0x0020) != 0,
            TriggerWord = raw.TriggerWord,
            TriggerMode = raw.TriggerMode,
            CommandPosition = raw.CommandPosition,
            ActualPosition = raw.ActualPosition,
            EffectiveSpeed = raw.EffectiveSpeed,
            SpeedSource = raw.SpeedSource,
            BusVoltageVolt = raw.BusVoltageVolt,
            InputIoBitmap = raw.InputIoBitmap,
            OutputIoBitmap = raw.OutputIoBitmap,
            CurrentFaultCode = raw.CurrentFaultCode,
            PrWarningCode = raw.PrWarningCode,
        };
    }
}
