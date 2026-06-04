namespace AuroraStruct3D.AI;

/// <summary>
/// AI 模型 Python 转换执行器。
/// </summary>
public interface IAiModelPythonConversionExecutor
{
    /// <summary>
    /// 调用 Python 进程执行模型转换。
    /// </summary>
    Task<AiModelPythonConversionResult> ExecuteAsync(
        AiModelPythonConversionRequest request,
        CancellationToken cancellationToken = default
    );
}
