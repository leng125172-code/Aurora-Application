using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Plcs;

public class PlcOperationLog : CreationAuditedEntity<Guid>
{
    public Guid PlcDeviceId { get; private set; }
    public Guid? PlcTagId { get; private set; }
    public PlcOperationType OperationType { get; private set; }
    public bool IsSuccess { get; private set; }
    public string? Summary { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTime OccurredAt { get; private set; }

    protected PlcOperationLog() { }

    public PlcOperationLog(
        Guid id,
        Guid plcDeviceId,
        Guid? plcTagId,
        PlcOperationType operationType,
        bool isSuccess,
        string? summary,
        string? errorMessage
    ) : base(id)
    {
        PlcDeviceId = plcDeviceId;
        PlcTagId = plcTagId;
        OperationType = operationType;
        IsSuccess = isSuccess;
        Summary = summary;
        ErrorMessage = errorMessage;
        OccurredAt = DateTime.UtcNow;
    }
}
