using AuroraStruct3D.Variables;
using AuroraStruct3D.Variables.Dtos;
using Xunit;

namespace AuroraStruct3D.Application.Tests.Variables;

public class OfflineVariableDiagnosticPipelineTests
{
    [Fact]
    public void DeduplicateDiagnostics_Should_Remove_Duplicates()
    {
        VariableDiagnosticDto first = new()
        {
            Severity = VariableDiagnosticSeverity.Error,
            Code = AuroraStruct3DDomainErrorCodes.VariableUninitializedRead,
            Message = "duplicate",
            VariableName = "v1",
            Location = "n1",
        };

        VariableDiagnosticDto second = new()
        {
            Severity = VariableDiagnosticSeverity.Error,
            Code = AuroraStruct3DDomainErrorCodes.VariableUninitializedRead,
            Message = "duplicate",
            VariableName = "v1",
            Location = "n1",
        };

        List<VariableDiagnosticDto> result =
            OfflineVariableDiagnosticPipeline.DeduplicateDiagnostics(
                new List<VariableDiagnosticDto> { first, second }
            );

        Assert.Single(result);
    }

    [Fact]
    public void ApplySuppression_Should_Filter_Codes_Case_Insensitive()
    {
        List<VariableDiagnosticDto> diagnostics =
        [
            new VariableDiagnosticDto
            {
                Severity = VariableDiagnosticSeverity.Error,
                Code = AuroraStruct3DDomainErrorCodes.VariableUninitializedRead,
                Message = "keep?",
            },
            new VariableDiagnosticDto
            {
                Severity = VariableDiagnosticSeverity.Error,
                Code = AuroraStruct3DDomainErrorCodes.VariableTypeMismatch,
                Message = "stay",
            },
        ];

        List<VariableDiagnosticDto> result = OfflineVariableDiagnosticPipeline.ApplySuppression(
            diagnostics,
            new List<string> { "var1006" }
        );

        Assert.Single(result);
        Assert.Equal(AuroraStruct3DDomainErrorCodes.VariableTypeMismatch, result[0].Code);
    }

    [Fact]
    public void CreateAnalysisModeDiagnostic_Should_Emit_Info_Code()
    {
        VariableDiagnosticDto diagnostic =
            OfflineVariableDiagnosticPipeline.CreateAnalysisModeDiagnostic(
                VariableDefUseAnalysisMode.Strict
            );

        Assert.Equal(VariableDiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Equal("VARI0001", diagnostic.Code);
        Assert.Contains("Strict", diagnostic.Message, StringComparison.Ordinal);
    }
}
