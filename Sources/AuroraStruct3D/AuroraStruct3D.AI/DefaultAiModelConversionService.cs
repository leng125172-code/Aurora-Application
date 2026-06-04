using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Volo.Abp.BlobStoring;

namespace AuroraStruct3D.AI;

/// <summary>
/// 默认 AI 模型转换服务。
/// 当前阶段仅提供规则占位与未接入提示，后续由 OnnxRuntime 分析器和 Python 转换器替换。
/// </summary>
public sealed class DefaultAiModelConversionService : IAiModelConversionService
{
    private static readonly HashSet<string> VisionInputHints =
    [
        "pixel_values",
        "image",
        "images",
        "input_image",
        "input_tensor",
    ];

    private static readonly string[] RkllmHints =
    [
        "llama",
        "qwen",
        "chatglm",
        "baichuan",
        "mistral",
        "mixtral",
        "gemma",
        "phi",
        "deepseek",
        "yi",
        "internlm",
        "falcon",
        "rwkv",
        "gpt",
    ];

    private static readonly string[] RknnHints =
    [
        "vision-encoder-decoder",
        "trocr",
        "deit",
        "vit",
        "swin",
        "clip",
        "convnext",
        "resnet",
        "mobilenet",
        "efficientnet",
        "yolo",
        "detr",
        "unet",
        "segformer",
        "ocr",
    ];

    private readonly IBlobContainer<AiModelBlobContainer> _blobContainer;
    private readonly ILogger<DefaultAiModelConversionService> _logger;

    public DefaultAiModelConversionService(
        IBlobContainer<AiModelBlobContainer> blobContainer,
        ILogger<DefaultAiModelConversionService> logger
    )
    {
        _blobContainer = blobContainer;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<AiModelConversionAnalysisResult> AnalyzeOnnxModelGroupAsync(
        AiModel model,
        IReadOnlyList<AiModelFile> files,
        CancellationToken cancellationToken = default
    )
    {
        List<AiModelFile> onnxFiles = files
            .Where(x => string.Equals(x.FileFormat, "ONNX", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.SortOrder)
            .ToList();

        if (onnxFiles.Count == 0)
        {
            return new AiModelConversionAnalysisResult
            {
                CanConvert = false,
                ResolvedConversionType = AiModelResolvedConversionType.DirectOnnx,
                Message = "未找到可分析的 ONNX 原始文件，已回退为直接使用 ONNX。",
            };
        }

        List<OnnxModelAnalysisSnapshot> snapshots = [];
        foreach (AiModelFile file in onnxFiles)
        {
            snapshots.Add(await AnalyzeSingleOnnxFileAsync(file, cancellationToken));
        }

        return BuildRecommendation(model, onnxFiles, snapshots);
    }

    /// <inheritdoc/>
    public Task<AiModelConversionDispatchResult> QueueConversionAsync(
        AiModel model,
        IReadOnlyList<AiModelFile> files,
        AiModelResolvedConversionType targetType,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(
            new AiModelConversionDispatchResult
            {
                Queued = false,
                Status = AiModelFileConversionStatus.Pending,
                Message = "Python 转换执行器尚未接入，当前仅完成转换流程编排。",
            }
        );
    }

    private async Task<OnnxModelAnalysisSnapshot> AnalyzeSingleOnnxFileAsync(
        AiModelFile file,
        CancellationToken cancellationToken
    )
    {
        string tempFilePath = Path.Combine(
            Path.GetTempPath(),
            $"aurora-ai-analysis-{Guid.NewGuid():N}.onnx"
        );

        try
        {
            await using Stream blobStream = await _blobContainer.GetAsync(file.BlobName);
            await using FileStream tempStream = new(
                tempFilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None
            );
            await blobStream.CopyToAsync(tempStream, cancellationToken);
            await tempStream.FlushAsync(cancellationToken);

            using InferenceSession session = new(tempFilePath);

            IReadOnlyList<OnnxInputSnapshot> inputs = session
                .InputMetadata.Select(x => new OnnxInputSnapshot(x.Key, x.Value.Dimensions))
                .ToList();

            List<string> metadataTexts =
            [
                file.OriginalFileName,
                file.DisplayName,
                session.ModelMetadata.GraphName,
                session.ModelMetadata.ProducerName,
                session.ModelMetadata.Description,
                session.ModelMetadata.Domain,
                session.ModelMetadata.GraphDescription,
            ];

            foreach (KeyValuePair<string, string> item in session.ModelMetadata.CustomMetadataMap)
            {
                metadataTexts.Add(item.Key);
                metadataTexts.Add(item.Value);
            }

            return new OnnxModelAnalysisSnapshot(file, inputs, metadataTexts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "[DefaultAiModelConversionService] 分析 ONNX 文件 {FileName} 失败，回退为 DirectOnnx。",
                file.OriginalFileName
            );
            return new OnnxModelAnalysisSnapshot(
                file,
                [],
                [file.OriginalFileName, file.DisplayName]
            );
        }
        finally
        {
            try
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(
                    ex,
                    "[DefaultAiModelConversionService] 删除临时 ONNX 分析文件失败：{TempFilePath}",
                    tempFilePath
                );
            }
        }
    }

    private static AiModelConversionAnalysisResult BuildRecommendation(
        AiModel model,
        IReadOnlyList<AiModelFile> files,
        IReadOnlyList<OnnxModelAnalysisSnapshot> snapshots
    )
    {
        Dictionary<string, int> scores = new(StringComparer.OrdinalIgnoreCase)
        {
            ["rknn"] = 0,
            ["rkllm"] = 0,
        };
        List<string> reasons = [];

        foreach (OnnxModelAnalysisSnapshot snapshot in snapshots)
        {
            foreach (string metadataText in snapshot.MetadataTexts)
            {
                string? rkllmHint = FindHint(metadataText, RkllmHints);
                if (rkllmHint is not null)
                {
                    AppendScore(
                        scores,
                        reasons,
                        "rkllm",
                        5,
                        $"文件 {snapshot.File.OriginalFileName} 的元数据包含 {rkllmHint}，更像大语言模型。"
                    );
                }

                string? rknnHint = FindHint(metadataText, RknnHints);
                if (rknnHint is not null)
                {
                    AppendScore(
                        scores,
                        reasons,
                        "rknn",
                        5,
                        $"文件 {snapshot.File.OriginalFileName} 的元数据包含 {rknnHint}，更像视觉/OCR 模型。"
                    );
                }
            }

            foreach (OnnxInputSnapshot input in snapshot.Inputs)
            {
                if (VisionInputHints.Contains(input.Name))
                {
                    AppendScore(
                        scores,
                        reasons,
                        "rknn",
                        5,
                        $"输入 {input.Name} 带有明显图像张量特征。"
                    );
                }

                if (input.Dimensions.Count >= 4)
                {
                    AppendScore(
                        scores,
                        reasons,
                        "rknn",
                        3,
                        $"输入 {input.Name} 的维度为 [{string.Join(", ", input.Dimensions)}]，更像图像张量。"
                    );
                }

                if (
                    string.Equals(
                        input.Name,
                        "encoder_hidden_states",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    AppendScore(
                        scores,
                        reasons,
                        "rknn",
                        3,
                        "输入包含 encoder_hidden_states，更像视觉编码器接文本解码器的 OCR 结构。"
                    );
                }

                if (string.Equals(input.Name, "input_ids", StringComparison.OrdinalIgnoreCase))
                {
                    AppendScore(
                        scores,
                        reasons,
                        "rkllm",
                        2,
                        "输入包含 input_ids，存在文本序列特征。"
                    );
                }

                if (
                    string.Equals(input.Name, "attention_mask", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(input.Name, "position_ids", StringComparison.OrdinalIgnoreCase)
                    || input.Name.Contains("past_key_values", StringComparison.OrdinalIgnoreCase)
                )
                {
                    AppendScore(
                        scores,
                        reasons,
                        "rkllm",
                        1,
                        $"输入 {input.Name} 符合大语言模型常见序列特征。"
                    );
                }
            }

            string stem = Path.GetFileNameWithoutExtension(snapshot.File.OriginalFileName);
            bool splitName =
                stem.Contains("encoder", StringComparison.OrdinalIgnoreCase)
                || stem.Contains("decoder", StringComparison.OrdinalIgnoreCase);
            bool splitRole =
                snapshot.File.FileRole
                is AiModelFileRole.SplitEncoder
                    or AiModelFileRole.SplitDecoder;
            if ((splitName || splitRole) && scores["rknn"] > 0 && scores["rkllm"] <= 2)
            {
                AppendScore(
                    scores,
                    reasons,
                    "rknn",
                    1,
                    $"文件 {snapshot.File.OriginalFileName} 呈现编码器/解码器拆分结构，常见于视觉推理导出。"
                );
            }
        }

        if (scores["rknn"] == 0 && scores["rkllm"] == 0)
        {
            return new AiModelConversionAnalysisResult
            {
                CanConvert = false,
                ResolvedConversionType = AiModelResolvedConversionType.DirectOnnx,
                Message = "OnnxRuntime 未识别出稳定的模型家族信号，已回退为直接使用 ONNX。",
            };
        }

        string suggestedTarget = scores["rknn"] >= scores["rkllm"] ? "rknn" : "rkllm";
        int scoreGap = Math.Abs(scores["rknn"] - scores["rkllm"]);

        if (scoreGap < 2)
        {
            return new AiModelConversionAnalysisResult
            {
                CanConvert = false,
                ResolvedConversionType = AiModelResolvedConversionType.DirectOnnx,
                Message =
                    $"OnnxRuntime 分析结果不够稳定（RKNN={scores["rknn"]}，RKLLM={scores["rkllm"]}），已回退为直接使用 ONNX。",
            };
        }

        AiModelResolvedConversionType resolvedType =
            suggestedTarget == "rknn"
                ? AiModelResolvedConversionType.ToRknn
                : AiModelResolvedConversionType.ToRkllm;
        string summary = string.Join("；", reasons.Distinct().Take(3));

        return new AiModelConversionAnalysisResult
        {
            CanConvert = true,
            ResolvedConversionType = resolvedType,
            Message = string.IsNullOrWhiteSpace(summary)
                ? $"OnnxRuntime 已分析模型 {model.Name}，建议目标为 {resolvedType}。"
                : $"OnnxRuntime 已分析模型 {model.Name}，建议目标为 {resolvedType}。依据：{summary}。",
        };
    }

    private static void AppendScore(
        IDictionary<string, int> scores,
        ICollection<string> reasons,
        string target,
        int weight,
        string reason
    )
    {
        scores[target] += weight;
        reasons.Add(reason);
    }

    private static string? FindHint(string? value, IEnumerable<string> hints)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return hints.FirstOrDefault(hint =>
            value.Contains(hint, StringComparison.OrdinalIgnoreCase)
        );
    }

    private sealed record OnnxModelAnalysisSnapshot(
        AiModelFile File,
        IReadOnlyList<OnnxInputSnapshot> Inputs,
        IReadOnlyList<string> MetadataTexts
    );

    private sealed record OnnxInputSnapshot(string Name, IReadOnlyList<int> Dimensions);
}
