using AuroraStruct3D.Variables;
using AuroraStruct3D.Variables.Dtos;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Variables;

public class OfflineVariableDefUseAnalyzerTests
{
    [Fact]
    public void Disabled_Mode_Should_Not_Report_DefUse_Diagnostics()
    {
        Guid workflowId = Guid.NewGuid();
        VariableCompileRequestDto input = CreateBaseInput(
            workflowId,
            VariableDefUseAnalysisMode.Disabled
        );
        input.Reads.Add(
            new VariableReferenceDto
            {
                OwnerWorkflowId = workflowId,
                VariableName = "temperature",
                Sequence = 1,
            }
        );

        Dictionary<string, VariableDeclarationDto> declarationMap = CreateRequiredDeclarationMap();

        List<VariableDiagnosticDto> diagnostics =
            OfflineVariableDefUseAnalyzer.AnalyzePotentialUninitializedReads(input, declarationMap);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Conservative_Mode_Should_Report_Read_Before_Write()
    {
        Guid workflowId = Guid.NewGuid();
        VariableCompileRequestDto input = CreateBaseInput(
            workflowId,
            VariableDefUseAnalysisMode.Conservative
        );
        input.Reads.Add(
            new VariableReferenceDto
            {
                OwnerWorkflowId = workflowId,
                VariableName = "temperature",
                Sequence = 1,
                Location = "node-1",
            }
        );
        input.Writes.Add(
            new VariableReferenceDto
            {
                OwnerWorkflowId = workflowId,
                VariableName = "temperature",
                Sequence = 2,
                Location = "node-2",
            }
        );
        input.ControlFlowEdges.AddRange([
            new VariableControlFlowEdgeDto { FromNodeId = "start", ToNodeId = "node-1" },
            new VariableControlFlowEdgeDto { FromNodeId = "node-1", ToNodeId = "node-2" },
        ]);
        input.EntryNodeId = "start";

        Dictionary<string, VariableDeclarationDto> declarationMap = CreateRequiredDeclarationMap();

        List<VariableDiagnosticDto> diagnostics =
            OfflineVariableDefUseAnalyzer.AnalyzePotentialUninitializedReads(input, declarationMap);

        Assert.Contains(
            diagnostics,
            x => x.Code == AuroraStruct3DDomainErrorCodes.VariableUninitializedRead
        );
    }

    [Fact]
    public void Strict_Mode_Should_Report_Unresolved_Read_Order()
    {
        Guid workflowId = Guid.NewGuid();
        VariableCompileRequestDto input = CreateBaseInput(
            workflowId,
            VariableDefUseAnalysisMode.Strict
        );
        input.Reads.Add(
            new VariableReferenceDto
            {
                OwnerWorkflowId = workflowId,
                VariableName = "temperature",
                Location = "read-node",
            }
        );
        input.Writes.Add(
            new VariableReferenceDto
            {
                OwnerWorkflowId = workflowId,
                VariableName = "temperature",
                Sequence = 2,
                Location = "write-node",
            }
        );
        input.ControlFlowEdges.AddRange([
            new VariableControlFlowEdgeDto { FromNodeId = "start", ToNodeId = "read-node" },
            new VariableControlFlowEdgeDto { FromNodeId = "read-node", ToNodeId = "write-node" },
        ]);
        input.EntryNodeId = "start";

        Dictionary<string, VariableDeclarationDto> declarationMap = CreateRequiredDeclarationMap();

        List<VariableDiagnosticDto> diagnostics =
            OfflineVariableDefUseAnalyzer.AnalyzePotentialUninitializedReads(input, declarationMap);

        Assert.Contains(
            diagnostics,
            x => x.Message.Contains("无法确定读取顺序", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Strict_Mode_Should_Report_Unresolved_Write_Order_Risk()
    {
        Guid workflowId = Guid.NewGuid();
        VariableCompileRequestDto input = CreateBaseInput(
            workflowId,
            VariableDefUseAnalysisMode.Strict
        );
        input.Reads.Add(
            new VariableReferenceDto
            {
                OwnerWorkflowId = workflowId,
                VariableName = "temperature",
                Sequence = 11,
                Location = "read-node",
            }
        );
        input.Writes.Add(
            new VariableReferenceDto
            {
                OwnerWorkflowId = workflowId,
                VariableName = "temperature",
                Sequence = 10,
                Location = "write-node",
            }
        );
        input.Writes.Add(
            new VariableReferenceDto
            {
                OwnerWorkflowId = workflowId,
                VariableName = "temperature",
                Location = "write-unknown",
            }
        );
        input.ControlFlowEdges.AddRange([
            new VariableControlFlowEdgeDto { FromNodeId = "start", ToNodeId = "write-node" },
            new VariableControlFlowEdgeDto { FromNodeId = "write-node", ToNodeId = "read-node" },
            new VariableControlFlowEdgeDto { FromNodeId = "start", ToNodeId = "write-unknown" },
        ]);
        input.EntryNodeId = "start";

        Dictionary<string, VariableDeclarationDto> declarationMap = CreateRequiredDeclarationMap();

        List<VariableDiagnosticDto> diagnostics =
            OfflineVariableDefUseAnalyzer.AnalyzePotentialUninitializedReads(input, declarationMap);

        Assert.Contains(
            diagnostics,
            x => x.Message.Contains("无法排序的写入位置", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void Conservative_Mode_Should_Report_At_Merge_When_One_Path_Misses_Write()
    {
        Guid workflowId = Guid.NewGuid();
        VariableCompileRequestDto input = CreateBaseInput(
            workflowId,
            VariableDefUseAnalysisMode.Conservative
        );
        input.Writes.Add(
            new VariableReferenceDto
            {
                OwnerWorkflowId = workflowId,
                VariableName = "temperature",
                Location = "then-write",
                Sequence = 2,
            }
        );
        input.Reads.Add(
            new VariableReferenceDto
            {
                OwnerWorkflowId = workflowId,
                VariableName = "temperature",
                Location = "merge-read",
                Sequence = 3,
            }
        );

        input.ControlFlowEdges.AddRange([
            new VariableControlFlowEdgeDto { FromNodeId = "start", ToNodeId = "then-write" },
            new VariableControlFlowEdgeDto { FromNodeId = "start", ToNodeId = "else-pass" },
            new VariableControlFlowEdgeDto { FromNodeId = "then-write", ToNodeId = "merge-read" },
            new VariableControlFlowEdgeDto { FromNodeId = "else-pass", ToNodeId = "merge-read" },
        ]);
        input.EntryNodeId = "start";

        Dictionary<string, VariableDeclarationDto> declarationMap = CreateRequiredDeclarationMap();

        List<VariableDiagnosticDto> diagnostics =
            OfflineVariableDefUseAnalyzer.AnalyzePotentialUninitializedReads(input, declarationMap);

        Assert.Contains(
            diagnostics,
            x => x.Code == AuroraStruct3DDomainErrorCodes.VariableUninitializedRead
        );
    }

    [Fact]
    public void Conservative_Mode_Should_Not_Report_When_Write_Dominates_Read()
    {
        Guid workflowId = Guid.NewGuid();
        VariableCompileRequestDto input = CreateBaseInput(
            workflowId,
            VariableDefUseAnalysisMode.Conservative
        );
        input.Writes.Add(
            new VariableReferenceDto
            {
                OwnerWorkflowId = workflowId,
                VariableName = "temperature",
                Location = "write",
                Sequence = 2,
            }
        );
        input.Reads.Add(
            new VariableReferenceDto
            {
                OwnerWorkflowId = workflowId,
                VariableName = "temperature",
                Location = "read",
                Sequence = 3,
            }
        );

        input.ControlFlowEdges.AddRange([
            new VariableControlFlowEdgeDto { FromNodeId = "start", ToNodeId = "write" },
            new VariableControlFlowEdgeDto { FromNodeId = "write", ToNodeId = "read" },
        ]);
        input.EntryNodeId = "start";

        Dictionary<string, VariableDeclarationDto> declarationMap = CreateRequiredDeclarationMap();

        List<VariableDiagnosticDto> diagnostics =
            OfflineVariableDefUseAnalyzer.AnalyzePotentialUninitializedReads(input, declarationMap);

        Assert.DoesNotContain(
            diagnostics,
            x => x.Code == AuroraStruct3DDomainErrorCodes.VariableUninitializedRead
        );
    }

    [Fact]
    public void Conservative_Mode_Should_Throw_When_ControlFlow_Is_Missing()
    {
        Guid workflowId = Guid.NewGuid();
        VariableCompileRequestDto input = CreateBaseInput(
            workflowId,
            VariableDefUseAnalysisMode.Conservative
        );
        input.Reads.Add(
            new VariableReferenceDto
            {
                OwnerWorkflowId = workflowId,
                VariableName = "temperature",
                Sequence = 1,
                Location = "node-1",
            }
        );
        input.Writes.Add(
            new VariableReferenceDto
            {
                OwnerWorkflowId = workflowId,
                VariableName = "temperature",
                Sequence = 2,
                Location = "node-2",
            }
        );

        Dictionary<string, VariableDeclarationDto> declarationMap = CreateRequiredDeclarationMap();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
            OfflineVariableDefUseAnalyzer.AnalyzePotentialUninitializedReads(input, declarationMap)
        );

        Assert.Contains("ControlFlowEdges", ex.Message, StringComparison.Ordinal);
    }

    private static VariableCompileRequestDto CreateBaseInput(
        Guid workflowId,
        VariableDefUseAnalysisMode mode
    )
    {
        return new VariableCompileRequestDto
        {
            ProjectId = Guid.NewGuid(),
            WorkflowId = workflowId,
            DefUseAnalysisMode = mode,
        };
    }

    private static Dictionary<string, VariableDeclarationDto> CreateRequiredDeclarationMap()
    {
        return new Dictionary<string, VariableDeclarationDto>(StringComparer.Ordinal)
        {
            ["temperature"] = new VariableDeclarationDto
            {
                Name = "temperature",
                TypeName = "double",
                IsRequiredInit = true,
                DefaultValueJson = null,
                Visibility = VariableVisibility.Private,
                Mutability = VariableMutability.Mutable,
            },
        };
    }
}
