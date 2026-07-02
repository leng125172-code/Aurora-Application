using System.ComponentModel.DataAnnotations;

namespace AuroraStruct3D.Variables.Dtos;

/// <summary>
/// Optional control-flow edge used by offline Def-Use analysis.
/// </summary>
public class VariableControlFlowEdgeDto
{
    /// <summary>
    /// Source node identifier.
    /// </summary>
    [MaxLength(256)]
    public string FromNodeId { get; set; } = string.Empty;

    /// <summary>
    /// Target node identifier.
    /// </summary>
    [MaxLength(256)]
    public string ToNodeId { get; set; } = string.Empty;
}
