using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Workflow;

public sealed class WorkflowSourceDraft : CreationAuditedAggregateRoot<Guid>
{
    public Guid WorkflowId { get; private set; }
    public Guid? UserId { get; private set; }
    public int BaseRevision { get; private set; }
    public string SourceCode { get; private set; } = string.Empty;
    public string GraphData { get; private set; } = "{}";
    public string ContentHash { get; private set; } = string.Empty;

    protected WorkflowSourceDraft() { }

    public WorkflowSourceDraft(
        Guid id, Guid workflowId, Guid? userId, int baseRevision,
        string sourceCode, string graphData, string contentHash
    ) : base(id) => Update(workflowId, userId, baseRevision, sourceCode, graphData, contentHash);

    public void Update(
        Guid workflowId, Guid? userId, int baseRevision,
        string sourceCode, string graphData, string contentHash
    )
    {
        WorkflowId = workflowId;
        UserId = userId;
        BaseRevision = baseRevision;
        SourceCode = sourceCode;
        GraphData = graphData;
        ContentHash = contentHash;
    }
}

public sealed class WorkflowSourceVersion : CreationAuditedAggregateRoot<Guid>
{
    public Guid WorkflowId { get; private set; }
    public int Revision { get; private set; }
    public string SourceCode { get; private set; } = string.Empty;
    public string GraphData { get; private set; } = "{}";
    public string ContentHash { get; private set; } = string.Empty;
    public string SemanticHash { get; private set; } = string.Empty;
    public string ProgramHash { get; private set; } = string.Empty;
    public bool IsPublished { get; private set; }

    protected WorkflowSourceVersion() { }

    public WorkflowSourceVersion(
        Guid id, Guid workflowId, int revision, string sourceCode, string graphData,
        string contentHash, string semanticHash, string programHash, bool isPublished = false
    ) : base(id)
    {
        WorkflowId = workflowId;
        Revision = revision;
        SourceCode = sourceCode;
        GraphData = graphData;
        ContentHash = contentHash;
        SemanticHash = semanticHash;
        ProgramHash = programHash;
        IsPublished = isPublished;
    }

    public void Publish() => IsPublished = true;
}
