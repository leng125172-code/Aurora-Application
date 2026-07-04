namespace AuroraStruct3D.OpenCV.Workflow.Compilation;

/// <summary>
/// Workflow static validation options.
/// </summary>
public sealed class WorkflowValidationOptions
{
    /// <summary>
    /// MOD: When true, duplicate variable definitions are treated as errors unless shadow is explicitly allowed.
    /// </summary>
    public bool StrictVariableDefinition { get; set; }
}
