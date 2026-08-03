using AuroraStruct3D.DeviceState;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;

namespace AuroraStruct3D.DeviceState;

/// <summary>将设备运行错误写入故障历史，不干预整机状态机。</summary>
public sealed class DeviceFaultReporter : IDeviceFaultReporter, ITransientDependency
{
    private readonly IDeviceFaultRepository _repository;
    private readonly IGuidGenerator _guidGenerator;

    public DeviceFaultReporter(IDeviceFaultRepository repository, IGuidGenerator guidGenerator)
    {
        _repository = repository;
        _guidGenerator = guidGenerator;
    }

    public async Task ReportAsync(DeviceFaultReport report, CancellationToken cancellationToken = default)
    {
        DeviceFault? existing = await _repository.FindUnresolvedByFingerprintAsync(report.Fingerprint, cancellationToken);
        if (existing is not null)
        {
            existing.Reoccur(report.FaultMessage);
            await _repository.UpdateAsync(existing, autoSave: true, cancellationToken);
            return;
        }

        DeviceFault fault = new(
            _guidGenerator.Create(), report.FaultLevel, report.FaultCode, report.FaultMessage);
        fault.SetRuntimeContext(report);
        await _repository.InsertAsync(fault, autoSave: true, cancellationToken);
    }

    public async Task RecoverAsync(string fingerprint, string? reason = null, CancellationToken cancellationToken = default)
    {
        DeviceFault? fault = await _repository.FindUnresolvedByFingerprintAsync(fingerprint, cancellationToken);
        if (fault is null)
            return;

        fault.MarkAutoRecovered(reason ?? "后续操作执行成功");
        await _repository.UpdateAsync(fault, autoSave: true, cancellationToken);
    }
}
