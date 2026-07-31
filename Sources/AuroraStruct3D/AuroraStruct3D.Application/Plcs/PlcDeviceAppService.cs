using System.Diagnostics;
using System.Text.Json;
using AuroraStruct3D.DeviceState;
using AuroraStruct3D.Sessions;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Security.Encryption;

namespace AuroraStruct3D.Plcs;

[Authorize]
public class PlcDeviceAppService
    : AuroraStruct3DAppService,
        IPlcDeviceAppService,
        IPlcTagAccessor
{
    private readonly IRepository<PlcDevice, Guid> _devices;
    private readonly IRepository<PlcTag, Guid> _tags;
    private readonly IRepository<PlcOperationLog, Guid> _logs;
    private readonly IRepository<PlcTrustedCertificate, Guid> _trustedCertificates;
    private readonly IPlcDriverRegistry _drivers;
    private readonly IPlcConnectionManager _connections;
    private readonly IStringEncryptionService _encryption;
    private readonly IDeviceStateManager _deviceState;
    private readonly IDeviceOperationSessionManager _sessions;
    private readonly ICurrentClientSession _currentClientSession;
    private readonly IPlcRealtimeNotifier _notifier;
    private readonly IPlcSubscriptionTracker _subscriptionTracker;

    public PlcDeviceAppService(
        IRepository<PlcDevice, Guid> devices,
        IRepository<PlcTag, Guid> tags,
        IRepository<PlcOperationLog, Guid> logs,
        IRepository<PlcTrustedCertificate, Guid> trustedCertificates,
        IPlcDriverRegistry drivers,
        IPlcConnectionManager connections,
        IStringEncryptionService encryption,
        IDeviceStateManager deviceState,
        IDeviceOperationSessionManager sessions,
        ICurrentClientSession currentClientSession,
        IPlcRealtimeNotifier notifier
        , IPlcSubscriptionTracker subscriptionTracker
    )
    {
        _devices = devices;
        _tags = tags;
        _logs = logs;
        _trustedCertificates = trustedCertificates;
        _drivers = drivers;
        _connections = connections;
        _encryption = encryption;
        _deviceState = deviceState;
        _sessions = sessions;
        _currentClientSession = currentClientSession;
        _notifier = notifier;
        _subscriptionTracker = subscriptionTracker;
    }

    public Task<ListResultDto<PlcDriverDescriptor>> GetDriversAsync() =>
        Task.FromResult(new ListResultDto<PlcDriverDescriptor>(_drivers.Drivers.ToList()));

    public async Task<ListResultDto<PlcDeviceDto>> GetListAsync()
    {
        List<PlcDevice> devices = await _devices.GetListAsync();
        return new ListResultDto<PlcDeviceDto>(
            devices.OrderBy(x => x.Name).Select(ToDto).ToList()
        );
    }

    public async Task<PlcDeviceDto> GetAsync(Guid id) => ToDto(await _devices.GetAsync(id));

    public async Task<PlcDeviceDto> CreateAsync(SavePlcDeviceDto input)
    {
        await EnsureWriteControlAsync(Guid.Empty, acquireSession: false);
        EnsureInstalledAndMatching(input.DriverId, input.Protocol);
        if (
            await AsyncExecuter.AnyAsync(
                (await _devices.GetQueryableAsync()).Where(x => x.Name == input.Name)
            )
        )
            throw new UserFriendlyException($"PLC 名称已存在：{input.Name}");

        var device = new PlcDevice(
            GuidGenerator.Create(),
            input.Name,
            input.Protocol,
            input.DriverId,
            input.EndpointUrl
        );
        Apply(device, input);
        await _devices.InsertAsync(device, autoSave: true);
        await LogAsync(device.Id, null, PlcOperationType.ConfigurationChanged, true, "创建 PLC");
        return ToDto(device);
    }

    public async Task<PlcDeviceDto> UpdateAsync(Guid id, SavePlcDeviceDto input)
    {
        await EnsureWriteControlAsync(id);
        EnsureInstalledAndMatching(input.DriverId, input.Protocol);
        PlcDevice device = await _devices.GetAsync(id);
        device.SetConfiguration(input.Name, input.Protocol, input.DriverId, input.EndpointUrl);
        Apply(device, input);
        await _connections.DisconnectAsync(id);
        await _devices.UpdateAsync(device, autoSave: true);
        await LogAsync(id, null, PlcOperationType.ConfigurationChanged, true, "更新 PLC 配置");
        return ToDto(device);
    }

    public async Task DeleteAsync(Guid id)
    {
        await EnsureWriteControlAsync(id);
        if (
            await AsyncExecuter.AnyAsync(
                (await _tags.GetQueryableAsync()).Where(x => x.PlcDeviceId == id)
            )
        )
            throw new UserFriendlyException("PLC 仍有关联点位，请先删除点位");
        await _connections.DisconnectAsync(id);
        await _devices.DeleteAsync(id);
    }

    public async Task<PlcConnectionTestResultDto> TestConnectionAsync(Guid id)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            PlcDevice device = await _devices.GetAsync(id);
            IPlcConnection connection = await ConnectCoreAsync(device);
            bool ok = await connection.HealthCheckAsync();
            await LogAsync(id, null, PlcOperationType.TestConnection, ok, "连接测试");
            return new PlcConnectionTestResultDto { Success = ok, DurationMs = sw.ElapsedMilliseconds };
        }
        catch (Exception ex)
        {
            await LogAsync(id, null, PlcOperationType.TestConnection, false, null, ex.Message);
            return new PlcConnectionTestResultDto
            {
                Success = false,
                Error = ex.Message,
                DurationMs = sw.ElapsedMilliseconds,
            };
        }
    }

    public async Task ConnectAsync(Guid id)
    {
        PlcDevice device = await _devices.GetAsync(id);
        await ConnectCoreAsync(device);
    }

    public async Task DisconnectAsync(Guid id)
    {
        await _connections.DisconnectAsync(id);
        PlcDevice device = await _devices.GetAsync(id);
        device.SetConnectionState(PlcConnectionStatus.Disconnected);
        await _devices.UpdateAsync(device);
        await _notifier.ConnectionStateChangedAsync(
            id,
            PlcConnectionStatus.Disconnected,
            null
        );
        await LogAsync(id, null, PlcOperationType.Disconnect, true, "主动断开");
    }

    public async Task ConfirmServerCertificateAsync(
        Guid id,
        ConfirmPlcCertificateInput input
    )
    {
        await EnsureWriteControlAsync(id);
        _ = await _devices.GetAsync(id);
        List<PlcTrustedCertificate> old = await AsyncExecuter.ToListAsync(
            (await _trustedCertificates.GetQueryableAsync()).Where(x => x.PlcDeviceId == id)
        );
        foreach (PlcTrustedCertificate certificate in old)
            await _trustedCertificates.DeleteAsync(certificate);
        await _trustedCertificates.InsertAsync(
            new PlcTrustedCertificate(
                GuidGenerator.Create(),
                id,
                input.Thumbprint.Trim(),
                "用户确认的 OPC UA 服务端证书",
                DateTime.UtcNow,
                DateTime.UtcNow.AddYears(100)
            ),
            autoSave: true
        );
        await LogAsync(
            id,
            null,
            PlcOperationType.TrustCertificate,
            true,
            $"人工确认服务端证书 {input.Thumbprint.Trim()}"
        );
    }

    public async Task<PlcBrowseResult> BrowseAsync(Guid id, PlcBrowseInput input)
    {
        PlcDevice device = await _devices.GetAsync(id);
        IPlcDriver driver = Resolve(device);
        if (driver is not IPlcBrowsableDriver browsable)
            throw new UserFriendlyException($"驱动 {device.DriverId} 不支持节点浏览");
        IPlcConnection connection = await ConnectCoreAsync(device);
        return await browsable.BrowseAsync(
            connection,
            new PlcBrowseRequest(
                input.ParentAddress,
                input.ContinuationToken,
                input.MaxResults
            )
        );
    }

    public async Task<ListResultDto<PlcTagDto>> GetTagsAsync(Guid id)
    {
        List<PlcTag> tags = await AsyncExecuter.ToListAsync(
            (await _tags.GetQueryableAsync())
                .Where(x => x.PlcDeviceId == id)
                .OrderBy(x => x.Code)
        );
        return new ListResultDto<PlcTagDto>(tags.Select(ToDto).ToList());
    }

    public async Task<PlcTagDto> CreateTagAsync(Guid id, SavePlcTagDto input)
    {
        await EnsureWriteControlAsync(id);
        PlcDevice device = await _devices.GetAsync(id);
        await Resolve(device).ValidateAddressAsync(input.Address, input.DataType);
        if (
            await AsyncExecuter.AnyAsync(
                (await _tags.GetQueryableAsync()).Where(x => x.Code == input.Code)
            )
        )
            throw new UserFriendlyException($"PLC 点位代码已存在：{input.Code}");
        var tag = new PlcTag(
            GuidGenerator.Create(),
            id,
            input.Code,
            input.Name,
            input.Address,
            input.DataType,
            input.Access
        );
        Apply(tag, input);
        await _tags.InsertAsync(tag, autoSave: true);
        return ToDto(tag);
    }

    public async Task<PlcTagDto> UpdateTagAsync(Guid id, Guid tagId, SavePlcTagDto input)
    {
        await EnsureWriteControlAsync(id);
        PlcTag tag = await GetOwnedTagAsync(id, tagId);
        PlcDevice device = await _devices.GetAsync(id);
        await Resolve(device).ValidateAddressAsync(input.Address, input.DataType);
        tag.Update(input.Code, input.Name, input.Address, input.DataType, input.Access);
        Apply(tag, input);
        await _tags.UpdateAsync(tag, autoSave: true);
        return ToDto(tag);
    }

    public async Task DeleteTagAsync(Guid id, Guid tagId)
    {
        await EnsureWriteControlAsync(id);
        _ = await GetOwnedTagAsync(id, tagId);
        await _tags.DeleteAsync(tagId);
    }

    public async Task<ListResultDto<PlcTagValueDto>> ReadAsync(Guid id, PlcReadInput input)
    {
        List<PlcTag> tags = await GetTagsAsync(id, input.TagIds);
        return new ListResultDto<PlcTagValueDto>((await ReadCoreAsync(id, tags)).ToList());
    }

    public async Task<ListResultDto<PlcWriteResultDto>> WriteAsync(
        Guid id,
        PlcWriteInput input
    )
    {
        await EnsureWriteControlAsync(id);
        Dictionary<Guid, PlcTag> tags = (await GetTagsAsync(id, input.Items.Select(x => x.TagId)))
            .ToDictionary(x => x.Id);
        var values = input.Items.ToDictionary(x => tags[x.TagId].Code, x => x.Value);
        return new ListResultDto<PlcWriteResultDto>(
            (await WriteCoreAsync(id, tags.Values.ToList(), values)).ToList()
        );
    }

    public async Task<PlcSubscriptionResultDto> SubscribeAsync(
        Guid id,
        PlcSubscribeInput input
    )
    {
        PlcDevice device = await _devices.GetAsync(id);
        IPlcDriver driver = Resolve(device);
        if (driver is not IPlcSubscriptionDriver subscriptions)
            throw new UserFriendlyException($"驱动 {device.DriverId} 不支持订阅");
        List<PlcTag> tags = await GetTagsAsync(id, input.TagIds);
        IPlcConnection connection = await ConnectCoreAsync(device);
        string subscriptionId = await subscriptions.SubscribeAsync(
            connection,
            tags.Select(x =>
                    new PlcSubscriptionItem(
                        x.Id.ToString(),
                        x.Address,
                        x.DataType,
                        x.SamplingIntervalMs,
                        x.Deadband
                    )
                )
                .ToList(),
            async value =>
            {
                _connections.Touch(id);
                PlcTag? tag = tags.FirstOrDefault(x => x.Id.ToString() == value.Key);
                if (tag != null)
                    await _notifier.ValueChangedAsync(input.ConnectionId, MapValue(tag, value));
            }
        );
        await LogAsync(id, null, PlcOperationType.Subscribe, true, $"订阅 {tags.Count} 个点位");
        _subscriptionTracker.Add(input.ConnectionId, id, subscriptionId);
        return new PlcSubscriptionResultDto { SubscriptionId = subscriptionId };
    }

    public async Task UnsubscribeAsync(Guid id, string subscriptionId)
    {
        PlcDevice device = await _devices.GetAsync(id);
        IPlcDriver driver = Resolve(device);
        if (driver is IPlcSubscriptionDriver subscriptions)
        {
            IPlcConnection connection = await ConnectCoreAsync(device);
            await subscriptions.UnsubscribeAsync(connection, subscriptionId);
        }
        _subscriptionTracker.Remove(subscriptionId);
    }

    public async Task<IReadOnlyList<PlcTagValueDto>> ReadByCodesAsync(
        Guid plcDeviceId,
        IReadOnlyList<string> tagCodes,
        CancellationToken cancellationToken = default
    )
    {
        List<PlcTag> tags = await AsyncExecuter.ToListAsync(
            (await _tags.GetQueryableAsync()).Where(x =>
                x.PlcDeviceId == plcDeviceId && tagCodes.Contains(x.Code)
            ),
            cancellationToken
        );
        return await ReadCoreAsync(plcDeviceId, tags, cancellationToken);
    }

    public async Task<IReadOnlyList<PlcWriteResultDto>> WriteByCodesAsync(
        Guid plcDeviceId,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default
    )
    {
        await EnsureWriteControlAsync(plcDeviceId);
        List<PlcTag> tags = await AsyncExecuter.ToListAsync(
            (await _tags.GetQueryableAsync()).Where(x =>
                x.PlcDeviceId == plcDeviceId && values.Keys.Contains(x.Code)
            ),
            cancellationToken
        );
        return await WriteCoreAsync(plcDeviceId, tags, values, cancellationToken);
    }

    private async Task<IReadOnlyList<PlcTagValueDto>> ReadCoreAsync(
        Guid id,
        List<PlcTag> tags,
        CancellationToken cancellationToken = default
    )
    {
        PlcDevice device = await _devices.GetAsync(id);
        IPlcDriver driver = Resolve(device);
        IPlcConnection connection = await ConnectCoreAsync(device, cancellationToken);
        IReadOnlyList<PlcValue> values = await driver.ReadAsync(
            connection,
            tags.Select(x => new PlcReadRequest(x.Id.ToString(), x.Address, x.DataType)).ToList(),
            cancellationToken
        );
        return values.Select(x => MapValue(tags.Single(t => t.Id.ToString() == x.Key), x)).ToList();
    }

    private async Task<IReadOnlyList<PlcWriteResultDto>> WriteCoreAsync(
        Guid id,
        List<PlcTag> tags,
        IReadOnlyDictionary<string, object?> values,
        CancellationToken cancellationToken = default
    )
    {
        PlcDevice device = await _devices.GetAsync(id);
        IPlcDriver driver = Resolve(device);
        IPlcConnection connection = await ConnectCoreAsync(device, cancellationToken);
        var immediateErrors = new List<PlcWriteResultDto>();
        var requests = new List<PlcWriteRequest>();
        foreach (PlcTag tag in tags)
        {
            try
            {
                if ((tag.Access & PlcTagAccess.Write) == 0)
                    throw new UserFriendlyException("点位只读");
                object? raw = ToRaw(tag, values[tag.Code]);
                requests.Add(new PlcWriteRequest(tag.Id.ToString(), tag.Address, tag.DataType, raw));
            }
            catch (Exception ex)
            {
                immediateErrors.Add(
                    new PlcWriteResultDto { TagId = tag.Id, Success = false, Error = ex.Message }
                );
            }
        }
        IReadOnlyList<PlcWriteResult> results = await driver.WriteAsync(
            connection,
            requests,
            cancellationToken
        );
        var mapped = results
            .Select(x => new PlcWriteResultDto
            {
                TagId = Guid.Parse(x.Key),
                Success = x.Success,
                Error = x.Error,
            })
            .Concat(immediateErrors)
            .ToList();
        foreach (PlcWriteResultDto result in mapped)
            await LogAsync(
                id,
                result.TagId,
                PlcOperationType.Write,
                result.Success,
                result.Success ? "点位写入" : null,
                result.Error
            );
        return mapped;
    }

    private async Task<IPlcConnection> ConnectCoreAsync(
        PlcDevice device,
        CancellationToken cancellationToken = default
    )
    {
        if (!device.IsEnabled)
            throw new UserFriendlyException($"PLC [{device.Name}] 已禁用");
        try
        {
            IPlcConnection connection = await _connections.GetOrConnectAsync(
                ToOptions(device),
                device.DriverId,
                cancellationToken
            );
            await EnsureServerCertificateAsync(device, connection);
            if (device.ConnectionStatus != PlcConnectionStatus.Connected)
            {
                device.SetConnectionState(PlcConnectionStatus.Connected);
                await _devices.UpdateAsync(device);
                await LogAsync(device.Id, null, PlcOperationType.Connect, true, "建立连接");
            }
            await _notifier.ConnectionStateChangedAsync(
                device.Id,
                PlcConnectionStatus.Connected,
                null
            );
            return connection;
        }
        catch (Exception ex)
        {
            device.SetConnectionState(PlcConnectionStatus.Faulted, ex.Message);
            await _devices.UpdateAsync(device);
            await _notifier.ConnectionStateChangedAsync(
                device.Id,
                PlcConnectionStatus.Faulted,
                ex.Message
            );
            throw new UserFriendlyException($"PLC 连接失败：{ex.Message}");
        }
    }

    private async Task EnsureServerCertificateAsync(
        PlcDevice device,
        IPlcConnection connection
    )
    {
        string? thumbprint = connection.ServerCertificateThumbprint;
        if (string.IsNullOrWhiteSpace(thumbprint))
            return;
        List<PlcTrustedCertificate> known = await AsyncExecuter.ToListAsync(
            (await _trustedCertificates.GetQueryableAsync()).Where(x =>
                x.PlcDeviceId == device.Id
            )
        );
        if (known.Count == 0)
        {
            await _trustedCertificates.InsertAsync(
                new PlcTrustedCertificate(
                    GuidGenerator.Create(),
                    device.Id,
                    thumbprint,
                    connection.ServerCertificateSubject ?? device.EndpointUrl,
                    connection.ServerCertificateNotBefore ?? DateTime.UtcNow,
                    connection.ServerCertificateNotAfter ?? DateTime.UtcNow
                ),
                autoSave: true
            );
            await LogAsync(
                device.Id,
                null,
                PlcOperationType.TrustCertificate,
                true,
                $"首次信任证书 {thumbprint}"
            );
            return;
        }
        if (!known.Any(x => string.Equals(x.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase)))
        {
            await _connections.DisconnectAsync(device.Id);
            throw new UserFriendlyException(
                $"OPC UA 服务端证书已变更，拒绝自动覆盖。新指纹：{thumbprint}"
            );
        }
    }

    private IPlcDriver Resolve(PlcDevice device)
    {
        if (!_drivers.TryGet(device.DriverId, out IPlcDriver? driver))
            throw new UserFriendlyException($"PLC 驱动未安装：{device.DriverId}");
        return driver!;
    }

    private void EnsureInstalledAndMatching(string driverId, PlcProtocolType protocol)
    {
        IPlcDriver driver = _drivers.TryGet(driverId, out IPlcDriver? found)
            ? found!
            : throw new UserFriendlyException($"PLC 驱动未安装：{driverId}");
        if (driver.Descriptor.Protocol != protocol)
            throw new UserFriendlyException("PLC 协议与驱动不匹配");
    }

    private async Task EnsureWriteControlAsync(Guid id, bool acquireSession = true)
    {
        if (_deviceState.RunMode is not (DeviceRunMode.Manual or DeviceRunMode.Maintenance))
            throw new UserFriendlyException("PLC 写入和配置修改仅允许在手动或检修模式执行");
        string? sessionId = _currentClientSession.SessionId;
        if (!acquireSession || id == Guid.Empty || string.IsNullOrWhiteSpace(sessionId))
            return;
        _sessions.TryAcquire(
            id,
            DeviceType.PLC,
            sessionId,
            CurrentUser.Id?.ToString(),
            CurrentUser.Name ?? CurrentUser.UserName ?? sessionId,
            false
        );
        await Task.CompletedTask;
    }

    private void Apply(PlcDevice device, SavePlcDeviceDto input)
    {
        device.SetEnabled(input.IsEnabled);
        device.ConfigureSecurity(
            input.AuthenticationType,
            input.UserName,
            string.IsNullOrEmpty(input.Password) ? null : _encryption.Encrypt(input.Password),
            input.ClientCertificatePath,
            string.IsNullOrEmpty(input.ClientCertificatePassword)
                ? null
                : _encryption.Encrypt(input.ClientCertificatePassword),
            input.SecurityPolicy,
            input.MessageSecurityMode,
            input.AutoTrustServerCertificate
        );
        device.ConfigureRuntime(
            input.ConnectTimeoutMs,
            input.OperationTimeoutMs,
            input.SessionTimeoutMs,
            input.KeepAliveMs,
            input.ReconnectInitialMs,
            input.ReconnectMaxMs,
            input.IdleTimeoutMs,
            input.ExtensionJson
        );
    }

    private static void Apply(PlcTag tag, SavePlcTagDto input)
    {
        tag.SetEnabled(input.IsEnabled);
        tag.ConfigureEngineering(
            input.SamplingIntervalMs,
            input.Deadband,
            input.Scale,
            input.Offset,
            input.Unit,
            input.DisplayFormat,
            input.Minimum,
            input.Maximum
        );
    }

    private PlcConnectionOptions ToOptions(PlcDevice x) =>
        new(
            x.Id,
            x.EndpointUrl,
            x.AuthenticationType,
            x.UserName,
            _encryption.Decrypt(x.EncryptedPassword),
            x.ClientCertificatePath,
            _encryption.Decrypt(x.EncryptedClientCertificatePassword),
            x.SecurityPolicy,
            x.MessageSecurityMode,
            x.AutoTrustServerCertificate,
            x.ConnectTimeoutMs,
            x.OperationTimeoutMs,
            x.SessionTimeoutMs,
            x.KeepAliveMs,
            x.ReconnectInitialMs,
            x.ReconnectMaxMs,
            x.IdleTimeoutMs,
            x.ExtensionJson
        );

    private PlcDeviceDto ToDto(PlcDevice x)
    {
        _drivers.TryGet(x.DriverId, out IPlcDriver? driver);
        return new PlcDeviceDto
        {
            Id = x.Id,
            Name = x.Name,
            Protocol = x.Protocol,
            DriverId = x.DriverId,
            DriverInstalled = driver != null,
            Capabilities = driver?.Descriptor.Capabilities ?? PlcCapability.None,
            IsEnabled = x.IsEnabled,
            EndpointUrl = x.EndpointUrl,
            AuthenticationType = x.AuthenticationType,
            UserName = x.UserName,
            HasPassword = !string.IsNullOrEmpty(x.EncryptedPassword),
            ClientCertificatePath = x.ClientCertificatePath,
            HasPrivateKey = !string.IsNullOrEmpty(x.EncryptedClientCertificatePassword),
            SecurityPolicy = x.SecurityPolicy,
            MessageSecurityMode = x.MessageSecurityMode,
            AutoTrustServerCertificate = x.AutoTrustServerCertificate,
            ConnectTimeoutMs = x.ConnectTimeoutMs,
            OperationTimeoutMs = x.OperationTimeoutMs,
            SessionTimeoutMs = x.SessionTimeoutMs,
            KeepAliveMs = x.KeepAliveMs,
            ReconnectInitialMs = x.ReconnectInitialMs,
            ReconnectMaxMs = x.ReconnectMaxMs,
            IdleTimeoutMs = x.IdleTimeoutMs,
            ExtensionJson = x.ExtensionJson,
            ConnectionStatus = _connections.GetStatus(x.Id),
            LastConnectedAt = x.LastConnectedAt,
            LastFailedAt = x.LastFailedAt,
            LastError = x.LastError,
            CreationTime = x.CreationTime,
            LastModificationTime = x.LastModificationTime,
        };
    }

    private static PlcTagDto ToDto(PlcTag x) =>
        new()
        {
            Id = x.Id,
            PlcDeviceId = x.PlcDeviceId,
            Code = x.Code,
            Name = x.Name,
            Address = x.Address,
            DataType = x.DataType,
            Access = x.Access,
            IsEnabled = x.IsEnabled,
            SamplingIntervalMs = x.SamplingIntervalMs,
            Deadband = x.Deadband,
            Scale = x.Scale,
            Offset = x.Offset,
            Unit = x.Unit,
            DisplayFormat = x.DisplayFormat,
            Minimum = x.Minimum,
            Maximum = x.Maximum,
            CreationTime = x.CreationTime,
            LastModificationTime = x.LastModificationTime,
        };

    private static PlcTagValueDto MapValue(PlcTag tag, PlcValue value) =>
        new()
        {
            TagId = tag.Id,
            Code = tag.Code,
            RawValue = value.Value,
            EngineeringValue = ToEngineering(tag, value.Value),
            DataType = value.DataType,
            Quality = value.Quality,
            SourceTimestamp = value.SourceTimestamp,
            ServerTimestamp = value.ServerTimestamp,
            ReceivedAt = value.ReceivedAt,
            Error = value.Error,
        };

    private static object? ToEngineering(PlcTag tag, object? raw)
    {
        if (raw == null || raw is bool or string or DateTime or byte[])
            return raw;
        try
        {
            return Convert.ToDouble(raw) * tag.Scale + tag.Offset;
        }
        catch
        {
            return raw;
        }
    }

    private static object? ToRaw(PlcTag tag, object? engineering)
    {
        if (engineering is JsonElement json)
            engineering = json.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => json.GetDouble(),
                JsonValueKind.String => json.GetString(),
                JsonValueKind.Null => null,
                _ => json.ToString(),
            };
        if (engineering == null || tag.DataType is PlcTagDataType.Boolean or PlcTagDataType.String)
            return engineering;
        double value = Convert.ToDouble(engineering);
        if (!double.IsFinite(value))
            throw new UserFriendlyException("写入值必须是有限数值");
        if (tag.Minimum.HasValue && value < tag.Minimum.Value)
            throw new UserFriendlyException($"写入值低于下限 {tag.Minimum}");
        if (tag.Maximum.HasValue && value > tag.Maximum.Value)
            throw new UserFriendlyException($"写入值高于上限 {tag.Maximum}");
        return (value - tag.Offset) / tag.Scale;
    }

    private async Task<List<PlcTag>> GetTagsAsync(Guid deviceId, IEnumerable<Guid> ids)
    {
        Guid[] tagIds = ids.Distinct().ToArray();
        List<PlcTag> tags = await AsyncExecuter.ToListAsync(
            (await _tags.GetQueryableAsync()).Where(x =>
                x.PlcDeviceId == deviceId && tagIds.Contains(x.Id) && x.IsEnabled
            )
        );
        if (tags.Count != tagIds.Length)
            throw new UserFriendlyException("部分 PLC 点位不存在、未启用或不属于该设备");
        return tags;
    }

    private async Task<PlcTag> GetOwnedTagAsync(Guid deviceId, Guid tagId)
    {
        PlcTag tag = await _tags.GetAsync(tagId);
        if (tag.PlcDeviceId != deviceId)
            throw new UserFriendlyException("PLC 点位不属于该设备");
        return tag;
    }

    private async Task LogAsync(
        Guid deviceId,
        Guid? tagId,
        PlcOperationType operation,
        bool success,
        string? summary = null,
        string? error = null
    )
    {
        await _logs.InsertAsync(
            new PlcOperationLog(
                GuidGenerator.Create(),
                deviceId,
                tagId,
                operation,
                success,
                summary,
                error
            )
        );
    }
}
