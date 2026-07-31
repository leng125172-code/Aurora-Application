using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AuroraStruct3D.OpenCV.Registry;
using AuroraStruct3D.OpenCV.Workflow.Compilation;
using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;
using AuroraStruct3D.OpenCV.Workflow.Scripting;
using AuroraStruct3D.Variables;
using AuroraStruct3D.Variables.Dtos;
using AuroraStruct3D.Workflow.Dtos;
using AuroraStruct3D.Workflow.Runtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Data;

namespace AuroraStruct3D.Workflow;

/// <summary>
/// 工作流应用服务实现。
/// 新建 / 修改保存接收强类型请求 DTO（<see cref="CreateWorkflowInput"/> / <see cref="UpdateWorkflowInput"/>），
/// 并将 graphData 严格校验后由后端自动组装变量编译输入，再执行离线变量编译并落库。
/// 审计信息由 ABP 自动记录。
/// 端点使用 ABP 约定式路由。
/// </summary>
[Authorize]
[Route("api/app/workflow")]
public class WorkflowAppService : AuroraStruct3DAppService, IWorkflowAppService
{
    private readonly IRepository<WorkflowDefinition, Guid> _repository;
    private readonly IOperatorRegistry _operatorRegistry;
    private readonly IOfflineVariableLibraryAppService _offlineVariableLibrary;
    private readonly IWorkflowProgramCache _programCache;
    private readonly IRepository<WorkflowSourceVersion, Guid> _sourceVersionRepository;
    private readonly WorkflowVariableCompileRequestFactory _compileRequestFactory;

    public WorkflowAppService(
        IRepository<WorkflowDefinition, Guid> repository,
        IOperatorRegistry operatorRegistry,
        IOfflineVariableLibraryAppService offlineVariableLibrary,
        IWorkflowProgramCache programCache,
        IRepository<WorkflowSourceVersion, Guid> sourceVersionRepository,
        WorkflowVariableCompileRequestFactory compileRequestFactory
    )
    {
        _repository = repository;
        _operatorRegistry = operatorRegistry;
        _offlineVariableLibrary = offlineVariableLibrary;
        _programCache = programCache;
        _sourceVersionRepository = sourceVersionRepository;
        _compileRequestFactory = compileRequestFactory;
    }

    /// <inheritdoc/>
    [HttpGet]
    public async Task<List<WorkflowBriefDto>> GetListAsync(Guid projectId)
    {
        IQueryable<WorkflowDefinition> queryable = await _repository.GetQueryableAsync();

        return await AsyncExecuter.ToListAsync(
            queryable
                .Where(w => w.ProjectId == projectId)
                .OrderByDescending(w => w.LastModificationTime)
                .Select(w => new WorkflowBriefDto
                {
                    Id = w.Id,
                    ProjectId = w.ProjectId,
                    Name = w.Name,
                    CreationTime = w.CreationTime,
                    CreatorId = w.CreatorId,
                    LastModificationTime = w.LastModificationTime,
                    LastModifierId = w.LastModifierId,
                })
        );
    }

    /// <inheritdoc/>
    [HttpPost]
    public async Task<WorkflowDto> CreateAsync(CreateWorkflowInput input)
    {
        (Guid projectId, string name, string graphData, GraphDataModel graph) = ReadPayload(input);

        Guid workflowId = GuidGenerator.Create();

        // MOD: 保存前执行图校验 + 离线变量编译，闭合运行时/编译时双重校验。
        await ValidateBeforeSaveAsync(projectId, workflowId, graph);

        WorkflowDefinition workflow = WorkflowDefinition.Create(
            workflowId,
            projectId,
            name,
            graphData
        );
        string sourceCode = CSharpWorkflowScript.Generate(name, graph);
        (string sourceHash, string programHash) = ComputeSourceHashes(sourceCode);
        await _programCache.GetOrAddAsync(programHash, sourceCode);
        workflow.UpdateSource(
            name,
            sourceCode,
            graphData,
            sourceHash,
            programHash,
            CSharpWorkflowScript.LanguageVersion,
            WorkflowProgramHash.ComputeSemanticHash(sourceCode),
            WorkflowProgramHash.ComputeOperatorContractHash()
        );
        workflow.UpdateOutputVariables(
            SerializeOutputVariables(
                WorkflowSignatureExtractor.Extract(graph).Outputs.ToList()
            )
        );

        await _repository.InsertAsync(workflow, autoSave: true);
        await CreateSourceVersionAsync(workflow);

        return MapToDto(workflow);
    }

    /// <inheritdoc/>
    [HttpGet("{id:guid}")]
    public async Task<WorkflowDto> GetAsync(Guid id)
    {
        WorkflowDefinition workflow = await _repository.GetAsync(id);

        return MapToDto(workflow);
    }

    /// <inheritdoc/>
    [HttpPut("{id:guid}")]
    public async Task<WorkflowDto> UpdateAsync(Guid id, UpdateWorkflowInput input)
    {
        (Guid projectId, string name, string graphData, GraphDataModel graph) = ReadPayload(input);

        WorkflowDefinition workflow = await _repository.GetAsync(id);
        EnsureBelongsToProject(workflow, projectId);

        // MOD: 更新同样要求先通过离线变量编译。
        await ValidateBeforeSaveAsync(projectId, workflow.Id, graph);

        string sourceCode = CSharpWorkflowScript.Generate(name, graph);
        (string sourceHash, string programHash) = ComputeSourceHashes(sourceCode);
        await _programCache.GetOrAddAsync(programHash, sourceCode);
        workflow.UpdateSource(
            name,
            sourceCode,
            graphData,
            sourceHash,
            programHash,
            CSharpWorkflowScript.LanguageVersion,
            WorkflowProgramHash.ComputeSemanticHash(sourceCode),
            WorkflowProgramHash.ComputeOperatorContractHash()
        );
        workflow.UpdateOutputVariables(
            SerializeOutputVariables(
                WorkflowSignatureExtractor.Extract(graph).Outputs.ToList()
            )
        );

        try
        {
            await _repository.UpdateAsync(workflow, autoSave: true);
            await CreateSourceVersionAsync(workflow);
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "工作流更新持久化异常：ProjectId={ProjectId}, WorkflowId={WorkflowId}, "
                    + "Revision={Revision}, Name={WorkflowName}",
                projectId,
                workflow.Id,
                workflow.SourceRevision,
                name
            );
            throw new UserFriendlyException(
                $"工作流更新持久化异常（{ex.GetType().Name}）：{ex.Message}"
            );
        }

        return MapToDto(workflow);
    }

    /// <inheritdoc/>
    [HttpDelete("{id:guid}")]
    public async Task DeleteAsync(Guid id)
    {
        WorkflowDefinition workflow = await _repository.GetAsync(id);

        await _repository.DeleteAsync(workflow, autoSave: true);
    }

    /// <inheritdoc/>
    [HttpPost("{id:guid}/validate")]
    public async Task<WorkflowValidateResultDto> ValidateAsync(Guid id)
    {
        WorkflowDefinition workflow = await _repository.GetAsync(id);

        // 解析 graphData JSON 为图模型。
        (_, GraphDataModel graph) = WorkflowGraphCompiler.ParseContent(workflow.GraphData);

        // 执行静态校验。
        WorkflowValidationResult validation = await new WorkflowGraphValidator(
            _operatorRegistry
        ).ValidateAsync(graph);

        return new WorkflowValidateResultDto
        {
            HasErrors = validation.HasErrors,
            Errors = validation
                .Errors.Select(e => new WorkflowDiagnosticDto
                {
                    Severity = e.Severity.ToString(),
                    NodeId = e.NodeId,
                    Target = e.Target,
                    Message = e.Message,
                })
                .ToList(),
            Warnings = validation
                .Warnings.Select(w => new WorkflowDiagnosticDto
                {
                    Severity = w.Severity.ToString(),
                    NodeId = w.NodeId,
                    Target = w.Target,
                    Message = w.Message,
                })
                .ToList(),
        };
    }

    /// <inheritdoc/>
    [HttpPost("{id:guid}/simulate")]
    public async Task<WorkflowDataFlowReportDto> SimulateAsync(Guid id)
    {
        WorkflowDefinition workflow = await _repository.GetAsync(id);

        // 解析并执行数据流仿真。
        (_, GraphDataModel graph) = WorkflowGraphCompiler.ParseContent(workflow.GraphData);

        WorkflowDataFlowReport report = await new WorkflowDataFlowSimulator(
            _operatorRegistry
        ).SimulateAsync(graph, workflow.Name);

        return new WorkflowDataFlowReportDto
        {
            WorkflowName = ResolveResponseWorkflowName(workflow, report.WorkflowName),
            TotalNodeCount = report.TotalNodeCount,
            OperatorNodeCount = report.OperatorNodeCount,
            TotalVariableCount = report.TotalVariableCount,
            TopologyDepth = report.TopologyDepth,
            Nodes = report
                .Nodes.Select(n => new NodeDataFlowInfoDto
                {
                    NodeId = n.NodeId,
                    NodeType = n.NodeType,
                    DisplayName = n.DisplayName,
                    TopologyLayer = n.TopologyLayer,
                    Consumes = n
                        .Consumes.Select(c => new VariablePortRefDto
                        {
                            VariableName = c.VariableName,
                            PortName = c.PortName,
                            TypeName = c.TypeName,
                        })
                        .ToList(),
                    Produces = n
                        .Produces.Select(p => new VariablePortRefDto
                        {
                            VariableName = p.VariableName,
                            PortName = p.PortName,
                            TypeName = p.TypeName,
                        })
                        .ToList(),
                })
                .ToList(),
            Variables = report
                .Variables.Select(v => new VariableLifecycleDto
                {
                    VariableName = v.VariableName,
                    TypeName = v.TypeName,
                    DefinedByNodeId = v.DefinedByNodeId,
                    ConsumedByNodeIds = v.ConsumedByNodeIds.ToList(),
                })
                .ToList(),
        };
    }

    /// <inheritdoc/>
    [HttpGet("{id:guid}/output-config")]
    public async Task<WorkflowOutputConfigDto> GetOutputConfigAsync(Guid id)
    {
        WorkflowDefinition workflow = await _repository.GetAsync(id);

        List<string> outputVariables = ParseOutputVariablesJson(workflow.OutputVariables);
        return new WorkflowOutputConfigDto { WorkflowId = id, OutputVariables = outputVariables };
    }

    /// <inheritdoc/>
    [HttpGet("{id:guid}/output-paths")]
    public async Task<WorkflowOutputPathListDto> GetOutputPathsAsync(
        Guid id,
        string variableName
    )
    {
        if (string.IsNullOrWhiteSpace(variableName))
        {
            throw new UserFriendlyException("VariableName 不能为空。");
        }

        string rootVariableName = GetOutputRootVariableName(variableName.Trim());
        WorkflowDefinition workflow = await _repository.GetAsync(id);
        (_, GraphDataModel graph) = WorkflowGraphCompiler.ParseContent(workflow.GraphData);

        (Guid OperatorId, string PortName)? producer = FindOutputProducer(
            graph,
            rootVariableName
        );
        if (producer is null)
        {
            throw new UserFriendlyException($"未找到变量 '{rootVariableName}' 的产出端口。");
        }

        OperatorParametersDescriptor? descriptor = await _operatorRegistry.GetParametersAsync(
            producer.Value.OperatorId
        );
        ParameterDescriptor? output = descriptor?.Outputs.FirstOrDefault(x =>
            string.Equals(x.ParameterName, producer.Value.PortName, StringComparison.Ordinal)
        );
        if (output is null)
        {
            throw new UserFriendlyException($"变量 '{rootVariableName}' 缺少输出类型元数据。");
        }

        List<WorkflowOutputPathDto> items =
        [
            new()
            {
                Path = rootVariableName,
                Name = rootVariableName,
                DisplayName = output.DisplayName,
                ValueType = ToFrontendValueType(output.ParameterTypeName),
                IsLeaf = string.IsNullOrWhiteSpace(output.JsonSchema),
                IsArray = false,
                Depth = 0,
            },
        ];

        if (!string.IsNullOrWhiteSpace(output.JsonSchema))
        {
            try
            {
                JsonNode? schema = JsonNode.Parse(output.JsonSchema);
                AppendSchemaPaths(schema, rootVariableName, 1, items);
                items[0].IsLeaf = items.Count == 1;
            }
            catch (JsonException ex)
            {
                throw new UserFriendlyException(
                    $"变量 '{rootVariableName}' 的输出 Schema 无效：{ex.Message}"
                );
            }
        }

        return new WorkflowOutputPathListDto
        {
            WorkflowId = id,
            VariableName = rootVariableName,
            ValueType = items[0].ValueType,
            Items = items,
        };
    }

    [HttpGet("{id:guid}/source")]
    public async Task<WorkflowSourceDto> GetSourceAsync(Guid id)
    {
        WorkflowDefinition workflow = await _repository.GetAsync(id);
        string source =
            workflow.SourceCode
            ?? CSharpWorkflowScript.Generate(
                workflow.Name,
                WorkflowGraphCompiler.ParseContent(workflow.GraphData).Graph
            );
        (string sourceHash, string programHash) = ComputeSourceHashes(source);
        return MapSourceDto(workflow, source, sourceHash, programHash);
    }

    [HttpPut("{id:guid}/source")]
    public async Task<WorkflowSourceDto> UpdateSourceAsync(
        Guid id,
        UpdateWorkflowSourceInput input
    )
    {
        WorkflowDefinition workflow = await _repository.GetAsync(id);
        if (
            input.ExpectedRevision.HasValue
            && input.ExpectedRevision.Value != workflow.SourceRevision
        )
        {
            throw CreateConcurrencyException(workflow);
        }
        if (
            !string.IsNullOrWhiteSpace(input.ConcurrencyStamp)
            && !string.Equals(
                input.ConcurrencyStamp,
                workflow.ConcurrencyStamp,
                StringComparison.Ordinal
            )
        )
        {
            throw CreateConcurrencyException(workflow);
        }

        (string name, GraphDataModel graph) = CSharpWorkflowScript.Parse(input.SourceCode);
        await ValidateBeforeSaveAsync(workflow.ProjectId, workflow.Id, graph);
        string graphData = JsonSerializer.Serialize(graph, GraphJson.Options);
        (string sourceHash, string programHash) = ComputeSourceHashes(input.SourceCode);
        string semanticHash = WorkflowProgramHash.ComputeSemanticHash(input.SourceCode);
        string operatorContractHash = WorkflowProgramHash.ComputeOperatorContractHash();
        await _programCache.GetOrAddAsync(programHash, input.SourceCode);
        workflow.UpdateSource(
            name,
            input.SourceCode,
            graphData,
            sourceHash,
            programHash,
            CSharpWorkflowScript.LanguageVersion,
            semanticHash,
            operatorContractHash
        );
        workflow.UpdateOutputVariables(
            SerializeOutputVariables(WorkflowSignatureExtractor.Extract(graph).Outputs.ToList())
        );
        await _repository.UpdateAsync(workflow, autoSave: true);
        await CreateSourceVersionAsync(workflow);
        return MapSourceDto(workflow, input.SourceCode, sourceHash, programHash);
    }

    [HttpPost("source/validate")]
    public async Task<WorkflowSourceValidationDto> ValidateSourceAsync(
        WorkflowSourceParseInput input
    )
    {
        WorkflowSourceValidationDto result = new();
        try
        {
            (string name, GraphDataModel graph) = CSharpWorkflowScript.Parse(input.SourceCode);
            WorkflowValidationResult validation = await new WorkflowGraphValidator(
                _operatorRegistry
            ).ValidateAsync(graph);
            result.Name = name;
            result.Errors = validation.Errors.Select(x => x.Message).ToList();
            result.Diagnostics = validation.Diagnostics
                .Select(x => new WorkflowSourceDiagnosticDto
                {
                    Code =
                        x.Severity == WorkflowDiagnosticSeverity.Error
                            ? "WFC2001"
                            : "WFC2002",
                    Severity = x.Severity.ToString().ToLowerInvariant(),
                    Message = x.Message,
                    Line = 0,
                    Column = 0,
                    NodeId = x.NodeId,
                })
                .ToList();
            result.IsValid = result.Errors.Count == 0;
            (result.SourceHash, result.ProgramHash) = ComputeSourceHashes(input.SourceCode);
        }
        catch (WorkflowScriptException ex)
        {
            result.Errors.Add(ex.Message);
            result.Diagnostics.Add(
                new WorkflowSourceDiagnosticDto
                {
                    Code = ex.Code,
                    Message = ex.Message,
                    Line = ex.Line,
                    Column = ex.Column,
                    NodeId = ex.NodeId,
                }
            );
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            result.Errors.Add(ex.Message);
            result.Diagnostics.Add(
                new WorkflowSourceDiagnosticDto
                {
                    Code = "WFS1000",
                    Message = ex.Message,
                    Line = 0,
                    Column = 0,
                }
            );
        }
        return result;
    }

    [HttpPost("graph/to-source")]
    public Task<string> ConvertGraphToSourceAsync(WorkflowSourceConversionInput input)
    {
        GraphDataModel graph =
            input.GraphData.Deserialize<GraphDataModel>(GraphJson.Options) ?? new();
        return Task.FromResult(CSharpWorkflowScript.Generate(input.Name, graph));
    }

    [HttpPost("source/to-graph")]
    public Task<JsonElement> ConvertSourceToGraphAsync(WorkflowSourceParseInput input)
    {
        (_, GraphDataModel graph) = CSharpWorkflowScript.Parse(input.SourceCode);
        return Task.FromResult(
            JsonSerializer.SerializeToElement(graph, GraphJson.Options)
        );
    }

    [HttpPost("source/migrate-legacy")]
    public async Task<WorkflowSourceMigrationResultDto> MigrateLegacySourcesAsync(
        int batchSize = 100
    )
    {
        batchSize = Math.Clamp(batchSize, 1, 1000);
        List<WorkflowDefinition> workflows = await AsyncExecuter.ToListAsync(
            (await _repository.GetQueryableAsync())
                .Where(x => x.SourceCode == null || x.SourceCode == string.Empty)
                .OrderBy(x => x.CreationTime)
                .Take(batchSize)
        );
        WorkflowSourceMigrationResultDto result = new() { ScannedCount = workflows.Count };
        foreach (WorkflowDefinition workflow in workflows)
        {
            try
            {
                (_, GraphDataModel graph) = WorkflowGraphCompiler.ParseContent(
                    workflow.GraphData
                );
                string source = CSharpWorkflowScript.Generate(workflow.Name, graph);
                (string sourceHash, string programHash) = ComputeSourceHashes(source);
                await _programCache.GetOrAddAsync(programHash, source);
                workflow.UpdateSource(
                    workflow.Name,
                    source,
                    workflow.GraphData,
                    sourceHash,
                    programHash,
                    CSharpWorkflowScript.LanguageVersion,
                    WorkflowProgramHash.ComputeSemanticHash(source),
                    WorkflowProgramHash.ComputeOperatorContractHash()
                );
                await _repository.UpdateAsync(workflow, autoSave: true);
                result.MigratedCount++;
            }
            catch
            {
                result.FailedWorkflowIds.Add(workflow.Id);
            }
        }
        return result;
    }

    /// <inheritdoc/>
    [Obsolete("输出变量由结束节点自动生成，请修改工作流图后调用 CreateAsync/UpdateAsync。")]
    [HttpPut("{id:guid}/output-config")]
    public async Task<WorkflowOutputConfigDto> UpdateOutputConfigAsync(
        Guid id,
        WorkflowOutputConfigDto input
    )
    {
        WorkflowDefinition workflow = await _repository.GetAsync(id);

        WorkflowSignature signature = WorkflowSignatureExtractor.Extract(workflow.GraphData);
        List<string> derivedOutputs = signature.Outputs.ToList();
        if (!derivedOutputs.SequenceEqual(input.OutputVariables, StringComparer.Ordinal))
        {
            throw new UserFriendlyException(
                "输出变量由结束节点自动生成，请在工作流结束节点中调整输出绑定后保存。"
            );
        }

        string outputVariablesJson = SerializeOutputVariables(derivedOutputs);
        workflow.UpdateOutputVariables(outputVariablesJson);

        await _repository.UpdateAsync(workflow, autoSave: true);

        return new WorkflowOutputConfigDto
        {
            WorkflowId = id,
            OutputVariables = derivedOutputs,
        };
    }

    // ─────────────────────────── 私有辅助 ───────────────────────────

    private async Task CreateSourceVersionAsync(WorkflowDefinition workflow)
    {
        var version = new WorkflowSourceVersion(
            GuidGenerator.Create(),
            workflow.Id,
            workflow.SourceRevision,
            workflow.SourceCode!,
            workflow.GraphData,
            workflow.SourceHash!,
            workflow.SemanticHash!,
            workflow.ProgramHash!
        );
        await _sourceVersionRepository.InsertAsync(version, autoSave: true);
    }

    private static (Guid OperatorId, string PortName)? FindOutputProducer(
        GraphDataModel graph,
        string variableName
    )
    {
        foreach (NodeModel node in graph.Nodes)
        {
            if (
                Guid.TryParse(node.Type, out Guid operatorId)
                && node.Properties?.OutputBindings is { } bindings
            )
            {
                foreach ((string portName, string boundVariableName) in bindings)
                {
                    if (string.Equals(boundVariableName, variableName, StringComparison.Ordinal))
                    {
                        return (operatorId, portName);
                    }
                }
            }

            if (node.Properties?.InnerGraphData is { } innerGraph)
            {
                (Guid OperatorId, string PortName)? inner = FindOutputProducer(
                    innerGraph,
                    variableName
                );
                if (inner is not null)
                {
                    return inner;
                }
            }
        }

        return null;
    }

    private static (string SourceHash, string ProgramHash) ComputeSourceHashes(string source)
        => (
            WorkflowProgramHash.ComputeSourceHash(source),
            WorkflowProgramHash.ComputeProgramHash(source)
        );

    private static AbpDbConcurrencyException CreateConcurrencyException(
        WorkflowDefinition workflow
    )
    {
        AbpDbConcurrencyException exception = new(
            $"工作流已被其他操作修改，服务器修订号为 {workflow.SourceRevision}。"
        );
        exception.Data["serverRevision"] = workflow.SourceRevision;
        exception.Data["contentHash"] = workflow.SourceHash ?? string.Empty;
        exception.Data["semanticHash"] = workflow.SemanticHash ?? string.Empty;
        exception.Data["programHash"] = workflow.ProgramHash ?? string.Empty;
        return exception;
    }

    private static WorkflowSourceDto MapSourceDto(
        WorkflowDefinition workflow,
        string source,
        string sourceHash,
        string programHash
    )
    {
        using JsonDocument graph = JsonDocument.Parse(workflow.GraphData);
        return new WorkflowSourceDto
        {
            WorkflowId = workflow.Id,
            Name = workflow.Name,
            SourceCode = source,
            SourceHash = sourceHash,
            ContentHash = sourceHash,
            SemanticHash = workflow.SemanticHash
                ?? WorkflowProgramHash.ComputeSemanticHash(source),
            ProgramHash = programHash,
            OperatorContractHash = workflow.OperatorContractHash
                ?? WorkflowProgramHash.ComputeOperatorContractHash(),
            LanguageVersion = CSharpWorkflowScript.LanguageVersion,
            Revision = workflow.SourceRevision,
            ConcurrencyStamp = workflow.ConcurrencyStamp,
            GraphData = graph.RootElement.Clone(),
        };
    }

    private static void AppendSchemaPaths(
        JsonNode? schema,
        string parentPath,
        int depth,
        ICollection<WorkflowOutputPathDto> result
    )
    {
        if (schema is not JsonObject schemaObject)
        {
            return;
        }

        string type = schemaObject["type"]?.GetValue<string>() ?? "object";
        if (type == "array")
        {
            JsonNode? itemsSchema = schemaObject["items"];
            string itemPath = parentPath + "[0]";
            result.Add(
                new WorkflowOutputPathDto
                {
                    Path = itemPath,
                    SchemaPath = parentPath + "[]",
                    Name = "[0]",
                    DisplayName = itemsSchema?["title"]?.GetValue<string>() ?? "数组元素",
                    ValueType = GetSchemaValueType(itemsSchema),
                    IsLeaf = !SchemaHasChildren(itemsSchema),
                    IsArray = false,
                    IsNullable = IsNullableSchema(itemsSchema),
                    Depth = depth,
                }
            );
            AppendSchemaPaths(itemsSchema, itemPath, depth + 1, result);
            return;
        }

        if (schemaObject["properties"] is not JsonObject properties)
        {
            return;
        }

        foreach ((string propertyName, JsonNode? propertySchema) in properties)
        {
            string path = parentPath + "." + propertyName;
            bool isArray = string.Equals(
                propertySchema?["type"]?.GetValue<string>(),
                "array",
                StringComparison.Ordinal
            );
            result.Add(
                new WorkflowOutputPathDto
                {
                    Path = path,
                    SchemaPath = path,
                    Name = propertyName,
                    DisplayName = propertySchema?["title"]?.GetValue<string>(),
                    ValueType = GetSchemaValueType(propertySchema),
                    IsLeaf = !SchemaHasChildren(propertySchema),
                    IsArray = isArray,
                    IsNullable = IsNullableSchema(propertySchema),
                    Depth = depth,
                }
            );
            AppendSchemaPaths(propertySchema, path, depth + 1, result);
        }
    }

    private static bool SchemaHasChildren(JsonNode? schema) =>
        schema is JsonObject obj
        && (
            obj["properties"] is JsonObject properties && properties.Count > 0
            || obj["type"]?.GetValue<string>() == "array" && obj["items"] is not null
        );

    private static bool IsNullableSchema(JsonNode? schema) =>
        schema is JsonObject obj
        && (
            obj["nullable"]?.GetValue<bool>() == true
            || obj["type"] is JsonArray types
                && types.Any(x => x?.GetValue<string>() == "null")
        );

    private static string GetSchemaValueType(JsonNode? schema) =>
        schema?["type"]?.GetValue<string>() switch
        {
            "boolean" => "bool",
            "integer" => "long",
            "number" => "double",
            "array" => "array",
            "object" => "object",
            "null" => "null",
            _ => "string",
        };

    private static string ToFrontendValueType(string clrTypeName) =>
        clrTypeName switch
        {
            "System.Boolean" => "bool",
            "System.Int16" or "System.Int32" or "System.Int64" => "long",
            "System.Single" or "System.Double" or "System.Decimal" => "double",
            "System.String" => "string",
            _ => clrTypeName,
        };

    /// <summary>
    /// 从 WorkflowPayload 中解析 projectId / name，并提取 graphData 作为入库内容。
    /// </summary>
    private static (
        Guid ProjectId,
        string Name,
        string GraphData,
        GraphDataModel Graph
    ) ReadPayload(CreateWorkflowInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.ProjectId == Guid.Empty)
        {
            throw new UserFriendlyException("工作流缺少有效的 projectId。");
        }

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            throw new UserFriendlyException("工作流缺少名称 name。");
        }

        if (input.GraphData is null)
        {
            throw new UserFriendlyException("工作流内容缺少 graphData。");
        }

        (string validatedGraphData, GraphDataModel graph) = ValidateAndSerializeGraphData(
            input.GraphData
        );
        return (input.ProjectId, input.Name, validatedGraphData, graph);
    }

    private static (
        Guid ProjectId,
        string Name,
        string GraphData,
        GraphDataModel Graph
    ) ReadPayload(UpdateWorkflowInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.ProjectId == Guid.Empty)
        {
            throw new UserFriendlyException("工作流缺少有效的 projectId。");
        }

        if (string.IsNullOrWhiteSpace(input.Name))
        {
            throw new UserFriendlyException("工作流缺少名称 name。");
        }

        if (input.GraphData is null)
        {
            throw new UserFriendlyException("工作流内容缺少 graphData。");
        }

        (string validatedGraphData, GraphDataModel graph) = ValidateAndSerializeGraphData(
            input.GraphData
        );
        return (input.ProjectId, input.Name, validatedGraphData, graph);
    }

    private static (string GraphJson, GraphDataModel Graph) ValidateAndSerializeGraphData(
        WorkflowGraphDto graphData
    )
    {
        string graphJsonText = JsonSerializer.Serialize(graphData, GraphJson.Options);
        GraphDataModel graph =
            JsonSerializer.Deserialize<GraphDataModel>(graphJsonText, GraphJson.Options)
            ?? new GraphDataModel();

        List<string> errors = WorkflowSourceFlagContractValidator.Validate(graph);
        if (errors.Count > 0)
        {
            throw new UserFriendlyException(
                "工作流 source flags 校验失败：" + string.Join(" | ", errors)
            );
        }

        return (JsonSerializer.Serialize(graph, GraphJson.Options), graph);
    }

    private async Task ValidateBeforeSaveAsync(
        Guid projectId,
        Guid workflowId,
        GraphDataModel graph
    )
    {
        VariableDefUseAnalysisMode defUseAnalysisMode = VariableDefUseAnalysisMode.Conservative;

        // MOD: 严格模式下启用重复定义错误（支持显式 shadow）。
        bool strictValidation = defUseAnalysisMode == VariableDefUseAnalysisMode.Strict;
        WorkflowValidationResult graphValidation = await new WorkflowGraphValidator(
            _operatorRegistry,
            new WorkflowValidationOptions { StrictVariableDefinition = strictValidation }
        ).ValidateAsync(graph);

        if (graphValidation.HasErrors)
        {
            throw new UserFriendlyException(
                "工作流图校验失败："
                    + string.Join(" | ", graphValidation.Errors.Select(x => x.Message))
            );
        }

        VariableCompileRequestDto variableCompileRequest = await _compileRequestFactory.BuildAsync(
            projectId,
            workflowId,
            graph,
            defUseAnalysisMode
        );

        VariableCompileResultDto compileResult;
        try
        {
            compileResult = await _offlineVariableLibrary.CompileAsync(variableCompileRequest);
        }
        catch (Exception ex)
        {
            Logger.LogError(
                ex,
                "工作流离线变量编译异常：ProjectId={ProjectId}, WorkflowId={WorkflowId}, "
                    + "Declarations={DeclarationCount}, Reads={ReadCount}, Writes={WriteCount}",
                projectId,
                workflowId,
                variableCompileRequest.Declarations.Count,
                variableCompileRequest.Reads.Count,
                variableCompileRequest.Writes.Count
            );
            throw new UserFriendlyException(
                $"离线变量编译异常（{ex.GetType().Name}）：{ex.Message}"
            );
        }

        if (!compileResult.CanPublish)
        {
            List<string> errors = compileResult
                .Diagnostics.Where(x => x.Severity == VariableDiagnosticSeverity.Error)
                .Select(x => x.Message)
                .Take(10)
                .ToList();
            throw new UserFriendlyException("离线变量编译未通过：" + string.Join(" | ", errors));
        }
    }

    private async Task<VariableCompileRequestDto> BuildVariableCompileRequestAsync(
        Guid projectId,
        Guid workflowId,
        GraphDataModel graph,
        VariableDefUseAnalysisMode defUseAnalysisMode
    )
    {
        string? entryNodeId = graph
            .Nodes.FirstOrDefault(x =>
                string.Equals(x.Type, "start-node", StringComparison.Ordinal)
            )
            ?.Id;
        if (string.IsNullOrWhiteSpace(entryNodeId))
        {
            throw new UserFriendlyException(
                "variableCompileRequest.EntryNodeId 不能为空。请确保图中存在 start-node。"
            );
        }

        List<VariableControlFlowEdgeDto> controlFlowEdges = graph
            .Edges.Where(x =>
                !string.IsNullOrWhiteSpace(x.SourceNodeId)
                && !string.IsNullOrWhiteSpace(x.TargetNodeId)
            )
            .Select(x => new VariableControlFlowEdgeDto
            {
                FromNodeId = x.SourceNodeId!,
                ToNodeId = x.TargetNodeId!,
            })
            .ToList();

        if (controlFlowEdges.Count == 0)
        {
            throw new UserFriendlyException(
                "variableCompileRequest.ControlFlowEdges 不能为空，必须提供完整 CFG。"
            );
        }

        List<Guid> operatorIds = graph
            .Nodes.Select(node => Guid.TryParse(node.Type, out Guid parsed) ? parsed : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        Dictionary<Guid, OperatorParametersDescriptor?> operatorParameterMap = new();
        foreach (Guid operatorId in operatorIds)
        {
            operatorParameterMap[operatorId] = await _operatorRegistry.GetParametersAsync(
                operatorId
            );
        }

        Dictionary<string, int> sequenceMap = graph
            .Nodes.Select((node, index) => new { node.Id, Sequence = index + 1 })
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .ToDictionary(x => x.Id, x => x.Sequence, StringComparer.Ordinal);

        List<VariableReferenceDto> reads = new();
        List<VariableReferenceDto> writes = new();
        Dictionary<string, VariableDeclarationDto> declarationMap = new(StringComparer.Ordinal);

        foreach (NodeModel node in graph.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id))
            {
                continue;
            }

            NodePropertiesModel properties = node.Properties ?? new NodePropertiesModel();
            Dictionary<string, string> inputBindings = properties.InputBindings ?? [];
            Dictionary<string, string> inputBindingSources = properties.InputBindingSources ?? [];
            Dictionary<string, JsonElement> @params = properties.Params ?? [];
            Dictionary<string, string> paramSources = properties.ParamSources ?? [];
            Dictionary<string, string> outputBindings = properties.OutputBindings ?? [];
            Dictionary<string, string> outputBindingSources = properties.OutputBindingSources ?? [];

            OperatorParametersDescriptor? parameters = null;
            if (Guid.TryParse(node.Type, out Guid operatorId))
            {
                parameters = operatorParameterMap.GetValueOrDefault(operatorId);
            }

            foreach ((string key, string variableNameRaw) in inputBindings)
            {
                if (
                    !string.Equals(
                        inputBindingSources.GetValueOrDefault(key),
                        "variable",
                        StringComparison.Ordinal
                    )
                )
                {
                    continue;
                }

                string variableName = variableNameRaw.Trim();
                if (node.Type == "end-node")
                {
                    variableName = GetOutputRootVariableName(variableName);
                }
                if (string.IsNullOrWhiteSpace(variableName))
                {
                    throw new UserFriendlyException(
                        $"节点 {node.Id} 的输入绑定 {key} 变量名为空。"
                    );
                }

                string? expectedTypeName = parameters
                    ?.Inputs.FirstOrDefault(x =>
                        string.Equals(x.ParameterName, key, StringComparison.Ordinal)
                    )
                    ?.ParameterTypeName;

                reads.Add(
                    new VariableReferenceDto
                    {
                        OwnerWorkflowId = workflowId,
                        VariableName = variableName,
                        Location = $"{node.Id}:inputBindings:{key}",
                        Sequence = sequenceMap.GetValueOrDefault(node.Id),
                        ExpectedTypeName = expectedTypeName,
                    }
                );
            }

            foreach ((string key, JsonElement valueElement) in @params)
            {
                if (
                    !string.Equals(
                        paramSources.GetValueOrDefault(key),
                        "variable",
                        StringComparison.Ordinal
                    )
                )
                {
                    continue;
                }

                string variableName = ExtractVariableName(valueElement, node.Id, key);
                string? expectedTypeName = parameters
                    ?.Config.FirstOrDefault(x =>
                        string.Equals(x.Name, key, StringComparison.Ordinal)
                    )
                    ?.ParameterTypeName;

                reads.Add(
                    new VariableReferenceDto
                    {
                        OwnerWorkflowId = workflowId,
                        VariableName = variableName,
                        Location = $"{node.Id}:params:{key}",
                        Sequence = sequenceMap.GetValueOrDefault(node.Id),
                        ExpectedTypeName = expectedTypeName,
                    }
                );
            }

            foreach ((string key, string variableNameRaw) in outputBindings)
            {
                if (
                    !string.Equals(
                        outputBindingSources.GetValueOrDefault(key),
                        "variable",
                        StringComparison.Ordinal
                    )
                )
                {
                    continue;
                }

                string variableName = variableNameRaw.Trim();
                if (string.IsNullOrWhiteSpace(variableName))
                {
                    throw new UserFriendlyException(
                        $"节点 {node.Id} 的输出绑定 {key} 变量名为空。"
                    );
                }

                string? expectedTypeName = parameters
                    ?.Outputs.FirstOrDefault(x =>
                        string.Equals(x.ParameterName, key, StringComparison.Ordinal)
                    )
                    ?.ParameterTypeName;
                if (string.IsNullOrWhiteSpace(expectedTypeName))
                {
                    throw new UserFriendlyException(
                        $"节点 {node.Id} 的输出端口 {key} 缺少类型元数据，无法自动生成变量声明。"
                    );
                }

                writes.Add(
                    new VariableReferenceDto
                    {
                        OwnerWorkflowId = workflowId,
                        VariableName = variableName,
                        Location = $"{node.Id}:outputBindings:{key}",
                        Sequence = sequenceMap.GetValueOrDefault(node.Id),
                        ExpectedTypeName = expectedTypeName,
                    }
                );

                if (
                    declarationMap.TryGetValue(variableName, out VariableDeclarationDto? existing)
                    && !string.Equals(existing.TypeName, expectedTypeName, StringComparison.Ordinal)
                )
                {
                    throw new UserFriendlyException(
                        $"变量 {variableName} 在图中被赋予了冲突类型：{existing.TypeName} 与 {expectedTypeName}。"
                    );
                }

                declarationMap[variableName] = new VariableDeclarationDto
                {
                    Name = variableName,
                    TypeName = expectedTypeName,
                    Visibility = VariableVisibility.Private,
                    Mutability = VariableMutability.Mutable,
                    IsRequiredInit = false,
                    DefaultValueJson = null,
                };
            }
        }

        return new VariableCompileRequestDto
        {
            ProjectId = projectId,
            WorkflowId = workflowId,
            Declarations = declarationMap.Values.ToList(),
            Imports = [],
            Reads = reads,
            Writes = writes,
            EntryNodeId = entryNodeId,
            ControlFlowEdges = controlFlowEdges,
            DefUseAnalysisMode = defUseAnalysisMode,
            SuppressedDiagnosticCodes = [],
        };
    }

    private static string ExtractVariableName(JsonElement value, string nodeId, string paramName)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            string? text = value.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }

        if (
            value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty("$var", out JsonElement varElement)
            && varElement.ValueKind == JsonValueKind.String
        )
        {
            string? text = varElement.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }

        throw new UserFriendlyException(
            $"节点 {nodeId} 的参数 {paramName} 标记为 variable，但值不是合法变量引用。"
        );
    }

    private static string ResolveResponseWorkflowName(
        WorkflowDefinition workflow,
        string fallbackName
    )
    {
        if (!string.IsNullOrWhiteSpace(workflow.Name))
        {
            return workflow.Name;
        }

        return fallbackName;
    }

    /// <summary>
    /// 校验工作流归属：防止跨项目读取 / 修改他人工作流。
    /// </summary>
    private static void EnsureBelongsToProject(WorkflowDefinition workflow, Guid projectId)
    {
        if (workflow.ProjectId != projectId)
        {
            throw new UserFriendlyException("工作流不存在或不属于该项目。");
        }
    }

    /// <summary>
    /// 将实体映射为输出 DTO。
    /// <see cref="WorkflowDefinition.GraphData"/> 直接解析为 JsonElement 返回。
    /// </summary>
    private static WorkflowDto MapToDto(WorkflowDefinition workflow)
    {
        using JsonDocument doc = JsonDocument.Parse(workflow.GraphData);

        return new WorkflowDto
        {
            Id = workflow.Id,
            ProjectId = workflow.ProjectId,
            Name = workflow.Name,
            GraphData = doc.RootElement.Clone(),
            OutputVariables = workflow.OutputVariables,
            CreationTime = workflow.CreationTime,
            CreatorId = workflow.CreatorId,
            LastModificationTime = workflow.LastModificationTime,
            LastModifierId = workflow.LastModifierId,
            IsDeleted = workflow.IsDeleted,
            DeletionTime = workflow.DeletionTime,
            DeleterId = workflow.DeleterId,
        };
    }

    private static string GetOutputRootVariableName(string outputPath)
    {
        int dotIndex = outputPath.IndexOf('.');
        int bracketIndex = outputPath.IndexOf('[');
        int separatorIndex =
            dotIndex < 0 ? bracketIndex
            : bracketIndex < 0 ? dotIndex
            : Math.Min(dotIndex, bracketIndex);
        return separatorIndex < 0 ? outputPath : outputPath[..separatorIndex];
    }

    private static List<string> ParseOutputVariablesJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, GraphJson.Options) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string SerializeOutputVariables(List<string> outputVariables)
    {
        return JsonSerializer.Serialize(outputVariables, GraphJson.Options);
    }
}
