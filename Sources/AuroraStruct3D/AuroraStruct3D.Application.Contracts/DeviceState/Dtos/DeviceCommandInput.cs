using System.ComponentModel.DataAnnotations;

namespace AuroraStruct3D.DeviceState;

public class DeviceCommandInput
{
    [StringLength(512)]
    public string? Reason { get; set; }
}

public class EmergencyStopInput : DeviceCommandInput
{
    [Required, StringLength(512), MinLength(2)]
    public new string Reason { get; set; } = string.Empty;
}
