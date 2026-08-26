using System.Text.Json;
using AuroraStruct3D.OpenCV.Registry;
using AuroraStruct3D.Workflow;
using AuroraStruct3D.Workflow.Dtos;
using Volo.Abp;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public class WorkflowOperatorSnippetTests
{
    [Fact]
    public void Fixed_Resource_And_Number_Config_Should_Be_Emitted_As_Literals()
    {
        Guid projectId = Guid.Parse("5fe13d7a-f230-40ef-9182-2bca90876aad");
        OperatorParametersDescriptor parameters = new()
        {
            OperatorId = Guid.NewGuid(),
            Inputs = [],
            Outputs =
            [
                new ParameterDescriptor
                {
                    ParameterName = "scan_point_cloud",
                    ParameterTypeName = "PointCloudData",
                },
                new ParameterDescriptor
                {
                    ParameterName = "scan_project_id",
                    ParameterTypeName = typeof(string).FullName!,
                },
            ],
            Config =
            [
                new ConfigParameterDescriptor
                {
                    Name = "calibProjectId",
                    ParameterTypeName = typeof(string).FullName!,
                    Required = true,
                    ControlType = (PortControlType)11,
                },
                new ConfigParameterDescriptor
                {
                    Name = "cycleCount",
                    ParameterTypeName = typeof(int).FullName!,
                    Required = false,
                    DefaultValue = 1,
                    ControlType = PortControlType.Number,
                },
            ],
        };
        Dictionary<string, JsonElement> values = new(StringComparer.Ordinal)
        {
            ["calibProjectId"] = JsonSerializer.SerializeToElement(projectId.ToString("D")),
            ["cycleCount"] = JsonSerializer.SerializeToElement(3),
        };

        string snippet = WorkflowIdeAppService.BuildOperatorCompletion(
            "read_online_point_cloud",
            parameters,
            values);

        Assert.Contains($"calibProjectId: \"{projectId:D}\"", snippet);
        Assert.Contains("cycleCount: 3", snippet);
        Assert.DoesNotContain("${3:", snippet, StringComparison.Ordinal);
        Assert.StartsWith("var (${1:scan_point_cloud}, ${2:scan_project_id}) = ", snippet);
    }

    [Fact]
    public void Completion_Without_Fixed_Values_Should_Keep_Config_Placeholders()
    {
        OperatorParametersDescriptor parameters = new()
        {
            OperatorId = Guid.NewGuid(),
            Inputs = [],
            Outputs = [],
            Config =
            [
                new ConfigParameterDescriptor
                {
                    Name = "cycleCount",
                    ParameterTypeName = typeof(int).FullName!,
                    DefaultValue = 1,
                    ControlType = PortControlType.Number,
                },
            ],
        };

        string snippet = WorkflowIdeAppService.BuildOperatorCompletion(
            "read_online_point_cloud",
            parameters);

        Assert.Equal(
            "read_online_point_cloud(\n    cycleCount: ${1:1}\n);",
            snippet.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Endpoint_Should_Reject_Invalid_Resource_Guid()
    {
        StubOperatorRegistry registry = new();
        WorkflowIdeAppService service = new(registry, null!, null!);
        WorkflowOperatorSnippetInput input = new()
        {
            OperatorId = registry.Descriptor.Id,
            ConfigValues = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["calibProjectId"] = JsonSerializer.SerializeToElement("not-a-guid"),
                ["cycleCount"] = JsonSerializer.SerializeToElement(2),
            },
        };

        UserFriendlyException error = await Assert.ThrowsAsync<UserFriendlyException>(
            () => service.OperatorSnippetAsync(input));

        Assert.Contains("有效的资源 GUID", error.Message);
    }

    [Fact]
    public async Task Endpoint_Should_Reject_Unknown_Config_Field()
    {
        StubOperatorRegistry registry = new();
        WorkflowIdeAppService service = new(registry, null!, null!);
        WorkflowOperatorSnippetInput input = new()
        {
            OperatorId = registry.Descriptor.Id,
            ConfigValues = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["unknown"] = JsonSerializer.SerializeToElement(true),
            },
        };

        UserFriendlyException error = await Assert.ThrowsAsync<UserFriendlyException>(
            () => service.OperatorSnippetAsync(input));

        Assert.Contains("没有配置参数 'unknown'", error.Message);
    }

    [Fact]
    public async Task Endpoint_Should_Require_Explicit_Value_When_Required_Field_Has_Default()
    {
        StubOperatorRegistry registry = new();
        WorkflowIdeAppService service = new(registry, null!, null!);
        WorkflowOperatorSnippetInput input = new()
        {
            OperatorId = registry.Descriptor.Id,
            ConfigValues = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["calibProjectId"] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString("D")),
            },
        };

        UserFriendlyException error = await Assert.ThrowsAsync<UserFriendlyException>(
            () => service.OperatorSnippetAsync(input));

        Assert.Contains("缺少必填配置参数 'cycleCount'", error.Message);
    }

    [Fact]
    public async Task Endpoint_Should_Reject_Number_Outside_Metadata_Range()
    {
        StubOperatorRegistry registry = new();
        WorkflowIdeAppService service = new(registry, null!, null!);
        WorkflowOperatorSnippetInput input = new()
        {
            OperatorId = registry.Descriptor.Id,
            ConfigValues = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["calibProjectId"] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString("D")),
                ["cycleCount"] = JsonSerializer.SerializeToElement(21),
            },
        };

        UserFriendlyException error = await Assert.ThrowsAsync<UserFriendlyException>(
            () => service.OperatorSnippetAsync(input));

        Assert.Contains("必须在 1 到 20 之间", error.Message);
    }

    private sealed class StubOperatorRegistry : IOperatorRegistry
    {
        public OperatorDescriptor Descriptor { get; } = new()
        {
            Id = Guid.Parse("5dc3be84-a32e-4924-91dc-ed0563167539"),
            Category = "数据读取",
            DisplayName = "在线扫描",
            TypeFullName = "Tests.read_online_point_cloud",
        };

        private OperatorParametersDescriptor Parameters => new()
        {
            OperatorId = Descriptor.Id,
            Inputs = [],
            Outputs = [],
            Config =
            [
                new ConfigParameterDescriptor
                {
                    Name = "calibProjectId",
                    ParameterTypeName = typeof(string).FullName!,
                    Required = true,
                    ControlType = (PortControlType)11,
                },
                new ConfigParameterDescriptor
                {
                    Name = "cycleCount",
                    ParameterTypeName = typeof(int).FullName!,
                    DefaultValue = 1,
                    ValueLimit = new[] { 1, 20 },
                    Required = true,
                    ControlType = PortControlType.Number,
                },
            ],
        };

        public Task<IReadOnlyList<OperatorDescriptor>> GetAllOperatorsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OperatorDescriptor>>([Descriptor]);

        public Task<OperatorParametersDescriptor?> GetParametersAsync(
            Guid operatorId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<OperatorParametersDescriptor?>(
                operatorId == Descriptor.Id ? Parameters : null);

        public Task<Type?> GetOperatorTypeAsync(
            Guid operatorId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Type?>(null);
    }
}
