using System.Collections.Concurrent;
using System.Text;
using System.Security.Cryptography.X509Certificates;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace AuroraStruct3D.Plcs;

public sealed class OpcUaPlcDriver : IPlcDriver, IPlcBrowsableDriver, IPlcSubscriptionDriver
{
    private sealed class OpcConnection : IPlcConnection
    {
        private readonly PlcConnectionOptions _options;
        private readonly Action<Session> _cleanupSubscriptions;
        private SessionReconnectHandler? _reconnect;
        public Session Session { get; private set; }

        public OpcConnection(Session session, PlcConnectionOptions options, Action<Session> cleanupSubscriptions)
        {
            Session = session;
            _options = options;
            _cleanupSubscriptions = cleanupSubscriptions;
            Session.KeepAlive += OnKeepAlive;
        }

        public bool IsConnected => Session.Connected;
        public string? ServerCertificateThumbprint
        {
            get
            {
                byte[]? raw = Session.ConfiguredEndpoint.Description.ServerCertificate;
                if (raw is not { Length: > 0 })
                    return null;
                using var certificate = X509CertificateLoader.LoadCertificate(raw);
                return certificate.Thumbprint;
            }
        }
        public string? ServerCertificateSubject
        {
            get
            {
                using X509Certificate2? certificate = GetServerCertificate();
                return certificate?.Subject;
            }
        }
        public DateTime? ServerCertificateNotBefore
        {
            get
            {
                using X509Certificate2? certificate = GetServerCertificate();
                return certificate?.NotBefore.ToUniversalTime();
            }
        }
        public DateTime? ServerCertificateNotAfter
        {
            get
            {
                using X509Certificate2? certificate = GetServerCertificate();
                return certificate?.NotAfter.ToUniversalTime();
            }
        }
        public Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(
                Session.Connected
                    || (_reconnect != null
                        && _reconnect.State
                            != SessionReconnectHandler.ReconnectState.Ready)
            );
        public async ValueTask DisposeAsync()
        {
            Session.KeepAlive -= OnKeepAlive;
            _reconnect?.Dispose();
            _cleanupSubscriptions(Session);
            await Session.CloseAsync().ConfigureAwait(false);
            Session.Dispose();
        }

        private void OnKeepAlive(ISession sender, KeepAliveEventArgs e)
        {
            if (ServiceResult.IsGood(e.Status))
                return;
            if (
                _reconnect != null
                && _reconnect.State != SessionReconnectHandler.ReconnectState.Ready
            )
                return;
            _reconnect ??= new SessionReconnectHandler(true, _options.ReconnectMaxMs);
            _reconnect.BeginReconnect(sender, _options.ReconnectInitialMs, OnReconnectComplete);
        }

        private X509Certificate2? GetServerCertificate()
        {
            byte[]? raw = Session.ConfiguredEndpoint.Description.ServerCertificate;
            return raw is { Length: > 0 }
                ? X509CertificateLoader.LoadCertificate(raw)
                : null;
        }

        private void OnReconnectComplete(object? sender, EventArgs e)
        {
            if (
                sender is not SessionReconnectHandler handler
                || handler.Session is not Session reconnected
            )
                return;
            Session.KeepAlive -= OnKeepAlive;
            Session = reconnected;
            Session.KeepAlive += OnKeepAlive;
        }
    }

    private readonly ConcurrentDictionary<string, Subscription> _subscriptions = new();

    public PlcDriverDescriptor Descriptor { get; } =
        new(
            PlcDriverIds.OpcUa,
            PlcProtocolType.OpcUa,
            "OPC UA",
            PlcCapability.Browse
                | PlcCapability.Read
                | PlcCapability.Write
                | PlcCapability.Subscribe
                | PlcCapability.BatchRead
                | PlcCapability.BatchWrite
                | PlcCapability.SecureChannel,
            true
        );

    public async Task<IPlcConnection> ConnectAsync(
        PlcConnectionOptions options,
        CancellationToken cancellationToken = default
    )
    {
        var configuration = new ApplicationConfiguration
        {
            ApplicationName = "AuroraStruct3D OPC UA Client",
            ApplicationUri = $"urn:{Utils.GetHostName()}:AuroraStruct3D:OpcUaClient",
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = "pki/own",
                    SubjectName = "CN=AuroraStruct3D OPC UA Client",
                },
                TrustedPeerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = "pki/trusted",
                },
                TrustedIssuerCertificates = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = "pki/issuers",
                },
                RejectedCertificateStore = new CertificateTrustList
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = "pki/rejected",
                },
                AutoAcceptUntrustedCertificates = options.AutoTrustServerCertificate,
                RejectSHA1SignedCertificates = true,
                MinimumCertificateKeySize = 2048,
            },
            TransportConfigurations = new TransportConfigurationCollection(),
            TransportQuotas = new TransportQuotas
            {
                OperationTimeout = options.OperationTimeoutMs,
            },
            ClientConfiguration = new ClientConfiguration
            {
                DefaultSessionTimeout = options.SessionTimeoutMs,
            },
        };
        await configuration.ValidateAsync(ApplicationType.Client).ConfigureAwait(false);
        configuration.CertificateValidator.CertificateValidation += (_, e) =>
        {
            if (options.AutoTrustServerCertificate)
                e.Accept = true;
        };

        var application = new ApplicationInstance
        {
            ApplicationName = configuration.ApplicationName,
            ApplicationType = ApplicationType.Client,
            ApplicationConfiguration = configuration,
        };
        await application.CheckApplicationInstanceCertificatesAsync(false).ConfigureAwait(false);

        EndpointDescription endpoint = CoreClientUtils.SelectEndpoint(
            configuration,
            options.EndpointUrl,
            options.MessageSecurityMode != PlcMessageSecurityMode.None
        );
        MessageSecurityMode expectedMode = options.MessageSecurityMode switch
        {
            PlcMessageSecurityMode.Sign => MessageSecurityMode.Sign,
            PlcMessageSecurityMode.SignAndEncrypt => MessageSecurityMode.SignAndEncrypt,
            _ => MessageSecurityMode.None,
        };
        if (endpoint.SecurityMode != expectedMode)
            throw new ServiceResultException(
                StatusCodes.BadSecurityModeRejected,
                $"服务端未提供指定消息安全模式：{options.MessageSecurityMode}"
            );
        if (
            !string.Equals(options.SecurityPolicy, "None", StringComparison.OrdinalIgnoreCase)
            && !endpoint.SecurityPolicyUri.EndsWith(
                options.SecurityPolicy,
                StringComparison.OrdinalIgnoreCase
            )
        )
            throw new ServiceResultException(
                StatusCodes.BadSecurityPolicyRejected,
                $"服务端未提供指定安全策略：{options.SecurityPolicy}"
            );
        var configuredEndpoint = new ConfiguredEndpoint(
            null,
            endpoint,
            EndpointConfiguration.Create(configuration)
        );
        IUserIdentity identity = options.AuthenticationType switch
        {
            PlcAuthenticationType.UserName => new UserIdentity(
                options.UserName ?? string.Empty,
                Encoding.UTF8.GetBytes(options.Password ?? string.Empty)
            ),
            PlcAuthenticationType.Certificate when !string.IsNullOrWhiteSpace(
                options.ClientCertificatePath
            ) => new UserIdentity(
                X509CertificateLoader.LoadPkcs12FromFile(
                    options.ClientCertificatePath,
                    options.ClientCertificatePassword
                )
            ),
            _ => new UserIdentity(new AnonymousIdentityToken()),
        };

        Session session = await Session.Create(
                configuration,
                configuredEndpoint,
                false,
                false,
                configuration.ApplicationName,
                (uint)options.SessionTimeoutMs,
                identity,
                null,
                cancellationToken
            )
            .ConfigureAwait(false);
        return new OpcConnection(session, options, CleanupSubscriptions);
    }

    public Task ValidateAddressAsync(
        string address,
        PlcTagDataType dataType,
        CancellationToken cancellationToken = default
    )
    {
        _ = NodeId.Parse(address);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PlcValue>> ReadAsync(
        IPlcConnection connection,
        IReadOnlyList<PlcReadRequest> requests,
        CancellationToken cancellationToken = default
    )
    {
        Session session = Require(connection);
        var nodes = new ReadValueIdCollection(
            requests.Select(x => new ReadValueId
            {
                NodeId = NodeId.Parse(x.Address),
                AttributeId = Attributes.Value,
            })
        );
        session.Read(
            null,
            0,
            TimestampsToReturn.Both,
            nodes,
            out DataValueCollection values,
            out _
        );
        var result = requests
            .Select(
                (request, i) =>
                    new PlcValue(
                        request.Key,
                        values[i].Value,
                        request.DataType,
                        values[i].StatusCode.ToString(),
                        values[i].SourceTimestamp,
                        values[i].ServerTimestamp,
                        DateTime.UtcNow,
                        StatusCode.IsBad(values[i].StatusCode)
                            ? values[i].StatusCode.ToString()
                            : null
                    )
            )
            .ToArray();
        return Task.FromResult<IReadOnlyList<PlcValue>>(result);
    }

    public Task<IReadOnlyList<PlcWriteResult>> WriteAsync(
        IPlcConnection connection,
        IReadOnlyList<PlcWriteRequest> requests,
        CancellationToken cancellationToken = default
    )
    {
        Session session = Require(connection);
        var writes = new WriteValueCollection(
            requests.Select(x => new WriteValue
            {
                NodeId = NodeId.Parse(x.Address),
                AttributeId = Attributes.Value,
                Value = new DataValue(new Variant(ConvertValue(x.Value, x.DataType))),
            })
        );
        session.Write(null, writes, out StatusCodeCollection statuses, out _);
        return Task.FromResult<IReadOnlyList<PlcWriteResult>>(
            requests
                .Select(
                    (x, i) =>
                        new PlcWriteResult(
                            x.Key,
                            StatusCode.IsGood(statuses[i]),
                            StatusCode.IsBad(statuses[i]) ? statuses[i].ToString() : null
                        )
                )
                .ToArray()
        );
    }

    public Task<PlcBrowseResult> BrowseAsync(
        IPlcConnection connection,
        PlcBrowseRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Session session = Require(connection);
        NodeId parent = string.IsNullOrWhiteSpace(request.ParentAddress)
            ? ObjectIds.ObjectsFolder
            : NodeId.Parse(request.ParentAddress);
        session.Browse(
            null,
            null,
            parent,
            (uint)request.MaxResults,
            BrowseDirection.Forward,
            ReferenceTypeIds.HierarchicalReferences,
            true,
            (uint)(NodeClass.Object | NodeClass.Variable),
            out byte[] continuationPoint,
            out ReferenceDescriptionCollection references
        );
        var allReferences = new List<ReferenceDescription>(references);
        while (continuationPoint is { Length: > 0 } && allReferences.Count < request.MaxResults)
        {
            session.BrowseNext(
                null,
                false,
                continuationPoint,
                out continuationPoint,
                out ReferenceDescriptionCollection nextReferences
            );
            allReferences.AddRange(nextReferences);
        }
        if (continuationPoint is { Length: > 0 })
        {
            session.BrowseNext(null, true, continuationPoint, out _, out _);
        }

        var items = allReferences
            .Take(request.MaxResults)
            .Select(x =>
                new PlcBrowseNode(
                    ExpandedNodeId.ToNodeId(x.NodeId, session.NamespaceUris)?.ToString()
                        ?? x.NodeId.ToString(),
                    x.BrowseName.Name ?? string.Empty,
                    x.DisplayName.Text ?? x.BrowseName.Name ?? string.Empty,
                    x.NodeClass.ToString(),
                    null,
                    x.NodeClass == NodeClass.Variable
                        ? PlcTagAccess.ReadWrite
                        : PlcTagAccess.None,
                    x.NodeClass == NodeClass.Object
                )
            )
            .ToArray();
        return Task.FromResult(new PlcBrowseResult(items, null));
    }

    public Task<string> SubscribeAsync(
        IPlcConnection connection,
        IReadOnlyList<PlcSubscriptionItem> items,
        Func<PlcValue, Task> onValue,
        CancellationToken cancellationToken = default
    )
    {
        Session session = Require(connection);
        var subscription = new Subscription(session.DefaultSubscription)
        {
            PublishingInterval = items.Count == 0
                ? PlcConsts.DefaultSamplingIntervalMs
                : items.Min(x => x.SamplingIntervalMs),
        };
        foreach (PlcSubscriptionItem item in items)
        {
            var callbackGate = new SemaphoreSlim(1, 1);
            var monitored = new MonitoredItem(subscription.DefaultItem)
            {
                DisplayName = item.Key,
                StartNodeId = NodeId.Parse(item.Address),
                AttributeId = Attributes.Value,
                SamplingInterval = item.SamplingIntervalMs,
                QueueSize = 10,
                DiscardOldest = true,
            };
            if (item.Deadband.HasValue)
                monitored.Filter = new DataChangeFilter
                {
                    Trigger = DataChangeTrigger.StatusValueTimestamp,
                    DeadbandType = (uint)DeadbandType.Absolute,
                    DeadbandValue = item.Deadband.Value,
                };
            monitored.Notification += async (sender, e) =>
            {
                await callbackGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    foreach (DataValue value in sender.DequeueValues())
                        await onValue(new PlcValue(item.Key, value.Value, item.DataType,
                            value.StatusCode.ToString(), value.SourceTimestamp,
                            value.ServerTimestamp, DateTime.UtcNow)).ConfigureAwait(false);
                }
                catch
                {
                    // OPC SDK event callbacks cannot propagate failures to the publisher.
                    // The serialized gate still prevents overlapping/out-of-order delivery.
                }
                finally
                {
                    callbackGate.Release();
                }
            };
            subscription.AddItem(monitored);
        }
        session.AddSubscription(subscription);
        subscription.Create();
        string id = Guid.NewGuid().ToString("N");
        _subscriptions[id] = subscription;
        return Task.FromResult(id);
    }

    public Task UnsubscribeAsync(
        IPlcConnection connection,
        string subscriptionId,
        CancellationToken cancellationToken = default
    )
    {
        if (_subscriptions.TryRemove(subscriptionId, out Subscription? subscription))
        {
            subscription.Delete(true);
            subscription.Dispose();
        }
        return Task.CompletedTask;
    }

    private static Session Require(IPlcConnection connection) =>
        connection is OpcConnection opc
            ? opc.Session
            : throw new ArgumentException("连接不属于 OPC UA 驱动", nameof(connection));

    private void CleanupSubscriptions(Session session)
    {
        foreach ((string id, Subscription subscription) in _subscriptions.ToArray())
        {
            if (!ReferenceEquals(subscription.Session, session)
                || !_subscriptions.TryRemove(id, out Subscription? removed))
                continue;
            try { removed.Delete(true); } catch { }
            removed.Dispose();
        }
    }

    private static object? ConvertValue(object? value, PlcTagDataType type)
    {
        if (value == null)
            return null;
        string text = value.ToString() ?? string.Empty;
        return type switch
        {
            PlcTagDataType.Boolean => Convert.ToBoolean(value),
            PlcTagDataType.SByte => Convert.ToSByte(value),
            PlcTagDataType.Byte => Convert.ToByte(value),
            PlcTagDataType.Int16 => Convert.ToInt16(value),
            PlcTagDataType.UInt16 => Convert.ToUInt16(value),
            PlcTagDataType.Int32 => Convert.ToInt32(value),
            PlcTagDataType.UInt32 => Convert.ToUInt32(value),
            PlcTagDataType.Int64 => Convert.ToInt64(value),
            PlcTagDataType.UInt64 => Convert.ToUInt64(value),
            PlcTagDataType.Float => Convert.ToSingle(value),
            PlcTagDataType.Double => Convert.ToDouble(value),
            PlcTagDataType.DateTime => Convert.ToDateTime(value),
            PlcTagDataType.ByteString => Convert.FromBase64String(text),
            _ => text,
        };
    }
}
