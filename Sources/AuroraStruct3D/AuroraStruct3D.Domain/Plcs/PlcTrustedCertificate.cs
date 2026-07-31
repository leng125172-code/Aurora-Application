using Volo.Abp.Domain.Entities.Auditing;

namespace AuroraStruct3D.Plcs;

public class PlcTrustedCertificate : CreationAuditedEntity<Guid>
{
    public Guid PlcDeviceId { get; private set; }
    public string Thumbprint { get; private set; } = string.Empty;
    public string Subject { get; private set; } = string.Empty;
    public DateTime NotBefore { get; private set; }
    public DateTime NotAfter { get; private set; }

    protected PlcTrustedCertificate() { }

    public PlcTrustedCertificate(
        Guid id,
        Guid plcDeviceId,
        string thumbprint,
        string subject,
        DateTime notBefore,
        DateTime notAfter
    ) : base(id)
    {
        PlcDeviceId = plcDeviceId;
        Thumbprint = thumbprint;
        Subject = subject;
        NotBefore = notBefore;
        NotAfter = notAfter;
    }
}
