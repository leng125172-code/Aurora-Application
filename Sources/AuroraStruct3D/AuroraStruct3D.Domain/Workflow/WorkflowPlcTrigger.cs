using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Workflow;

/// <summary>PLC point value mapping that starts a project's active workflow deployment.</summary>
public class WorkflowPlcTrigger : FullAuditedAggregateRoot<Guid>
{
    public Guid ProjectId { get; private set; }
    public Guid PlcDeviceId { get; private set; }
    public Guid PlcTagId { get; private set; }
    public string ExpectedValueJson { get; private set; } = string.Empty;
    public double Tolerance { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTime? LastTriggeredAt { get; private set; }
    public string? LastSkipReason { get; private set; }
    protected WorkflowPlcTrigger() { }
    public WorkflowPlcTrigger(Guid id, Guid projectId, Guid plcDeviceId, Guid plcTagId, string expectedValueJson, double tolerance, bool isEnabled) : base(id)
    {
        if (projectId == Guid.Empty || plcDeviceId == Guid.Empty || plcTagId == Guid.Empty)
            throw new BusinessException("Workflow.PlcTrigger.Identity.Empty");
        ProjectId = projectId; PlcDeviceId = plcDeviceId; PlcTagId = plcTagId;
        Update(expectedValueJson, tolerance, isEnabled);
    }
    public void Update(string expectedValueJson, double tolerance, bool isEnabled)
    {
        ExpectedValueJson = Check.NotNullOrWhiteSpace(expectedValueJson, nameof(expectedValueJson), 2048);
        if (tolerance < 0) throw new BusinessException("Workflow.PlcTrigger.Tolerance.Invalid");
        Tolerance = tolerance; IsEnabled = isEnabled;
    }
    public void MarkTriggered() { LastTriggeredAt = DateTime.UtcNow; LastSkipReason = null; }
    public void MarkSkipped(string reason) => LastSkipReason = reason.Length <= 1024 ? reason : reason[..1024];
}
