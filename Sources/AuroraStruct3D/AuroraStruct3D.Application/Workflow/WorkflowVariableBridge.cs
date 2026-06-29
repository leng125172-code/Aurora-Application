using AuroraStruct3D.OpenCV.Workflow.Values;
using AuroraStruct3D.VariableStage;
using AuroraStruct3D.VariableStage.Dtos;
using Volo.Abp.Content;
using Volo.Abp.DependencyInjection;

namespace AuroraStruct3D.Workflow;

/// <summary>
/// Redis 暂存 ↔ 内存执行上下文 的变量桥接。
/// <para>
/// 加载：从 <see cref="IVariableStageAppService"/> 取出暂存字节 + ValueType，反序列化为 CLR 值；
/// 保存：把执行后的 CLR 值序列化为字节，按 ValueType 写回暂存。
/// 复用现有 VariableStage 的 Redis Key 布局，不另起一套存储。
/// </para>
/// </summary>
public interface IWorkflowVariableBridge
{
    /// <summary>
    /// 按变量名（= 暂存 key）批量加载为初始变量字典，供 <c>WorkflowExecutor.Execute</c> 注入。
    /// </summary>
    Task<Dictionary<string, object?>> LoadAsync(IEnumerable<string> keys);

    /// <summary>
    /// 把一个变量值序列化并写回暂存，返回暂存 key（= 变量名）。
    /// </summary>
    Task<string> SaveAsync(string key, object? value);
}

/// <inheritdoc cref="IWorkflowVariableBridge"/>
public sealed class WorkflowVariableBridge : IWorkflowVariableBridge, ITransientDependency
{
    private readonly IVariableStageAppService _stage;

    public WorkflowVariableBridge(IVariableStageAppService stage)
    {
        _stage = stage;
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, object?>> LoadAsync(IEnumerable<string> keys)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (string key in keys.Distinct(StringComparer.Ordinal))
        {
            VariableValueDto dto = await _stage.GetAsync(key);
            byte[] bytes = Convert.FromBase64String(dto.Value);
            result[key] = WorkflowValueSerializer.Deserialize(bytes, dto.ValueType);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<string> SaveAsync(string key, object? value)
    {
        string valueType = WorkflowValueSerializer.InferValueType(value);
        byte[] bytes = WorkflowValueSerializer.Serialize(value, valueType);

        using var ms = new MemoryStream(bytes);
        var content = new RemoteStreamContent(ms, fileName: key, contentType: "application/octet-stream");
        await _stage.SetAsync(key, valueType, content);

        return key;
    }
}
