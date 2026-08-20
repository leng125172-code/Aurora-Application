using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Workflow;

public sealed class WorkflowMigrationSnapshot : CreationAuditedEntity<Guid>
{
    public Guid BatchId { get; private set; }
    public Guid WorkflowId { get; private set; }
    public string WorkflowName { get; private set; } = string.Empty;
    public string? SourceCode { get; private set; }
    public string GraphData { get; private set; } = string.Empty;
    public int LanguageVersion { get; private set; }
    public int SourceRevision { get; private set; }
    public string? SourceHash { get; private set; }
    public string? SemanticHash { get; private set; }
    public string? ProgramHash { get; private set; }
    public string? OperatorContractHash { get; private set; }
    public DateTime? RolledBackAt { get; private set; }

    private WorkflowMigrationSnapshot() { }

    public WorkflowMigrationSnapshot(Guid id, Guid batchId, WorkflowDefinition workflow) : base(id)
    {
        BatchId = batchId;
        WorkflowId = workflow.Id;
        WorkflowName = workflow.Name;
        SourceCode = workflow.SourceCode;
        GraphData = workflow.GraphData;
        LanguageVersion = workflow.LanguageVersion;
        SourceRevision = workflow.SourceRevision;
        SourceHash = workflow.SourceHash;
        SemanticHash = workflow.SemanticHash;
        ProgramHash = workflow.ProgramHash;
        OperatorContractHash = workflow.OperatorContractHash;
    }

    public void MarkRolledBack() => RolledBackAt = DateTime.UtcNow;
}
