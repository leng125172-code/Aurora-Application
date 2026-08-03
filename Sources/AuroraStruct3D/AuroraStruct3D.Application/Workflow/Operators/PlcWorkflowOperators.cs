using System.Text.Json;
using System.ComponentModel;
using System.Runtime.InteropServices;
using AuroraStruct3D.OpenCV;
using AuroraStruct3D.OpenCV.VisionParameters;
using AuroraStruct3D.OpenCV.Workflow;
using Microsoft.Extensions.DependencyInjection;
using AuroraStruct3D.Plcs;
using Volo.Abp.Uow;

namespace AuroraStruct3D.Workflow.Operators;

internal static class PlcOperatorSupport
{
    public static async Task<T> ExecuteAsync<T>(
        Func<IPlcWorkflowTagAccessor, Task<T>> action)
    {
        IServiceProvider serviceProvider = WorkflowServiceProviderAmbient.Current;
        IUnitOfWorkManager unitOfWorkManager =
            serviceProvider.GetRequiredService<IUnitOfWorkManager>();
        using IUnitOfWork unitOfWork = unitOfWorkManager.Begin(
            requiresNew: true,
            isTransactional: false
        );
        IPlcWorkflowTagAccessor accessor =
            serviceProvider.GetRequiredService<IPlcWorkflowTagAccessor>();
        T result = await action(accessor);
        await unitOfWork.CompleteAsync();
        return result;
    }
    public static Guid Device(string value) => Guid.TryParse(value, out Guid id) && id != Guid.Empty
        ? id : throw new InvalidOperationException("PLC 设备 ID 无效。");
    public static string RequiredString(IWorkflowContext context, string name) => context.Get<string>(name)
        ?? throw new InvalidOperationException($"PLC 算子输入 '{name}' 不能为空。");
    public static string Json(object? value) => JsonSerializer.Serialize(value);
}

[Guid("c7297012-ef3e-45c4-ae7b-2b8a2d454101")]
[Category("PLC")]
[DisplayName("PLC 单点读取")]
[Description("按业务点位代码读取 PLC 工程量值。")]
public sealed class plc_read : IOperator, IAsyncWorkflowOperator
{
    public static List<IVisionParameter>? InputVisionParameters => null;
    public static List<IVisionParameter>? OutputVisionParameters => new()
    {
        new VisionParameter<object> { ParameterName = "value", DisplayName = "工程量值", ParameterType = typeof(object) },
        new VisionParameter<string> { ParameterName = "quality", DisplayName = "质量", ParameterType = typeof(string) },
        new VisionParameter<string> { ParameterName = "error", DisplayName = "错误", ParameterType = typeof(string) },
    };
    public static List<IConfigParameter>? ConfigParameters => PlcConfig.SingleTag;
    private readonly string _deviceId; private readonly string _tagCode;
    public plc_read(string plcDeviceId, string tagCode) { _deviceId = plcDeviceId; _tagCode = tagCode; }
    public void Execute(IWorkflowContext context) => ExecuteAsync(context).GetAwaiter().GetResult();
    public async Task ExecuteAsync(IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        PlcTagValueDto value = (await PlcOperatorSupport.ExecuteAsync(accessor =>
            accessor.ReadByCodesAsync(PlcOperatorSupport.Device(_deviceId), [_tagCode], cancellationToken))).SingleOrDefault()
            ?? throw new InvalidOperationException($"PLC 点位不存在：{_tagCode}");
        if (!string.IsNullOrWhiteSpace(value.Error)) throw new InvalidOperationException($"[PLC_READ_FAILED] {value.Error}");
        context.Set("value", value.EngineeringValue); context.Set("quality", value.Quality); context.Set("error", value.Error);
    }
    public void Dispose() { }
}

[Guid("c7297012-ef3e-45c4-ae7b-2b8a2d454102")]
[Category("PLC")]
[DisplayName("PLC 单点写入")]
[Description("将工作流值写入 PLC 业务点位。")]
public sealed class plc_write : IOperator, IAsyncWorkflowOperator
{
    public static List<IVisionParameter>? InputVisionParameters => new() { new VisionParameter<object> { ParameterName = "value", DisplayName = "写入值", ParameterType = typeof(object) } };
    public static List<IVisionParameter>? OutputVisionParameters => new() { new VisionParameter<bool> { ParameterName = "success", DisplayName = "成功", ParameterType = typeof(bool) }, new VisionParameter<string> { ParameterName = "error", DisplayName = "错误", ParameterType = typeof(string) } };
    public static List<IConfigParameter>? ConfigParameters => PlcConfig.SingleTag;
    private readonly string _deviceId; private readonly string _tagCode;
    public plc_write(string plcDeviceId, string tagCode) { _deviceId = plcDeviceId; _tagCode = tagCode; }
    public void Execute(IWorkflowContext context) => ExecuteAsync(context).GetAwaiter().GetResult();
    public async Task ExecuteAsync(IWorkflowContext context, CancellationToken cancellationToken = default)
    {
        PlcWriteResultDto result = (await PlcOperatorSupport.ExecuteAsync(accessor =>
            accessor.WriteByCodesAsync(PlcOperatorSupport.Device(_deviceId), new Dictionary<string, object?> { [_tagCode] = context.Get("value") }, new WorkflowPlcOperationContext(null, Guid.Empty, null), cancellationToken))).SingleOrDefault()
            ?? throw new InvalidOperationException($"PLC 点位不存在：{_tagCode}");
        context.Set("success", result.Success); context.Set("error", result.Error);
        if (!result.Success) throw new InvalidOperationException($"[PLC_WRITE_FAILED] {result.Error ?? "PLC 写入失败。"}");
    }
    public void Dispose() { }
}

[Guid("c7297012-ef3e-45c4-ae7b-2b8a2d454103")]
[Category("PLC")]
[DisplayName("PLC 批量读取")]
public sealed class plc_batch_read : IOperator, IAsyncWorkflowOperator
{
    public static List<IVisionParameter>? InputVisionParameters => null;
    public static List<IVisionParameter>? OutputVisionParameters => new() { new VisionParameter<string> { ParameterName = "values_json", DisplayName = "读取结果 JSON", ParameterType = typeof(string) } };
    public static List<IConfigParameter>? ConfigParameters => PlcConfig.BatchTags;
    private readonly string _deviceId; private readonly string _tagCodesJson;
    public plc_batch_read(string plcDeviceId, string tagCodesJson) { _deviceId = plcDeviceId; _tagCodesJson = tagCodesJson; }
    public void Execute(IWorkflowContext context) => ExecuteAsync(context).GetAwaiter().GetResult();
    public async Task ExecuteAsync(IWorkflowContext context, CancellationToken cancellationToken = default)
    { var codes = JsonSerializer.Deserialize<List<string>>(_tagCodesJson) ?? []; var values = await PlcOperatorSupport.ExecuteAsync(accessor => accessor.ReadByCodesAsync(PlcOperatorSupport.Device(_deviceId), codes, cancellationToken)); if (values.Any(x => !string.IsNullOrWhiteSpace(x.Error))) throw new InvalidOperationException($"[PLC_READ_FAILED] {values.First(x => !string.IsNullOrWhiteSpace(x.Error)).Error}"); context.Set("values_json", PlcOperatorSupport.Json(values.ToDictionary(x => x.Code))); }
    public void Dispose() { }
}

[Guid("c7297012-ef3e-45c4-ae7b-2b8a2d454104")]
[Category("PLC")]
[DisplayName("PLC 批量写入")]
public sealed class plc_batch_write : IOperator, IAsyncWorkflowOperator
{
    public static List<IVisionParameter>? InputVisionParameters => new() { new VisionParameter<string> { ParameterName = "values_json", DisplayName = "写入值 JSON", ParameterType = typeof(string) } };
    public static List<IVisionParameter>? OutputVisionParameters => new() { new VisionParameter<string> { ParameterName = "results_json", DisplayName = "写入结果 JSON", ParameterType = typeof(string) } };
    public static List<IConfigParameter>? ConfigParameters => PlcConfig.DeviceOnly;
    private readonly string _deviceId;
    public plc_batch_write(string plcDeviceId) { _deviceId = plcDeviceId; }
    public void Execute(IWorkflowContext context) => ExecuteAsync(context).GetAwaiter().GetResult();
    public async Task ExecuteAsync(IWorkflowContext context, CancellationToken cancellationToken = default)
    { var values = JsonSerializer.Deserialize<Dictionary<string, object?>>(PlcOperatorSupport.RequiredString(context, "values_json")) ?? []; var results = await PlcOperatorSupport.ExecuteAsync(accessor => accessor.WriteByCodesAsync(PlcOperatorSupport.Device(_deviceId), values, new WorkflowPlcOperationContext(null, Guid.Empty, null), cancellationToken)); context.Set("results_json", PlcOperatorSupport.Json(results)); if (results.Any(x => !x.Success)) throw new InvalidOperationException($"[PLC_WRITE_FAILED] {results.First(x => !x.Success).Error ?? "PLC 批量写入存在失败项。"}"); }
    public void Dispose() { }
}

internal static class PlcConfig
{
    public static List<IConfigParameter> DeviceOnly => new() { new ConfigParameter { Name = "plcDeviceId", DisplayName = "PLC 设备 ID", ParameterType = typeof(string), Required = true } };
    public static List<IConfigParameter> SingleTag => new(DeviceOnly) { new ConfigParameter { Name = "tagCode", DisplayName = "点位代码", ParameterType = typeof(string), Required = true } };
    public static List<IConfigParameter> BatchTags => new(DeviceOnly) { new ConfigParameter { Name = "tagCodesJson", DisplayName = "点位代码 JSON", ParameterType = typeof(string), Required = true } };
}
