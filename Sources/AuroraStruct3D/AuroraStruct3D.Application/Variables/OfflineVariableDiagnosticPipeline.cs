using System.Collections.Immutable;
using AuroraStruct3D.Variables.Dtos;

namespace AuroraStruct3D.Variables;

/// <summary>
/// Utility methods for post-processing offline variable diagnostics.
/// </summary>
public static class OfflineVariableDiagnosticPipeline
{
    /// <summary>
    /// Remove duplicate diagnostics using a stable composite key.
    /// </summary>
    public static List<VariableDiagnosticDto> DeduplicateDiagnostics(
        List<VariableDiagnosticDto> diagnostics
    )
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        List<VariableDiagnosticDto> result = new();

        foreach (VariableDiagnosticDto diagnostic in diagnostics)
        {
            string key = string.Join(
                "|",
                diagnostic.Severity,
                diagnostic.Code ?? string.Empty,
                diagnostic.OwnerWorkflowId?.ToString() ?? string.Empty,
                diagnostic.VariableName ?? string.Empty,
                diagnostic.Location ?? string.Empty,
                diagnostic.Message ?? string.Empty
            );

            if (!seen.Add(key))
            {
                continue;
            }

            result.Add(diagnostic);
        }

        return result;
    }

    /// <summary>
    /// Remove diagnostics whose code is in the suppression list.
    /// </summary>
    public static List<VariableDiagnosticDto> ApplySuppression(
        List<VariableDiagnosticDto> diagnostics,
        List<string> suppressedCodes
    )
    {
        if (suppressedCodes.Count == 0)
        {
            return diagnostics;
        }

        ImmutableHashSet<string> suppressed = suppressedCodes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);

        if (suppressed.Count == 0)
        {
            return diagnostics;
        }

        return diagnostics
            .Where(x => string.IsNullOrWhiteSpace(x.Code) || !suppressed.Contains(x.Code))
            .ToList();
    }

    /// <summary>
    /// Emit an informational diagnostic describing the selected Def-Use analysis mode.
    /// </summary>
    public static VariableDiagnosticDto CreateAnalysisModeDiagnostic(
        VariableDefUseAnalysisMode mode
    )
    {
        return new VariableDiagnosticDto
        {
            Severity = VariableDiagnosticSeverity.Info,
            Code = "VARI0001",
            Message = $"Def-Use analysis mode: {mode}.",
            OwnerWorkflowId = null,
            VariableName = null,
            Location = null,
        };
    }
}
