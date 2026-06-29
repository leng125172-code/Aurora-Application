using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace AuroraStruct3D.OpenCV.Registry;

/// <summary>
/// 算子注册表实现。
/// 程序启动时通过 <see cref="InitializeAsync"/> 扫描程序集，
/// 将算子元数据和端口信息写入 Redis；后续请求直接读缓存。
/// </summary>
internal sealed class OperatorRegistry : IOperatorRegistry
{
    // Redis Key 约定
    private const string AllOperatorsCacheKey = "opencv:operators";
    private const string ParamsCacheKeyFmt = "opencv:op:{0:N}:params";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // 算子元数据在运行期间不变，缓存条目不设置过期
    private static readonly DistributedCacheEntryOptions NeverExpire = new();

    private readonly IDistributedCache _cache;
    private readonly ILogger<OperatorRegistry> _logger;
    private readonly IReadOnlyList<Assembly> _assemblies;

    // Guid → 算子 CLR 类型映射，首次使用时扫描程序集惰性构建（不依赖 Redis）。
    private readonly object _typeMapLock = new();
    private Dictionary<Guid, Type>? _typeMap;

    public OperatorRegistry(
        IDistributedCache cache,
        ILogger<OperatorRegistry> logger,
        OperatorScanAssemblies scanAssemblies
    )
    {
        _cache = cache;
        _logger = logger;
        _assemblies = scanAssemblies.Items;
    }

    /// <summary>
    /// 从 Redis 读取所有算子列表；缓存未命中时重新扫描（容灾场景）。
    /// </summary>
    public async Task<IReadOnlyList<OperatorDescriptor>> GetAllOperatorsAsync(
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            byte[]? cached = await _cache.GetAsync(AllOperatorsCacheKey, cancellationToken);
            if (cached is not null)
            {
                return JsonSerializer.Deserialize<List<OperatorDescriptor>>(cached, JsonOptions)
                    ?? [];
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取算子列表缓存失败，将重新扫描");
        }

        // 缓存丢失（Redis 被清空/重启）时回源扫描
        return await ScanAndCacheAllAsync(cancellationToken);
    }

    /// <summary>
    /// 根据算子 GUID 从 Redis 读取端口描述；找不到时返回 null。
    /// </summary>
    public async Task<OperatorParametersDescriptor?> GetParametersAsync(
        Guid operatorId,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            string key = string.Format(ParamsCacheKeyFmt, operatorId);
            byte[]? cached = await _cache.GetAsync(key, cancellationToken);
            if (cached is not null)
            {
                return JsonSerializer.Deserialize<OperatorParametersDescriptor>(
                    cached,
                    JsonOptions
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取算子 {OperatorId} 端口描述缓存失败", operatorId);
        }

        return null;
    }

    /// <summary>
    /// 根据算子 GUID 解析 CLR 类型。首次调用时扫描程序集惰性构建映射，后续直接命中内存缓存。
    /// </summary>
    public Task<Type?> GetOperatorTypeAsync(
        Guid operatorId,
        CancellationToken cancellationToken = default
    )
    {
        Dictionary<Guid, Type> map = EnsureTypeMap();
        return Task.FromResult(map.GetValueOrDefault(operatorId));
    }

    /// <summary>
    /// 惰性构建并缓存 Guid → 算子类型映射（线程安全，双重检查）。
    /// 扫描逻辑与 <see cref="ScanAndCacheAllAsync"/> 一致：仅收录实现 <see cref="IOperator"/>
    /// 且带合法 <see cref="GuidAttribute"/> 的具体类。
    /// </summary>
    private Dictionary<Guid, Type> EnsureTypeMap()
    {
        if (_typeMap is not null)
            return _typeMap;

        lock (_typeMapLock)
        {
            if (_typeMap is not null)
                return _typeMap;

            var map = new Dictionary<Guid, Type>();

            foreach (Assembly assembly in _assemblies)
            {
                IEnumerable<Type> types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.OfType<Type>();
                }

                foreach (Type type in types)
                {
                    if (!type.IsClass || type.IsAbstract)
                        continue;
                    if (!typeof(IOperator).IsAssignableFrom(type))
                        continue;

                    var guidAttr = type.GetCustomAttribute<GuidAttribute>();
                    if (guidAttr is null || !Guid.TryParse(guidAttr.Value, out Guid id))
                        continue;

                    map[id] = type;
                }
            }

            _typeMap = map;
            return _typeMap;
        }
    }

    /// <summary>
    /// 程序启动时由 <see cref="OperatorRegistryInitializer"/> 调用，
    /// 扫描所有注册程序集并将结果写入 Redis。
    /// </summary>
    internal async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "开始扫描算子，扫描程序集：{Assemblies}",
            string.Join(", ", _assemblies.Select(a => a.GetName().Name))
        );

        await ScanAndCacheAllAsync(cancellationToken);
    }

    // ── 私有实现 ───────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<OperatorDescriptor>> ScanAndCacheAllAsync(
        CancellationToken cancellationToken
    )
    {
        var operators = new List<OperatorDescriptor>();

        foreach (Assembly assembly in _assemblies)
        {
            IEnumerable<Type> types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                _logger.LogWarning(
                    ex,
                    "程序集 {Assembly} 部分类型加载失败，已跳过无法加载的类型",
                    assembly.GetName().Name
                );
                types = ex.Types.OfType<Type>();
            }

            foreach (Type type in types)
            {
                if (!type.IsClass || type.IsAbstract)
                    continue;
                if (!typeof(IOperator).IsAssignableFrom(type))
                    continue;

                OperatorDescriptor? descriptor = TryBuildDescriptor(type);
                if (descriptor is null)
                    continue;

                operators.Add(descriptor);
                await TryCacheParametersAsync(type, descriptor.Id, cancellationToken);
            }
        }

        // 按 Category / DisplayName 两级排序，保证前端展示顺序稳定
        operators.Sort(
            (a, b) =>
            {
                int c = string.Compare(a.Category, b.Category, StringComparison.OrdinalIgnoreCase);
                if (c != 0)
                    return c;

                return string.Compare(
                    a.DisplayName,
                    b.DisplayName,
                    StringComparison.OrdinalIgnoreCase
                );
            }
        );

        try
        {
            byte[] data = JsonSerializer.SerializeToUtf8Bytes(operators, JsonOptions);
            await _cache.SetAsync(AllOperatorsCacheKey, data, NeverExpire, cancellationToken);
            _logger.LogInformation(
                "算子注册表初始化完成，共扫描到 {Count} 个算子",
                operators.Count
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "算子列表写入 Redis 失败");
        }

        return operators;
    }

    private OperatorDescriptor? TryBuildDescriptor(Type type)
    {
        var guidAttr = type.GetCustomAttribute<GuidAttribute>();
        if (guidAttr is null)
        {
            _logger.LogWarning("算子 {TypeName} 缺少 [Guid] 特性，已跳过注册", type.FullName);
            return null;
        }

        if (!Guid.TryParse(guidAttr.Value, out Guid operatorId))
        {
            _logger.LogWarning(
                "算子 {TypeName} 的 [Guid] 值 '{Value}' 格式无效，已跳过注册",
                type.FullName,
                guidAttr.Value
            );
            return null;
        }

        return new OperatorDescriptor
        {
            Id = operatorId,
            Category = type.GetCustomAttribute<CategoryAttribute>()?.Category ?? "未分类",
            DisplayName = type.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? type.Name,
            Description = type.GetCustomAttribute<DescriptionAttribute>()?.Description,
            TypeFullName = type.FullName ?? type.Name,
        };
    }

    private async Task TryCacheParametersAsync(
        Type operatorType,
        Guid operatorId,
        CancellationToken cancellationToken
    )
    {
        try
        {
            IReadOnlyList<ParameterDescriptor> inputs = ReadStaticParameters(
                operatorType,
                "InputVisionParameters"
            );
            IReadOnlyList<ParameterDescriptor> outputs = ReadStaticParameters(
                operatorType,
                "OutputVisionParameters"
            );

            var paramsDescriptor = new OperatorParametersDescriptor
            {
                OperatorId = operatorId,
                Inputs = inputs,
                Outputs = outputs,
                Config = ReadConfigParameters(operatorType),
            };

            string key = string.Format(ParamsCacheKeyFmt, operatorId);
            byte[] data = JsonSerializer.SerializeToUtf8Bytes(paramsDescriptor, JsonOptions);
            await _cache.SetAsync(key, data, NeverExpire, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "缓存算子 {TypeName} 端口描述失败", operatorType.FullName);
        }
    }

    private static IReadOnlyList<ParameterDescriptor> ReadStaticParameters(
        Type operatorType,
        string propertyName
    )
    {
        var property = operatorType.GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Static
        );

        if (property is null)
            return [];

        if (property.GetValue(null) is not List<IVisionParameter> parameters)
            return [];

        return parameters
            .Select(p =>
            {
                string? displayName = p.DisplayName;
                if (displayName == null)
                {
                    displayName = p.GetType()
                        .GetCustomAttribute<DisplayNameAttribute>()
                        ?.DisplayName;
                }

                var matType =
                    p.GetType().GetCustomAttribute<MatTypeAttribute>()?.MatType
                    ?? PortMatType.Generic;

                return new ParameterDescriptor
                {
                    ParameterName = p.ParameterName,
                    DisplayName = displayName,
                    ParameterTypeName = p.ParameterType.FullName ?? p.ParameterType.Name,
                    DefaultValue = p.DefaultValue,
                    ValueLimit = p.ValueLimit,
                    ErrorCheck = p.ErrorCheck,
                    ControlType = p.ControlType,
                    MatType = matType,
                };
            })
            .ToList()
            .AsReadOnly();
    }

    /// <summary>反射读取算子的静态 ConfigParameters 属性，输出可序列化的配置参数描述列表。</summary>
    private static IReadOnlyList<ConfigParameterDescriptor> ReadConfigParameters(Type operatorType)
    {
        var property = operatorType.GetProperty(
            "ConfigParameters",
            BindingFlags.Public | BindingFlags.Static
        );

        if (property is null)
            return [];

        if (property.GetValue(null) is not List<IConfigParameter> configs)
            return [];

        return configs
            .Select(c => new ConfigParameterDescriptor
            {
                Name = c.Name,
                DisplayName = c.DisplayName,
                ParameterTypeName = c.ParameterType.FullName ?? c.ParameterType.Name,
                DefaultValue = c.DefaultValue,
                ValueLimit = c.ValueLimit,
                Required = c.Required,
                ControlType = c.ControlType,
            })
            .ToList()
            .AsReadOnly();
    }
}
