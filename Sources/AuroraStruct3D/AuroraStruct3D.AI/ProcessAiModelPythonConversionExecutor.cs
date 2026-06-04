using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AuroraStruct3D.AI;

/// <summary>
/// 基于外部 Python 进程的 AI 模型转换执行器。
/// </summary>
public sealed class ProcessAiModelPythonConversionExecutor : IAiModelPythonConversionExecutor
{
    private const string PythonAssetDirectoryName = "Python";
    private const string ConversionScriptFileName = "AiModelConvert.py";

    private readonly ILogger<ProcessAiModelPythonConversionExecutor> _logger;

    public ProcessAiModelPythonConversionExecutor(
        ILogger<ProcessAiModelPythonConversionExecutor> logger
    )
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<AiModelPythonConversionResult> ExecuteAsync(
        AiModelPythonConversionRequest request,
        CancellationToken cancellationToken = default
    )
    {
        string scriptPath = ResolveScriptPath();
        string pythonExecutable = ResolvePythonExecutable();
        string resultFilePath = Path.Combine(request.WorkingDirectory, "conversion-result.json");

        _logger.LogInformation(
            "[ProcessAiModelPythonConversionExecutor] 使用 Python={PythonExecutable}，Script={ScriptPath}，ModelId={ModelId}",
            pythonExecutable,
            scriptPath,
            request.ModelId
        );

        Directory.CreateDirectory(request.WorkingDirectory);

        ProcessStartInfo startInfo = new()
        {
            FileName = pythonExecutable,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("--target");
        startInfo.ArgumentList.Add(
            request.TargetType == AiModelResolvedConversionType.ToRknn ? "rknn" : "rkllm"
        );
        startInfo.ArgumentList.Add("--model-id");
        startInfo.ArgumentList.Add(request.ModelId.ToString("D"));
        startInfo.ArgumentList.Add("--model-name");
        startInfo.ArgumentList.Add(request.ModelName);
        startInfo.ArgumentList.Add("--output-dir");
        startInfo.ArgumentList.Add(Path.Combine(request.WorkingDirectory, "outputs"));
        startInfo.ArgumentList.Add("--result-json");
        startInfo.ArgumentList.Add(resultFilePath);

        foreach (
            AiModelPythonConversionInputFile input in request.InputFiles.OrderBy(x => x.SortOrder)
        )
        {
            startInfo.ArgumentList.Add("--input");
            startInfo.ArgumentList.Add(input.FilePath);
            startInfo.ArgumentList.Add("--source-file-id");
            startInfo.ArgumentList.Add(input.SourceFileId.ToString("D"));
            startInfo.ArgumentList.Add("--file-role");
            startInfo.ArgumentList.Add(((int)input.FileRole).ToString());
            startInfo.ArgumentList.Add("--sort-order");
            startInfo.ArgumentList.Add(input.SortOrder.ToString());
        }

        using Process process = new() { StartInfo = startInfo };
        process.Start();

        Task<string> stdOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stdErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        string standardOutput = await stdOutTask;
        string standardError = await stdErrTask;

        _logger.LogInformation(
            "[ProcessAiModelPythonConversionExecutor] Python 转换进程结束，ExitCode={ExitCode}，ModelId={ModelId}",
            process.ExitCode,
            request.ModelId
        );

        if (!File.Exists(resultFilePath))
        {
            throw new InvalidOperationException(
                $"Python 转换脚本未生成结果文件：{resultFilePath}。错误输出：{standardError}"
            );
        }

        string json = await File.ReadAllTextAsync(resultFilePath, cancellationToken);
        JsonSerializerOptions serializerOptions = new(JsonSerializerDefaults.Web);
        ProcessConversionResponse? response = JsonSerializer.Deserialize<ProcessConversionResponse>(
            json,
            serializerOptions
        );
        if (response is null)
        {
            throw new InvalidOperationException("Python 转换结果 JSON 为空或无法解析。");
        }

        if (!response.Success)
        {
            return new AiModelPythonConversionResult
            {
                Success = false,
                Message = response.Message ?? "Python 转换失败。",
                StandardOutput = string.IsNullOrWhiteSpace(response.StandardOutput)
                    ? standardOutput
                    : response.StandardOutput,
                StandardError = string.IsNullOrWhiteSpace(response.StandardError)
                    ? standardError
                    : response.StandardError,
            };
        }

        return new AiModelPythonConversionResult
        {
            Success = process.ExitCode == 0,
            Message = response.Message ?? "Python 转换完成。",
            StandardOutput = string.IsNullOrWhiteSpace(response.StandardOutput)
                ? standardOutput
                : response.StandardOutput,
            StandardError = string.IsNullOrWhiteSpace(response.StandardError)
                ? standardError
                : response.StandardError,
            OutputFiles = response
                .OutputFiles.Select(x => new AiModelPythonConversionOutputFile
                {
                    SourceFileId = x.SourceFileId,
                    FilePath = x.FilePath,
                    FileName = x.FileName,
                    DisplayName = x.DisplayName,
                    FileFormat = x.FileFormat,
                    FileRole = (AiModelFileRole)x.FileRole,
                    SortOrder = x.SortOrder,
                })
                .ToList(),
        };
    }

    private static string ResolvePythonExecutable()
    {
        return Environment.GetEnvironmentVariable("AURORA_AI_CONVERTER_PYTHON")
            ?? (OperatingSystem.IsWindows() ? "python" : "python3");
    }

    private static string ResolveScriptPath()
    {
        string? configured = Environment.GetEnvironmentVariable("AURORA_AI_CONVERTER_SCRIPT");
        string? configuredPath = ResolveExistingPath(configured);
        if (configuredPath is not null)
        {
            return configuredPath;
        }

        string[] candidates =
        [
            Path.Combine(
                AppContext.BaseDirectory,
                PythonAssetDirectoryName,
                ConversionScriptFileName
            ),
            Path.Combine(
                Directory.GetCurrentDirectory(),
                PythonAssetDirectoryName,
                ConversionScriptFileName
            ),
            Path.Combine(AppContext.BaseDirectory, "Tools", ConversionScriptFileName),
            Path.Combine(Directory.GetCurrentDirectory(), "Tools", ConversionScriptFileName),
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "AuroraStruct3D.AI",
                PythonAssetDirectoryName,
                ConversionScriptFileName
            ),
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "Tools",
                ConversionScriptFileName
            ),
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "Tools",
                ConversionScriptFileName
            ),
            Path.Combine(
                Directory.GetCurrentDirectory(),
                "..",
                "AuroraStruct3D.AI",
                PythonAssetDirectoryName,
                ConversionScriptFileName
            ),
            Path.Combine(
                Directory.GetCurrentDirectory(),
                "..",
                "..",
                "..",
                "Tools",
                ConversionScriptFileName
            ),
            Path.Combine(
                Directory.GetCurrentDirectory(),
                "..",
                "..",
                "..",
                "..",
                "Tools",
                ConversionScriptFileName
            ),
        ];

        string? matched = candidates
            .Select(path => Path.GetFullPath(path))
            .FirstOrDefault(File.Exists);
        if (matched is not null)
        {
            return matched;
        }

        throw new FileNotFoundException(
            "未找到 AI 模型 Python 转换脚本，请设置环境变量 AURORA_AI_CONVERTER_SCRIPT。"
        );
    }

    private static string? ResolveExistingPath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        string trimmedPath = rawPath.Trim();
        IEnumerable<string> candidates = Path.IsPathRooted(trimmedPath)
            ? [trimmedPath]
            :
            [
                trimmedPath,
                Path.Combine(Directory.GetCurrentDirectory(), trimmedPath),
                Path.Combine(AppContext.BaseDirectory, trimmedPath),
            ];

        return candidates.Select(path => Path.GetFullPath(path)).FirstOrDefault(File.Exists);
    }

    private sealed class ProcessConversionResponse
    {
        public bool Success { get; set; }

        public string? Message { get; set; }

        public string? StandardOutput { get; set; }

        public string? StandardError { get; set; }

        public List<ProcessConversionOutputFile> OutputFiles { get; set; } = [];
    }

    private sealed class ProcessConversionOutputFile
    {
        public Guid SourceFileId { get; set; }

        public string FilePath { get; set; } = string.Empty;

        public string FileName { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string FileFormat { get; set; } = string.Empty;

        public int FileRole { get; set; }

        public int SortOrder { get; set; }
    }
}
