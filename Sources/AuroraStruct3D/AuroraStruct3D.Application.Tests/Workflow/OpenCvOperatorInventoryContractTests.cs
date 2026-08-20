using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using AuroraStruct3D.OpenCV;
using AuroraStruct3D.OpenCV.Registry;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public class OpenCvOperatorInventoryContractTests
{
    [Fact]
    public void AllOperators_Should_Declare_Runtime_Metadata()
    {
        IReadOnlyList<OperatorInventoryItem> operators = GetOperatorInventory();

        Assert.NotEmpty(operators);
        Assert.Equal(
            operators.Count,
            operators.Select(x => x.Guid).Distinct(StringComparer.OrdinalIgnoreCase).Count()
        );

        foreach (OperatorInventoryItem op in operators)
        {
            Assert.False(string.IsNullOrWhiteSpace(op.Guid), $"{op.Type.FullName} 缺少 Guid。");
            Assert.False(
                string.IsNullOrWhiteSpace(op.Category),
                $"{op.Type.FullName} 缺少 Category。"
            );
            Assert.False(
                string.IsNullOrWhiteSpace(op.DisplayName),
                $"{op.Type.FullName} 缺少 DisplayName。"
            );
            Assert.False(
                string.IsNullOrWhiteSpace(op.Description),
                $"{op.Type.FullName} 缺少 Description。"
            );
        }
    }

    [Fact]
    public void AllOperators_Should_Expose_Static_Workflow_Metadata_Properties()
    {
        foreach (Type type in GetOperatorTypes())
        {
            AssertStaticProperty(type, "InputVisionParameters");
            AssertStaticProperty(type, "OutputVisionParameters");
            AssertStaticProperty(type, "ConfigParameters");
        }
    }

    [Fact]
    public async Task AddOpenCvServices_Should_Expose_All_Runtime_Operators_Through_Registry()
    {
        IReadOnlyList<OperatorInventoryItem> inventory = GetOperatorInventory();

        using ServiceProvider provider = CreateOpenCvServiceProvider();
        IOperatorRegistry registry = provider.GetRequiredService<IOperatorRegistry>();
        IReadOnlyList<OperatorDescriptor> registered = await registry.GetAllOperatorsAsync();

        Assert.Equal(inventory.Count, registered.Count);

        Dictionary<Guid, OperatorInventoryItem> inventoryById = inventory.ToDictionary(x =>
            x.OperatorId
        );

        foreach (OperatorDescriptor descriptor in registered)
        {
            Assert.True(
                inventoryById.TryGetValue(descriptor.Id, out OperatorInventoryItem? expected),
                $"运行时注册了未出现在源码清单里的算子 {descriptor.Id}。"
            );

            Assert.Equal(expected!.Category, descriptor.Category);
            Assert.Equal(expected.DisplayName, descriptor.DisplayName);
            Assert.Equal(expected.Description, descriptor.Description);
            Assert.Equal(expected.Type.FullName, descriptor.TypeFullName);

            Type? operatorType = await registry.GetOperatorTypeAsync(descriptor.Id);
            Assert.Equal(expected.Type, operatorType);

            OperatorParametersDescriptor? parameters = await registry.GetParametersAsync(
                descriptor.Id
            );
            Assert.NotNull(parameters);
            Assert.Equal(descriptor.Id, parameters!.OperatorId);
            Assert.NotNull(parameters.Inputs);
            Assert.NotNull(parameters.Outputs);
            Assert.NotNull(parameters.Config);
            Assert.All(parameters.Inputs, x => Assert.False(string.IsNullOrWhiteSpace(x.Description),
                $"{descriptor.DisplayName} 的输入参数 {x.ParameterName} 缺少说明。"));
            Assert.All(parameters.Outputs, x => Assert.False(string.IsNullOrWhiteSpace(x.Description),
                $"{descriptor.DisplayName} 的输出参数 {x.ParameterName} 缺少说明。"));
            Assert.All(parameters.Config, x => Assert.False(string.IsNullOrWhiteSpace(x.Description),
                $"{descriptor.DisplayName} 的配置参数 {x.Name} 缺少说明。"));
        }
    }

    [Fact]
    public async Task WorkflowNodePalette_Should_Expose_All_Registered_Operators()
    {
        IReadOnlyList<OperatorInventoryItem> inventory = GetOperatorInventory();

        using ServiceProvider provider = CreateOpenCvServiceProvider();
        IOperatorRegistry registry = provider.GetRequiredService<IOperatorRegistry>();
        var appService = new global::AuroraStruct3D.Workflow.WorkflowNodePaletteAppService(
            registry
        );

        global::AuroraStruct3D.Workflow.Dtos.NodePaletteDto palette = await appService.GetAsync();

        Dictionary<string, string?> overlayModes = palette
            .Categories.SelectMany(x => x.Nodes ?? [])
            .Where(x => x.OverlayMode is not null)
            .ToDictionary(x => x.Id, x => x.OverlayMode, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("plane", overlayModes["c38e27a6-8d49-4c41-96a0-a53f30c23101"]);
        Assert.Equal("region", overlayModes["d420473b-76f1-455a-83e4-492809c23102"]);
        Assert.Equal("none", overlayModes["e15fb284-2abe-477c-bd47-3cfde9c23103"]);

        Assert.Equal(
            3 + inventory.Select(x => x.Category).Distinct(StringComparer.Ordinal).Count(),
            palette.Categories.Count
        );

        string[] builtinCategoryNames = palette.Categories.Take(3).Select(x => x.Name).ToArray();
        Assert.Equal(new[] { "流程边界", "流程控制", "参数赋值" }, builtinCategoryNames);
        Assert.Equal(2, palette.Categories[0].Nodes?.Count);
        Assert.Equal(2, palette.Categories[1].Nodes?.Count);
        Assert.Equal(1, palette.Categories[2].Nodes?.Count);

        Dictionary<string, List<OperatorInventoryItem>> inventoryByCategory = inventory
            .GroupBy(x => x.Category, StringComparer.Ordinal)
            .ToDictionary(
                x => x.Key,
                x => x.OrderBy(item => item.DisplayName, StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal
            );
        string[] expectedCategoryOrder = inventoryByCategory
            .Keys.OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        string[] actualCategoryOrder = palette.Categories.Skip(3).Select(x => x.Name).ToArray();
        Assert.Equal(expectedCategoryOrder, actualCategoryOrder);

        Dictionary<Guid, OperatorInventoryItem> inventoryById = inventory.ToDictionary(x =>
            x.OperatorId
        );

        int operatorNodeCount = 0;
        foreach (
            global::AuroraStruct3D.Workflow.Dtos.NodeCategoryDto category in palette.Categories.Skip(
                3
            )
        )
        {
            Assert.True(
                inventoryByCategory.TryGetValue(
                    category.Name,
                    out List<OperatorInventoryItem>? expectedOperators
                ),
                $"节点面板暴露了未知分类 {category.Name}。"
            );

            Assert.NotNull(category.Nodes);
            Assert.Equal(expectedOperators!.Count, category.Nodes!.Count);
            Assert.Equal(
                expectedOperators.Select(x => x.DisplayName).ToArray(),
                category.Nodes.Select(x => x.DisplayName).ToArray()
            );

            foreach (global::AuroraStruct3D.Workflow.Dtos.NodeDefinitionDto node in category.Nodes)
            {
                operatorNodeCount++;
                Assert.Equal("Operator", node.NodeType);
                Assert.False(node.HasBody);
                Assert.False(node.IsBoundary);

                Guid operatorId = Guid.Parse(node.Id);
                Assert.True(
                    inventoryById.TryGetValue(operatorId, out OperatorInventoryItem? expected),
                    $"节点面板暴露了未知算子 {node.Id}。"
                );

                Assert.Equal(expected!.DisplayName, node.DisplayName);
                Assert.Equal(expected.Description, node.Description);

                OperatorParametersDescriptor? parameters = await registry.GetParametersAsync(
                    operatorId
                );
                Assert.NotNull(parameters);
                Assert.Equal(parameters!.Inputs.Count, node.InputPorts.Count);
                Assert.Equal(parameters.Outputs.Count, node.OutputPorts.Count);
                Assert.Equal(parameters.Config.Count, node.ConfigFields.Count);
            }
        }

        Assert.Equal(inventory.Count, operatorNodeCount);
    }

    [Fact]
    public async Task RegisteredOperatorParameters_Should_Have_Valid_Names_And_Match_Constructors()
    {
        IReadOnlyList<OperatorInventoryItem> inventory = GetOperatorInventory();

        using ServiceProvider provider = CreateOpenCvServiceProvider();
        IOperatorRegistry registry = provider.GetRequiredService<IOperatorRegistry>();
        _ = await registry.GetAllOperatorsAsync();

        foreach (OperatorInventoryItem op in inventory)
        {
            OperatorParametersDescriptor? parameters = await registry.GetParametersAsync(
                op.OperatorId
            );
            Assert.NotNull(parameters);

            AssertUniqueNonEmptyNames(
                parameters!.Inputs.Select(x => x.ParameterName),
                $"{op.Type.FullName} 输入端口"
            );
            AssertUniqueNonEmptyNames(
                parameters.Outputs.Select(x => x.ParameterName),
                $"{op.Type.FullName} 输出端口"
            );
            AssertUniqueNonEmptyNames(
                parameters.Config.Select(x => x.Name),
                $"{op.Type.FullName} 配置参数"
            );

            ConstructorInfo[] constructors = op.Type.GetConstructors(
                BindingFlags.Public | BindingFlags.Instance
            );

            Assert.True(
                constructors.Any(ctor => ConstructorMatches(ctor, parameters.Config)),
                $"{op.Type.FullName} 的 ConfigParameters 与任何公共构造函数都不匹配。"
            );
        }
    }

    [Fact]
    public async Task GetParametersAsync_Should_Rebuild_And_Recache_When_Parameter_Cache_Is_Missing()
    {
        OperatorInventoryItem expected = GetOperatorInventory().First();

        using ServiceProvider provider = CreateOpenCvServiceProvider();
        IOperatorRegistry registry = provider.GetRequiredService<IOperatorRegistry>();
        IDistributedCache cache = provider.GetRequiredService<IDistributedCache>();

        _ = await registry.GetAllOperatorsAsync();
        await cache.RemoveAsync($"opencv:v4:op:{expected.OperatorId:N}:params");

        OperatorParametersDescriptor? rebuilt = await registry.GetParametersAsync(
            expected.OperatorId
        );

        Assert.NotNull(rebuilt);
        Assert.Equal(expected.OperatorId, rebuilt!.OperatorId);
        Assert.NotNull(await cache.GetAsync($"opencv:v4:op:{expected.OperatorId:N}:params"));
    }

    private static void AssertStaticProperty(Type type, string propertyName)
    {
        PropertyInfo? property = type.GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Static
        );

        Assert.True(property is not null, $"{type.FullName} 缺少静态属性 {propertyName}。");
        _ = property!.GetValue(null);
    }

    private static IReadOnlyList<Type> GetOperatorTypes()
    {
        return typeof(IOperator)
            .Assembly.GetTypes()
            .Where(type =>
                type.IsClass && !type.IsAbstract && typeof(IOperator).IsAssignableFrom(type)
            )
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<OperatorInventoryItem> GetOperatorInventory()
    {
        string sourceRoot = Path.Combine(
            GetRepositoryRoot(),
            "Sources",
            "AuroraStruct3D",
            "AuroraStruct3D.OpenCV"
        );

        return GetOperatorTypes()
            .Select(type =>
            {
                GuidAttribute guid =
                    type.GetCustomAttribute<GuidAttribute>()
                    ?? throw new Xunit.Sdk.XunitException($"{type.FullName} 缺少 GuidAttribute。");
                CategoryAttribute category =
                    type.GetCustomAttribute<CategoryAttribute>()
                    ?? throw new Xunit.Sdk.XunitException(
                        $"{type.FullName} 缺少 CategoryAttribute。"
                    );
                DisplayNameAttribute displayName =
                    type.GetCustomAttribute<DisplayNameAttribute>()
                    ?? throw new Xunit.Sdk.XunitException(
                        $"{type.FullName} 缺少 DisplayNameAttribute。"
                    );
                DescriptionAttribute description =
                    type.GetCustomAttribute<DescriptionAttribute>()
                    ?? throw new Xunit.Sdk.XunitException(
                        $"{type.FullName} 缺少 DescriptionAttribute。"
                    );

                string sourcePath = Assert.Single(
                    Directory.GetFiles(sourceRoot, $"{type.Name}.cs", SearchOption.AllDirectories)
                );

                return new OperatorInventoryItem(
                    type,
                    guid.Value,
                    category.Category,
                    displayName.DisplayName,
                    description.Description,
                    Path.GetRelativePath(GetRepositoryRoot(), sourcePath).Replace('\\', '/')
                );
            })
            .OrderBy(x => x.Category, StringComparer.Ordinal)
            .ThenBy(x => x.DisplayName, StringComparer.Ordinal)
            .ToList();
    }

    private static ServiceProvider CreateOpenCvServiceProvider()
    {
        return new ServiceCollection()
            .AddLogging()
            .AddDistributedMemoryCache()
            .AddOpenCVServices()
            .BuildServiceProvider();
    }

    private static void AssertUniqueNonEmptyNames(
        IEnumerable<string?> names,
        string scopeDescription
    )
    {
        string?[] rawValues = names.ToArray();
        Assert.All(
            rawValues,
            value =>
                Assert.False(string.IsNullOrWhiteSpace(value), $"{scopeDescription} 存在空名称。")
        );

        string[] values = rawValues.Select(value => value!).ToArray();
        Assert.Equal(values.Length, values.Distinct(StringComparer.Ordinal).Count());
    }

    private static bool ConstructorMatches(
        ConstructorInfo constructor,
        IReadOnlyList<ConfigParameterDescriptor> configs
    )
    {
        ParameterInfo[] parameters = constructor.GetParameters();
        if (parameters.Length != configs.Count)
        {
            return false;
        }

        for (int index = 0; index < parameters.Length; index++)
        {
            ParameterInfo parameter = parameters[index];
            ConfigParameterDescriptor config = configs[index];

            if (!string.Equals(parameter.Name, config.Name, StringComparison.Ordinal))
            {
                return false;
            }

            string parameterTypeName =
                parameter.ParameterType.FullName ?? parameter.ParameterType.Name;
            if (
                !string.Equals(
                    parameterTypeName,
                    config.ParameterTypeName,
                    StringComparison.Ordinal
                )
            )
            {
                return false;
            }
        }

        return true;
    }

    private static string GetRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Aurora Application.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new Xunit.Sdk.XunitException("无法定位仓库根目录。");
    }

    private sealed record OperatorInventoryItem(
        Type Type,
        string Guid,
        string Category,
        string DisplayName,
        string Description,
        string RelativeSourcePath
    )
    {
        public System.Guid OperatorId => System.Guid.Parse(Guid);
    }

}
