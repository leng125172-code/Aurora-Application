using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using AuroraStruct3D.OpenCV;
using AuroraStruct3D.OpenCV.Registry;
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
    public void DetectionOperatorDocument_Should_Match_Runtime_Inventory()
    {
        IReadOnlyList<OperatorInventoryItem> operators = GetOperatorInventory();
        OperatorDocumentSnapshot document = ParseOperatorDocument();

        Assert.Equal(operators.Count, document.TotalCount);

        Dictionary<string, List<OperatorInventoryItem>> actualByCategory = operators
            .GroupBy(x => x.Category, StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .ToDictionary(
                x => x.Key,
                x => x.OrderBy(item => item.DisplayName, StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal
            );

        Assert.Equal(actualByCategory.Count, document.SectionCounts.Count);
        Assert.Equal(actualByCategory.Count, document.StatsCounts.Count);

        foreach ((string category, List<OperatorInventoryItem> items) in actualByCategory)
        {
            Assert.True(document.SectionCounts.ContainsKey(category), $"文档缺少分类 {category}。");
            Assert.True(
                document.SectionPathsByCategory.ContainsKey(category),
                $"文档缺少分类 {category} 的算子表。"
            );
            Assert.True(document.StatsCounts.ContainsKey(category), $"统计表缺少分类 {category}。");

            Assert.Equal(items.Count, document.SectionCounts[category]);
            Assert.Equal(items.Count, document.StatsCounts[category]);

            string[] actualPaths = items
                .Select(x => x.RelativeSourcePath)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();
            string[] documentedPaths = document
                .SectionPathsByCategory[category]
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(actualPaths, documentedPaths);
        }

        Assert.Equal(operators.Count, document.StatsTotal);
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

    private static OperatorDocumentSnapshot ParseOperatorDocument()
    {
        string repositoryRoot = GetRepositoryRoot();
        string documentPath = Path.Combine(repositoryRoot, "Documents", "检测算子.md");
        string documentDirectory = Path.GetDirectoryName(documentPath)!;
        string[] lines = File.ReadAllLines(documentPath);

        string? totalLine = lines.FirstOrDefault(line =>
            line.TrimStart('\uFEFF').StartsWith("当前公开算子总数：", StringComparison.Ordinal)
        );
        Assert.True(totalLine is not null, "文档缺少总数行。");

        Match totalMatch = Regex.Match(totalLine!, "^当前公开算子总数：(\\d+)$");
        Assert.True(totalMatch.Success, "文档总数行格式无效。");

        var sectionCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var sectionPathsByCategory = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        string? currentCategory = null;

        foreach (string line in lines)
        {
            Match sectionMatch = Regex.Match(line, "^### (.+?)（(\\d+) 个）$");
            if (sectionMatch.Success)
            {
                currentCategory = sectionMatch.Groups[1].Value;
                sectionCounts[currentCategory] = int.Parse(sectionMatch.Groups[2].Value);
                sectionPathsByCategory[currentCategory] = [];
                continue;
            }

            if (string.Equals(line, "## 统计", StringComparison.Ordinal))
            {
                currentCategory = null;
                continue;
            }

            if (
                currentCategory is not null
                && TryNormalizeDocumentSourcePath(
                    line,
                    documentDirectory,
                    repositoryRoot,
                    out string path
                )
            )
            {
                sectionPathsByCategory[currentCategory].Add(path);
            }
        }

        var statsCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        int statsTotal = 0;
        int statsHeaderIndex = Array.FindIndex(
            lines,
            line => string.Equals(line, "| 分类 | 数量 |", StringComparison.Ordinal)
        );

        Assert.True(statsHeaderIndex >= 0, "文档缺少统计表。");

        for (int index = statsHeaderIndex + 2; index < lines.Length; index++)
        {
            string line = lines[index];
            if (!line.StartsWith("|", StringComparison.Ordinal))
            {
                break;
            }

            Match statsMatch = Regex.Match(line, "^\\| (.+?) \\| (\\d+) \\|$");
            if (!statsMatch.Success)
            {
                continue;
            }

            string category = statsMatch.Groups[1].Value;
            int count = int.Parse(statsMatch.Groups[2].Value);
            if (string.Equals(category, "合计", StringComparison.Ordinal))
            {
                statsTotal = count;
            }
            else
            {
                statsCounts[category] = count;
            }
        }

        return new OperatorDocumentSnapshot(
            int.Parse(totalMatch.Groups[1].Value),
            sectionCounts,
            sectionPathsByCategory,
            statsCounts,
            statsTotal
        );
    }

    private static bool TryNormalizeDocumentSourcePath(
        string line,
        string documentDirectory,
        string repositoryRoot,
        out string relativePath
    )
    {
        Match pathMatch = Regex.Match(line, "\\]\\(([^)]+\\.cs)\\)");
        if (!pathMatch.Success)
        {
            relativePath = string.Empty;
            return false;
        }

        string rawPath = pathMatch.Groups[1].Value;
        string fullPath = rawPath.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)
            ? new Uri(rawPath).LocalPath
            : Path.GetFullPath(
                Path.Combine(documentDirectory, rawPath.Replace('/', Path.DirectorySeparatorChar))
            );

        relativePath = Path.GetRelativePath(repositoryRoot, fullPath).Replace('\\', '/');
        return true;
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

    private sealed record OperatorDocumentSnapshot(
        int TotalCount,
        IReadOnlyDictionary<string, int> SectionCounts,
        IReadOnlyDictionary<string, List<string>> SectionPathsByCategory,
        IReadOnlyDictionary<string, int> StatsCounts,
        int StatsTotal
    );
}
