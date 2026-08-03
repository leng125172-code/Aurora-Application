using AuroraStruct3D.OpenCV.Registry;
using AuroraStruct3D.OpenCV.VisionParameters;
using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;
using AuroraStruct3D.OpenCV.Workflow.Values;
using AuroraStruct3D.Workflow;
using AuroraStruct3D.Workflow.Runtime;
using OpenCvSharp;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Workflow;

public class WorkflowVariableCompileRequestFactoryTests
{
    [Theory]
    [InlineData("System.Double")]
    [InlineData("System.Boolean")]
    [InlineData("System.String")]
    public void IsCompatible_Should_Allow_Any_Runtime_Type_For_Object(string runtimeType)
    {
        Assert.True(
            WorkflowExecutionTypeNormalizer.IsCompatible("System.Object", runtimeType)
        );
    }

    [Fact]
    public void NormalizeDeclaredType_Should_Shorten_Generic_Clr_Type_Name()
    {
        string fullName = typeof(List<Mat>).FullName!;

        string normalized = WorkflowExecutionTypeNormalizer.NormalizeDeclaredType(fullName);

        Assert.Equal("System.Collections.Generic.List<OpenCvSharp.Mat>", normalized);
        Assert.True(normalized.Length <= 128);
    }

    [Fact]
    public void NormalizeRuntimeValueType_Should_Preserve_ListOfMat_Type()
    {
        using Mat first = new(2, 2, MatType.CV_8UC1);
        using Mat second = new(2, 2, MatType.CV_8UC1);
        List<Mat> masks = [first, second];

        string normalized = WorkflowExecutionTypeNormalizer.NormalizeRuntimeValueType(masks);

        Assert.Equal("System.Collections.Generic.List<OpenCvSharp.Mat>", normalized);
        Assert.True(
            WorkflowExecutionTypeNormalizer.IsCompatible(
                typeof(List<Mat>).FullName!,
                normalized
            )
        );
    }

    [Fact]
    public async Task BuildAsync_Should_Declare_PointCloud_Output_As_PointCloudData()
    {
        Guid operatorId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
        var pointCloudPort = new PointCloudData
        {
            ParameterName = "output_point_cloud",
            DisplayName = "输出点云",
        };
        var registry = new StubOperatorRegistry(
            new OperatorParametersDescriptor
            {
                OperatorId = operatorId,
                Inputs =
                [
                    new ParameterDescriptor
                    {
                        ParameterName = "point_cloud_path",
                        DisplayName = "点云路径",
                        ParameterTypeName = typeof(string).FullName!,
                        ControlType = PortControlType.Variable,
                        MatType = PortMatType.Generic,
                    },
                ],
                Outputs =
                [
                    new ParameterDescriptor
                    {
                        ParameterName = pointCloudPort.ParameterName,
                        DisplayName = pointCloudPort.DisplayName,
                        ParameterTypeName =
                            pointCloudPort.ParameterType.FullName
                            ?? pointCloudPort.ParameterType.Name,
                        ControlType = pointCloudPort.ControlType,
                        MatType = PortMatType.PointCloud3D,
                    },
                ],
            }
        );
        var factory = new WorkflowVariableCompileRequestFactory(registry);

        GraphDataModel graph = new()
        {
            Nodes =
            [
                new NodeModel { Id = "start", Type = "start-node" },
                new NodeModel
                {
                    Id = "node-1",
                    Type = operatorId.ToString(),
                    Properties = new NodePropertiesModel
                    {
                        InputBindings = new Dictionary<string, string>
                        {
                            ["point_cloud_path"] = "demo.ply",
                        },
                        InputBindingSources = new Dictionary<string, string>
                        {
                            ["point_cloud_path"] = "literal",
                        },
                        OutputBindings = new Dictionary<string, string>
                        {
                            ["output_point_cloud"] = "cloud",
                        },
                        OutputBindingSources = new Dictionary<string, string>
                        {
                            ["output_point_cloud"] = "variable",
                        },
                    },
                },
            ],
            Edges =
            [
                new EdgeModel
                {
                    Id = "edge-1",
                    SourceNodeId = "start",
                    TargetNodeId = "node-1",
                },
            ],
        };

        var request = await factory.BuildAsync(Guid.NewGuid(), Guid.NewGuid(), graph);

        var declaration = Assert.Single(request.Declarations);
        Assert.Equal("cloud", declaration.Name);
        Assert.Equal(WorkflowValueTypes.PointCloud, declaration.TypeName);
    }

    private sealed class StubOperatorRegistry : IOperatorRegistry
    {
        private readonly OperatorParametersDescriptor _parameters;

        public StubOperatorRegistry(OperatorParametersDescriptor parameters)
        {
            _parameters = parameters;
        }

        public Task<IReadOnlyList<OperatorDescriptor>> GetAllOperatorsAsync(
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyList<OperatorDescriptor>>([]);

        public Task<OperatorParametersDescriptor?> GetParametersAsync(
            Guid operatorId,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(operatorId == _parameters.OperatorId ? _parameters : null);

        public Task<Type?> GetOperatorTypeAsync(
            Guid operatorId,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<Type?>(null);
    }
}
