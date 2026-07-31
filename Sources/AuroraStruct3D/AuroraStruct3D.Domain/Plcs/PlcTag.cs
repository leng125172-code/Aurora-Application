using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp;

namespace AuroraStruct3D.Plcs;

public class PlcTag : FullAuditedAggregateRoot<Guid>
{
    public Guid PlcDeviceId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Address { get; private set; } = string.Empty;
    public PlcTagDataType DataType { get; private set; }
    public PlcTagAccess Access { get; private set; }
    public bool IsEnabled { get; private set; } = true;
    public int SamplingIntervalMs { get; private set; } = PlcConsts.DefaultSamplingIntervalMs;
    public double? Deadband { get; private set; }
    public double Scale { get; private set; } = 1d;
    public double Offset { get; private set; }
    public string? Unit { get; private set; }
    public string? DisplayFormat { get; private set; }
    public double? Minimum { get; private set; }
    public double? Maximum { get; private set; }

    protected PlcTag() { }

    public PlcTag(
        Guid id,
        Guid plcDeviceId,
        string code,
        string name,
        string address,
        PlcTagDataType dataType,
        PlcTagAccess access
    ) : base(id)
    {
        PlcDeviceId = plcDeviceId;
        Update(code, name, address, dataType, access);
    }

    public void Update(
        string code,
        string name,
        string address,
        PlcTagDataType dataType,
        PlcTagAccess access
    )
    {
        Code = Check.NotNullOrWhiteSpace(code, nameof(code), PlcConsts.MaxCodeLength);
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), PlcConsts.MaxNameLength);
        Address = Check.NotNullOrWhiteSpace(address, nameof(address), PlcConsts.MaxAddressLength);
        DataType = dataType;
        Access = access;
    }

    public void ConfigureEngineering(
        int samplingIntervalMs,
        double? deadband,
        double scale,
        double offset,
        string? unit,
        string? displayFormat,
        double? minimum,
        double? maximum
    )
    {
        if (samplingIntervalMs < 50)
            throw new ArgumentOutOfRangeException(nameof(samplingIntervalMs));
        if (!double.IsFinite(scale) || scale == 0 || !double.IsFinite(offset))
            throw new ArgumentOutOfRangeException(nameof(scale));
        if (deadband is < 0 || (minimum.HasValue && maximum.HasValue && minimum > maximum))
            throw new ArgumentOutOfRangeException(nameof(deadband));
        SamplingIntervalMs = samplingIntervalMs;
        Deadband = deadband;
        Scale = scale;
        Offset = offset;
        Unit = unit;
        DisplayFormat = displayFormat;
        Minimum = minimum;
        Maximum = maximum;
    }

    public void SetEnabled(bool enabled) => IsEnabled = enabled;
}
