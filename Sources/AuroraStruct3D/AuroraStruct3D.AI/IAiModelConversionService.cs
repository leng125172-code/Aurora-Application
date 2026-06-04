namespace AuroraStruct3D.AI;

/// <summary>
/// AI 模型转换服务。
/// 负责分析 ONNX 模型组并调度实际转换执行器。
/// </summary>
public interface IAiModelConversionService
{
    /// <summary>
    /// 分析 ONNX 模型组，给出建议转换目标。
    /// </summary>
    Task<AiModelConversionAnalysisResult> AnalyzeOnnxModelGroupAsync(
        AiModel model,
        IReadOnlyList<AiModelFile> files,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// 调度模型转换任务。
    /// </summary>
    Task<AiModelConversionDispatchResult> QueueConversionAsync(
        AiModel model,
        IReadOnlyList<AiModelFile> files,
        AiModelResolvedConversionType targetType,
        CancellationToken cancellationToken = default
    );
}
