using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Workflow;

public sealed class WorkflowMigrationBatch : CreationAuditedAggregateRoot<Guid>
{
    public WorkflowMigrationBatchStatus Status { get; private set; }
    public int ScannedCount { get; private set; }
    public int MigratedCount { get; private set; }
    public int FailedCount { get; private set; }
    public int RetryCount { get; private set; }
    public string ResultsJson { get; private set; } = "[]";
    public DateTime? CompletedAt { get; private set; }

    private WorkflowMigrationBatch() { }

    public WorkflowMigrationBatch(Guid id) : base(id) { }

    public void Start()
    {
        Status = WorkflowMigrationBatchStatus.Running;
        CompletedAt = null;
    }

    public void Complete(int scanned, int migrated, int failed, string resultsJson)
    {
        ScannedCount = scanned;
        MigratedCount = migrated;
        FailedCount = failed;
        ResultsJson = resultsJson;
        Status = failed == 0
            ? WorkflowMigrationBatchStatus.Completed
            : WorkflowMigrationBatchStatus.CompletedWithErrors;
        CompletedAt = DateTime.UtcNow;
    }

    public void IncrementRetry() => RetryCount++;
}
