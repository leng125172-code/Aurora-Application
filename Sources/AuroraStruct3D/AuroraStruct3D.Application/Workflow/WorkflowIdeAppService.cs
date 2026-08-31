using System.Text.Json;
using AuroraStruct3D.OpenCV.Registry;
using AuroraStruct3D.OpenCV.Workflow.Compilation;
using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;
using AuroraStruct3D.OpenCV.Workflow.Scripting;
using AuroraStruct3D.OpenCV.Workflow;
using AuroraStruct3D.Workflow.Dtos;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Volo.Abp;
using Volo.Abp.Data;
using System.Text.RegularExpressions;

namespace AuroraStruct3D.Workflow;

[Authorize(AuroraStruct3D.Permissions.AuroraStruct3DAccessPermissions.Management)]
[Route("api/app/workflow/source")]
public sealed class WorkflowIdeAppService : ApplicationService
{
    private static readonly IReadOnlyDictionary<string, string[]> Signatures =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Workflow"] = ["name", "languageVersion"],
            ["Return"] = ["outputs"],
        };

    private readonly IOperatorRegistry _registry;
    private readonly IRepository<WorkflowDefinition, Guid> _repository;
    private readonly IWorkflowLanguageDocumentService _documents;

    public WorkflowIdeAppService(
        IOperatorRegistry registry,
        IRepository<WorkflowDefinition, Guid> repository,
        IWorkflowLanguageDocumentService documents
    )
    {
        _registry = registry;
        _repository = repository;
        _documents = documents;
    }

    [HttpPost("diagnostics")]
    public async Task<WorkflowIdeDiagnosticsDto> DiagnosticsAsync(
        WorkflowIdeDocumentInput input,
        CancellationToken cancellationToken = default
    )
    {
        WorkflowLanguageDocument document = await _documents.GetAsync(
            input.SourceCode, input.DocumentVersion, includeSemantic: false, cancellationToken);
        WorkflowSyntaxTree syntax = document.SyntaxTree;
        WorkflowIdeDiagnosticsDto result = new()
        {
            DocumentVersion = input.DocumentVersion,
            ContentHash = document.ContentHash,
            Diagnostics = syntax.Diagnostics.Select(MapDiagnostic).ToList(),
        };
        if (result.Diagnostics.Any(x => x.Severity == "error"))
            return result;

        try
        {
            document = await _documents.GetAsync(
                input.SourceCode, input.DocumentVersion, includeSemantic: true, cancellationToken);
            WorkflowIntermediateRepresentation ir = document.IntermediateRepresentation!;
            result.SemanticHash = WorkflowProgramHash.ComputeSemanticHash(input.SourceCode);
            WorkflowValidationResult validation = await new WorkflowGraphValidator(_registry)
                .ValidateAsync(ir.Graph);
            foreach (WorkflowDiagnostic diagnostic in validation.Diagnostics)
            {
                WorkflowTextSpan span = diagnostic.NodeId is not null
                    && ir.NodeSpans.TryGetValue(diagnostic.NodeId, out WorkflowTextSpan nodeSpan)
                        ? nodeSpan
                        : new(new(0, 1, 1), new(0, 1, 1));
                result.Diagnostics.Add(
                    new WorkflowIdeDiagnosticDto
                    {
                        Code = diagnostic.Severity == WorkflowDiagnosticSeverity.Error ? "WFC2001" : "WFC2002",
                        Severity = diagnostic.Severity.ToString().ToLowerInvariant(),
                        Message = diagnostic.Message,
                        NodeId = diagnostic.NodeId,
                        StatementId = ir.SyntaxTree.Statements.FirstOrDefault(x => x.NodeId == diagnostic.NodeId)?.StatementId,
                        Range = MapRange(span),
                    }
                );
            }
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            result.Diagnostics.Add(
                new WorkflowIdeDiagnosticDto
                {
                    Code = "WFS1000",
                    Message = ex.Message,
                    Range = new WorkflowIdeRangeDto(),
                }
            );
        }
        return result;
    }

    [HttpPost("completions")]
    public async Task<WorkflowIdeCompletionListDto> CompletionsAsync(WorkflowIdeDocumentInput input)
    {
        List<WorkflowIdeCompletionDto>? memberItems = await TryGetMemberCompletionsAsync(input);
        if (memberItems is not null)
            return new() { DocumentVersion = input.DocumentVersion, Items = memberItems };

        List<WorkflowIdeCompletionDto> items =
        [
            new()
            {
                Label = "Input", Kind = "method",
                InsertText = "var ${1:input} = Input<${2:string}>(\"${3:input}\");",
                Detail = "Strongly typed workflow input",
                Documentation = "Declares a named input port with a registered CLR type.",
            },
            new()
            {
                Label = "Workflow",
                Kind = "method",
                InsertText = "Workflow(\"${1:name}\", 3);",
                Detail = "Workflow declaration",
                Documentation = "Declares the workflow name and V3 strongly typed language version.",
            },
            new()
            {
                Label = "Return",
                Kind = "method",
                InsertText = "Return(${1:output});",
                Detail = "Workflow outputs",
                Documentation = "Returns one or more workflow variables.",
            },
        ];
        foreach (Type type in WorkflowTypeRegistry.GetRegisteredTypes()
                     .OrderBy(x => x.Name, StringComparer.Ordinal))
        {
            items.Add(new WorkflowIdeCompletionDto
            {
                Label = type.Name, Kind = type.IsEnum ? "enum" : "type",
                InsertText = type.Name, Detail = type.FullName,
                SortText = "1_" + type.Name,
            });
        }
        foreach (OperatorDescriptor descriptor in await _registry.GetAllOperatorsAsync())
        {
            OperatorParametersDescriptor? parameters =
                await _registry.GetParametersAsync(descriptor.Id);
            string className = descriptor.TypeFullName.Split('.').Last();
            items.Add(
                new WorkflowIdeCompletionDto
                {
                    Label = className,
                    Kind = "operator",
                    InsertText = BuildOperatorCompletion(className, parameters),
                    Detail = $"{descriptor.Category} · {descriptor.DisplayName}",
                    Documentation = BuildOperatorDocumentation(descriptor, parameters),
                }
            );
        }

        foreach (string variable in ReadDeclaredVariables(input.SourceCode))
        {
            items.Add(
                new WorkflowIdeCompletionDto
                {
                    Label = variable,
                    Kind = "variable",
                    InsertText = variable,
                    Detail = "Workflow variable",
                    Documentation = "Variable declared by an earlier operator output.",
                });
        }
        return new() { DocumentVersion = input.DocumentVersion, Items = items };
    }

    [HttpPost("operator-snippet")]
    public async Task<WorkflowOperatorSnippetDto> OperatorSnippetAsync(
        WorkflowOperatorSnippetInput input,
        CancellationToken cancellationToken = default)
    {
        if (input.OperatorId == Guid.Empty)
            throw new UserFriendlyException("必须选择有效的算子。");

        OperatorDescriptor? descriptor = (await _registry.GetAllOperatorsAsync(cancellationToken))
            .FirstOrDefault(x => x.Id == input.OperatorId);
        if (descriptor is null)
            throw new UserFriendlyException($"算子 {input.OperatorId:D} 未注册。");

        OperatorParametersDescriptor? parameters =
            await _registry.GetParametersAsync(input.OperatorId, cancellationToken);
        IReadOnlyList<ConfigParameterDescriptor> config = parameters?.Config ?? [];
        IReadOnlyDictionary<string, JsonElement> configValues = input.ConfigValues
            ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        HashSet<string> knownConfig = config.Select(x => x.Name)
            .ToHashSet(StringComparer.Ordinal);
        string? unknown = configValues.Keys
            .FirstOrDefault(x => !knownConfig.Contains(x));
        if (unknown is not null)
            throw new UserFriendlyException($"算子没有配置参数 '{unknown}'。");

        foreach (ConfigParameterDescriptor parameter in config)
        {
            if (configValues.TryGetValue(parameter.Name, out JsonElement value))
            {
                ValidateSnippetConfigValue(parameter, value);
                continue;
            }

            if (parameter.Required)
                throw new UserFriendlyException($"缺少必填配置参数 '{parameter.Name}'。");
        }

        string className = descriptor.TypeFullName.Split('.').Last();
        return new WorkflowOperatorSnippetDto
        {
            DocumentVersion = input.DocumentVersion,
            InsertText = BuildOperatorCompletion(className, parameters, configValues),
        };
    }

    private async Task<List<WorkflowIdeCompletionDto>?> TryGetMemberCompletionsAsync(
        WorkflowIdeDocumentInput input)
    {
        int offset = Math.Clamp(input.Offset, 0, input.SourceCode.Length);
        Match access = Regex.Match(input.SourceCode[..offset],
            @"(?<root>[A-Za-z_][A-Za-z0-9_]*)(?<path>(?:\.[A-Za-z_][A-Za-z0-9_]*|\[\d+\])*)\.(?<partial>[A-Za-z_][A-Za-z0-9_]*)?$");
        if (!access.Success)
            return null;

        string root = access.Groups["root"].Value;
        Type? enumType = WorkflowTypeRegistry.Resolve(root);
        if (enumType?.IsEnum == true)
            return Enum.GetNames(enumType).Select(name => new WorkflowIdeCompletionDto
            {
                Label = name, Kind = "enum", InsertText = name,
                Detail = enumType.Name, SortText = "0_" + name,
            }).ToList();
        try
        {
            (_, GraphDataModel parsedGraph) = CSharpWorkflowScript.Parse(input.SourceCode);
            NodeModel? startNode = parsedGraph.Nodes.FirstOrDefault(x => x.Type == "start-node");
            string? inputPort = startNode?.Properties?.OutputBindings?
                .FirstOrDefault(x => x.Value == root).Key;
            string? inputSchema = inputPort is null ? null
                : startNode?.Properties?.OutputBindingSchemas?.GetValueOrDefault(inputPort);
            if (!string.IsNullOrWhiteSpace(inputSchema))
                return CompleteSchema(inputSchema, access.Groups["path"].Value,
                    access.Groups["partial"].Value);
        }
        catch (FormatException) { }
        Match declaration = Regex.Matches(input.SourceCode[..offset],
            @"var\s+(?:(?<single>[A-Za-z_][A-Za-z0-9_]*)|\((?<tuple>[^)]*)\))\s*=\s*(?<op>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
            RegexOptions.Singleline).Cast<Match>().LastOrDefault(x =>
                x.Groups["single"].Value == root
                || x.Groups["tuple"].Value.Split(',').Any(v => v.Trim() == root))!;
        if (declaration is null)
            return [];

        OperatorDescriptor? descriptor = (await _registry.GetAllOperatorsAsync()).FirstOrDefault(x =>
            x.TypeFullName.EndsWith("." + declaration.Groups["op"].Value, StringComparison.Ordinal));
        if (descriptor is null)
            return [];
        OperatorParametersDescriptor? contract = await _registry.GetParametersAsync(descriptor.Id);
        string[] declaredOutputs = declaration.Groups["single"].Success
            ? [declaration.Groups["single"].Value]
            : declaration.Groups["tuple"].Value.Split(',').Select(x => x.Trim()).ToArray();
        int outputIndex = Array.IndexOf(declaredOutputs, root);
        ParameterDescriptor? port = contract?.Outputs.ElementAtOrDefault(outputIndex);
        if (string.IsNullOrWhiteSpace(port?.JsonSchema))
            return [];

        return CompleteSchema(port.JsonSchema, access.Groups["path"].Value,
            access.Groups["partial"].Value);
    }

    private static List<WorkflowIdeCompletionDto> CompleteSchema(
        string jsonSchema, string path, string partial)
    {
        using JsonDocument schemaDocument = JsonDocument.Parse(jsonSchema);
        JsonElement schema = schemaDocument.RootElement;
        foreach (Match segment in Regex.Matches(path, @"\.([A-Za-z_][A-Za-z0-9_]*)|\[(\d+)\]"))
        {
            schema = EffectiveSchema(schema);
            if (segment.Groups[1].Success)
            {
                if (!schema.TryGetProperty("properties", out JsonElement properties)
                    || !properties.TryGetProperty(segment.Groups[1].Value, out schema))
                    return [];
            }
            else if (!schema.TryGetProperty("items", out schema))
                return [];
        }
        schema = EffectiveSchema(schema);
        if (schema.TryGetProperty("type", out JsonElement finalType)
            && finalType.GetString() == "array")
            return [new WorkflowIdeCompletionDto
            {
                Label = "Length", Kind = "property", InsertText = "Length",
                Detail = "int", Documentation = "Number of elements in the array.",
            }];
        if (!schema.TryGetProperty("properties", out JsonElement members))
            return [];
        return members.EnumerateObject()
            .Where(x => x.Name.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
            .Select(x => new WorkflowIdeCompletionDto
            {
                Label = x.Name,
                Kind = "property",
                InsertText = x.Name,
                Detail = x.Value.TryGetProperty("type", out JsonElement type) ? type.ToString() : "member",
                Documentation = x.Value.TryGetProperty("description", out JsonElement description)
                    ? description.GetString() : null,
                SortText = "0_" + x.Name,
            }).ToList();
    }

    private static JsonElement EffectiveSchema(JsonElement schema)
    {
        if (schema.TryGetProperty("anyOf", out JsonElement anyOf))
            foreach (JsonElement candidate in anyOf.EnumerateArray())
                if (candidate.TryGetProperty("type", out JsonElement type)
                    && type.GetString() != "null") return candidate;
        return schema;
    }

    [HttpPost("hover")]
    public Task<WorkflowIdeHoverDto> HoverAsync(WorkflowIdeDocumentInput input)
    {
        WorkflowSyntaxStatement? statement = WorkflowSyntaxParser.Parse(input.SourceCode).FindAt(input.Offset);
        return Task.FromResult(
            new WorkflowIdeHoverDto
            {
                DocumentVersion = input.DocumentVersion,
                Markdown = statement is null
                    ? null
                    : $"**{statement.Method}**  \nStatement `{statement.StatementId}`"
                        + (statement.NodeId is null ? string.Empty : $"  \nNode `{statement.NodeId}`"),
                Range = statement is null ? null : MapRange(statement.Span),
            }
        );
    }

    [HttpPost("signature-help")]
    public async Task<WorkflowIdeSignatureDto> SignatureHelpAsync(WorkflowIdeDocumentInput input)
    {
        WorkflowSyntaxStatement? statement = WorkflowSyntaxParser.Parse(input.SourceCode).FindAt(input.Offset);
        string[] parameters = statement is not null
            && Signatures.TryGetValue(statement.Method, out string[]? found)
                ? found
                : [];
        if (statement is not null && parameters.Length == 0)
        {
            OperatorDescriptor? descriptor = (await _registry.GetAllOperatorsAsync())
                .FirstOrDefault(x =>
                    x.TypeFullName.EndsWith(
                        "." + statement.Method,
                        StringComparison.Ordinal)
                    || x.TypeFullName == statement.Method);
            if (descriptor is not null)
            {
                OperatorParametersDescriptor? contract =
                    await _registry.GetParametersAsync(descriptor.Id);
                parameters =
                [
                    .. (contract?.Inputs ?? []).Select(x => x.ParameterName ?? "input"),
                    .. (contract?.Config ?? []).Select(x => x.Name),
                ];
            }
        }
        return new WorkflowIdeSignatureDto
        {
            DocumentVersion = input.DocumentVersion,
            Label = statement is null
                ? null
                : $"{statement.Method}({string.Join(", ", parameters)})",
            Parameters = parameters.ToList(),
            ActiveParameter = statement?.Arguments.Count == 0
                ? 0
                : Math.Max(0, statement!.Arguments.Count - 1),
        };
    }

    [HttpPost("document-symbols")]
    public Task<WorkflowIdeSymbolsDto> DocumentSymbolsAsync(WorkflowIdeDocumentInput input)
    {
        List<WorkflowIdeSymbolDto> symbols = WorkflowSyntaxParser.Parse(input.SourceCode).Statements
            .Where(x => x.Method == "Workflow" || x.NodeId is not null)
            .Select(x => MapSymbol(x, x.NodeId ?? x.Method))
            .ToList();
        return Task.FromResult(new WorkflowIdeSymbolsDto { DocumentVersion = input.DocumentVersion, Symbols = symbols });
    }

    [HttpPost("definition")]
    public async Task<WorkflowIdeLocationsDto> DefinitionAsync(WorkflowIdeDocumentInput input)
    {
        Match? token = Regex.Matches(input.SourceCode,
                @"[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*|\[\d+\])*")
            .Cast<Match>().FirstOrDefault(x => x.Index <= input.Offset && x.Index + x.Length >= input.Offset);
        if (token is not null && token.Value.Contains('.'))
        {
            string root = Regex.Match(token.Value, @"^[A-Za-z_][A-Za-z0-9_]*").Value;
            string? schema = await TryGetVariableSchemaAsync(input.SourceCode, root);
            if (!string.IsNullOrWhiteSpace(schema))
            {
                string[] memberPath = Regex.Matches(token.Value, @"[A-Za-z_][A-Za-z0-9_]*")
                    .Cast<Match>().Skip(1).Select(x => x.Value).ToArray();
                string member = memberPath.Last();
                (string virtualSource, int line, int column) = BuildVirtualTypeSource(schema, memberPath);
                string schemaHash = WorkflowProgramHash.ComputeContentHash(schema)[..12];
                return new WorkflowIdeLocationsDto
                {
                    DocumentVersion = input.DocumentVersion,
                    Locations = [new WorkflowIdeSymbolDto
                    {
                        Name = member, Kind = "member-definition",
                        Uri = $"aurora-type://{Uri.EscapeDataString(root)}/{schemaHash}",
                        VirtualSource = virtualSource,
                        Range = new WorkflowIdeRangeDto
                        {
                            Start = new WorkflowIdePositionDto { Line = line, Column = column },
                            End = new WorkflowIdePositionDto { Line = line, Column = column + member.Length },
                        },
                    }],
                };
            }
        }
        return await FindLocationsAsync(input, definitionsOnly: true);
    }

    [HttpPost("references")]
    public Task<WorkflowIdeLocationsDto> ReferencesAsync(WorkflowIdeDocumentInput input) =>
        FindLocationsAsync(input, definitionsOnly: false);

    [HttpPost("format")]
    public Task<WorkflowIdeFormatDto> FormatAsync(WorkflowIdeDocumentInput input)
    {
        string formatted = WorkflowSyntaxParser.Format(input.SourceCode);
        WorkflowTextSpan full = new(new(0, 1, 1), Position(input.SourceCode, input.SourceCode.Length));
        return Task.FromResult(
            new WorkflowIdeFormatDto
            {
                DocumentVersion = input.DocumentVersion,
                SourceCode = formatted,
                Edits = [new() { Range = MapRange(full), NewText = formatted }],
            }
        );
    }

    [HttpPost("code-actions")]
    public async Task<WorkflowIdeCodeActionsDto> CodeActionsAsync(WorkflowIdeDocumentInput input)
    {
        WorkflowIdeDiagnosticsDto diagnostics = await DiagnosticsAsync(input);
        return new WorkflowIdeCodeActionsDto
        {
            DocumentVersion = input.DocumentVersion,
            Actions = diagnostics.Diagnostics
                .Where(x => x.Fixes.Count > 0)
                .Select(x => new WorkflowIdeCodeActionDto
                {
                    Title = $"修复：{x.Message}",
                    DiagnosticCode = x.Code,
                    Edits = x.Fixes,
                }).ToList(),
        };
    }

    [HttpPost("semantic-tokens")]
    public Task<WorkflowIdeSemanticTokensDto> SemanticTokensAsync(WorkflowIdeDocumentInput input)
    {
        string source = input.SourceCode;
        List<WorkflowIdeSemanticTokenDto> tokens = [];
        List<(int Start, int End)> occupied = [];
        void Add(Match match, string type, string group = "")
        {
            Group value = string.IsNullOrEmpty(group) ? match.Groups[0] : match.Groups[group];
            if (!value.Success || occupied.Any(x => value.Index < x.End && value.Index + value.Length > x.Start)
                || !IsCodeToken(source, value.Index)) return;
            occupied.Add((value.Index, value.Index + value.Length));
            tokens.Add(new WorkflowIdeSemanticTokenDto
            {
                Range = MapRange(new WorkflowTextSpan(Position(source, value.Index),
                    Position(source, value.Index + value.Length))), Type = type,
            });
        }
        foreach (Match match in Regex.Matches(source, @"\b(?:Workflow|Return|Input|var|true|false|null)\b")) Add(match, "keyword");
        foreach (Match match in Regex.Matches(source, @"Input\s*<\s*(?<type>[A-Za-z_][A-Za-z0-9_\.<>\[\]?]*)")) Add(match, "type", "type");
        foreach (Match match in Regex.Matches(source, @"\bInspectionResultCode\.(?<value>[A-Za-z_][A-Za-z0-9_]*)")) Add(match, "enumMember", "value");
        foreach (Match match in Regex.Matches(source, @"\bvar\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)")) Add(match, "variable", "name");
        foreach (Match match in Regex.Matches(source, @"\.(?<member>[A-Za-z_][A-Za-z0-9_]*)")) Add(match, "property", "member");
        foreach (Match match in Regex.Matches(source, @"(?<call>[A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^;()]+>)?\s*\(")) Add(match, "function", "call");
        foreach (string variable in ReadDeclaredVariables(source))
            foreach (Match match in Regex.Matches(source, $@"\b{Regex.Escape(variable)}\b")) Add(match, "variable");
        tokens = tokens.OrderBy(x => x.Range.Start.Offset).ToList();
        return Task.FromResult(new WorkflowIdeSemanticTokensDto { DocumentVersion = input.DocumentVersion, Tokens = tokens });
    }

    private static bool IsCodeToken(string source, int offset)
    {
        bool inString = false, escaped = false, inLineComment = false;
        for (int i = 0; i < offset; i++)
        {
            char c = source[i];
            if (inLineComment) { if (c == '\n') inLineComment = false; continue; }
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') inString = true;
            else if (c == '/' && i + 1 < offset && source[i + 1] == '/') { inLineComment = true; i++; }
        }
        return !inString && !inLineComment;
    }

    [HttpPost("map-position")]
    public Task<WorkflowIdeMapPositionDto> MapPositionAsync(WorkflowIdeMapPositionInput input)
    {
        WorkflowSyntaxTree tree = WorkflowSyntaxParser.Parse(input.SourceCode);
        WorkflowSyntaxStatement? statement =
            input.StatementId is not null ? tree.Statements.FirstOrDefault(x => x.StatementId == input.StatementId)
            : input.NodeId is not null ? tree.Statements.FirstOrDefault(x => x.NodeId == input.NodeId)
            : tree.FindAt(input.Offset);
        string? portName = input.PortName;
        string? portDirection = input.PortDirection;
        if (statement?.NodeId is not null && string.IsNullOrWhiteSpace(portName))
        {
            try
            {
                (_, GraphDataModel graph) = CSharpWorkflowScript.Parse(input.SourceCode);
                NodeModel? node = EnumerateNodes(graph).FirstOrDefault(x => x.Id == statement.NodeId);
                Match? token = Regex.Matches(input.SourceCode,
                        @"[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*|\[\d+\])*")
                    .Cast<Match>().FirstOrDefault(x => x.Index <= input.Offset && x.Index + x.Length >= input.Offset);
                string root = token is null ? string.Empty
                    : WorkflowValueAccessor.Compile(token.Value).RootVariableName;
                KeyValuePair<string, string>? inputBinding = node?.Properties?.InputBindings?
                    .FirstOrDefault(x => WorkflowValueAccessor.Compile(x.Value).RootVariableName == root);
                KeyValuePair<string, string>? outputBinding = node?.Properties?.OutputBindings?
                    .FirstOrDefault(x => x.Value == root);
                if (inputBinding is { } read && !string.IsNullOrEmpty(read.Key))
                    (portName, portDirection) = (read.Key, "input");
                else if (outputBinding is { } write && !string.IsNullOrEmpty(write.Key))
                    (portName, portDirection) = (write.Key, "output");
            }
            catch (FormatException) { }
        }
        return Task.FromResult(new WorkflowIdeMapPositionDto
        {
            DocumentVersion = input.DocumentVersion,
            StatementId = statement?.StatementId,
            NodeId = statement?.NodeId,
            PortName = portName,
            PortDirection = portDirection,
            Range = statement is null ? null : MapRange(statement.Span),
        });
    }

    [HttpPost("patch-graph")]
    public async Task<WorkflowGraphPatchDto> PatchGraphAsync(WorkflowGraphPatchInput input)
    {
        if (input.WorkflowId.HasValue)
        {
            WorkflowDefinition entity = await _repository.GetAsync(input.WorkflowId.Value);
            if (entity.SourceRevision != input.BaseRevision
                || (!string.IsNullOrWhiteSpace(input.ConcurrencyStamp)
                    && entity.ConcurrencyStamp != input.ConcurrencyStamp))
            {
                AbpDbConcurrencyException conflict = new(
                    $"工作流已被其他操作修改，服务器修订号为 {entity.SourceRevision}。"
                );
                conflict.Data["serverRevision"] = entity.SourceRevision;
                conflict.Data["contentHash"] = entity.SourceHash ?? string.Empty;
                conflict.Data["semanticHash"] = entity.SemanticHash ?? string.Empty;
                conflict.Data["programHash"] = entity.ProgramHash ?? string.Empty;
                throw conflict;
            }
        }
        GraphDataModel graph = input.GraphData.Deserialize<GraphDataModel>(GraphJson.Options) ?? new();
        string name = "Workflow";
        try { (name, _) = CSharpWorkflowScript.Parse(input.SourceCode); } catch (FormatException) { }
        string candidate = CSharpWorkflowScript.Generate(name, graph);
        string source;
        try
        {
            source = WorkflowSyntaxParser.PatchPreservingTrivia(input.SourceCode, candidate);
        }
        catch (WorkflowScriptException ex)
        {
            AbpDbConcurrencyException conflict = new(
                $"无法安全合并画布修改：[{ex.Code}] {ex.Message}"
            );
            conflict.Data["documentVersion"] = input.DocumentVersion;
            conflict.Data["diagnosticCode"] = ex.Code;
            throw conflict;
        }
        return new WorkflowGraphPatchDto
        {
            DocumentVersion = input.DocumentVersion,
            SourceCode = source,
            GraphData = JsonSerializer.SerializeToElement(graph, GraphJson.Options),
        };
    }

    [HttpPost("rename")]
    public Task<WorkflowRefactorPreviewDto> RenameAsync(WorkflowRenameInput input)
    {
        if (!Regex.IsMatch(input.NewName, @"^[A-Za-z_][A-Za-z0-9_]*$"))
            return Task.FromResult(RefactorError(input, "WFR4001", "新变量名不是合法标识符。"));
        HashSet<string> declarations = ReadDeclaredVariables(input.SourceCode).ToHashSet(StringComparer.Ordinal);
        if (!declarations.Contains(input.OldName))
            return Task.FromResult(RefactorError(input, "WFR4002", "当前位置不是可重命名的工作流变量。"));
        if (!string.Equals(input.OldName, input.NewName, StringComparison.Ordinal)
            && declarations.Contains(input.NewName))
            return Task.FromResult(RefactorError(input, "WFR4003", "目标变量名已存在。"));
        if (CSharpKeywords.Contains(input.NewName))
            return Task.FromResult(RefactorError(input, "WFR4004", "目标变量名是保留关键字。"));
        List<Match> matches = Regex.Matches(input.SourceCode, $@"\b{Regex.Escape(input.OldName)}\b")
            .Cast<Match>().Where(match => IsVariableOccurrence(input.SourceCode, match)).ToList();
        List<WorkflowIdeTextEditDto> edits = matches.Select(x =>
            new WorkflowIdeTextEditDto
            {
                Range = MapRange(new WorkflowTextSpan(
                    Position(input.SourceCode, x.Index),
                    Position(input.SourceCode, x.Index + x.Length))),
                NewText = input.NewName,
            }).ToList();
        string source = input.SourceCode;
        foreach (Match match in matches.AsEnumerable().Reverse())
            source = source.Remove(match.Index, match.Length).Insert(match.Index, input.NewName);
        return Task.FromResult(new WorkflowRefactorPreviewDto
        {
            DocumentVersion = input.DocumentVersion,
            SourceCode = source,
            Edits = edits,
        });
    }

    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    { "var", "class", "return", "true", "false", "null", "new", "string", "int", "bool", "double" };

    private static bool IsVariableOccurrence(string source, Match match)
    {
        bool inString = false, escaped = false, inLineComment = false;
        for (int i = 0; i < match.Index; i++)
        {
            char c = source[i];
            if (inLineComment) { if (c == '\n') inLineComment = false; continue; }
            if (inString)
            {
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') inString = true;
            else if (c == '/' && i + 1 < match.Index && source[i + 1] == '/') { inLineComment = true; i++; }
        }
        if (inString || inLineComment) return false;
        int previous = match.Index - 1;
        while (previous >= 0 && char.IsWhiteSpace(source[previous])) previous--;
        if (previous >= 0 && source[previous] == '.') return false;
        int next = match.Index + match.Length;
        while (next < source.Length && char.IsWhiteSpace(source[next])) next++;
        return next >= source.Length || source[next] != ':';
    }

    [HttpPost("extract-subgraph")]
    public Task<WorkflowRefactorPreviewDto> ExtractSubgraphAsync(WorkflowExtractSubgraphInput input)
    {
        WorkflowSyntaxTree tree = WorkflowSyntaxParser.Parse(input.SourceCode);
        List<WorkflowSyntaxStatement> selected = tree.Statements
            .Where(x => input.StatementIds.Contains(x.StatementId, StringComparer.Ordinal))
            .OrderBy(x => x.Span.Start.Offset).ToList();
        if (selected.Count == 0 || selected.Count != input.StatementIds.Distinct().Count())
            return Task.FromResult(RefactorError(input, "WFR4101", "选中语句不存在或已发生变化。"));
        int start = selected[0].Span.Start.Offset;
        int end = selected[^1].Span.End.Offset;
        if (tree.Statements.Any(x => x.Span.Start.Offset > start && x.Span.End.Offset < end
                                     && !input.StatementIds.Contains(x.StatementId)))
            return Task.FromResult(RefactorError(input, "WFR4102", "提取子图要求选择连续的完整语句闭包。"));
        string prefix = $"GraphBegin({JsonSerializer.Serialize(input.ContainerNodeId)});{Environment.NewLine}";
        string suffix = $"{Environment.NewLine}GraphEnd();";
        List<WorkflowIdeTextEditDto> edits =
        [
            new() { Range = MapRange(new(Position(input.SourceCode, start), Position(input.SourceCode, start))), NewText = prefix },
            new() { Range = MapRange(new(Position(input.SourceCode, end), Position(input.SourceCode, end))), NewText = suffix },
        ];
        string source = input.SourceCode.Insert(end, suffix).Insert(start, prefix);
        return Task.FromResult(new WorkflowRefactorPreviewDto
        {
            DocumentVersion = input.DocumentVersion, SourceCode = source, Edits = edits,
        });
    }

    [HttpPost("search-symbols")]
    public Task<WorkflowIdeSymbolsDto> SearchSymbolsAsync(WorkflowSymbolSearchInput input)
    {
        IEnumerable<WorkflowSyntaxStatement> statements = WorkflowSyntaxParser.Parse(input.SourceCode).Statements;
        if (!string.IsNullOrWhiteSpace(input.Kind))
            statements = statements.Where(x => x.Method.Equals(input.Kind, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(input.Query))
            statements = statements.Where(x =>
                x.Text.Contains(input.Query, StringComparison.OrdinalIgnoreCase)
                || (x.NodeId?.Contains(input.Query, StringComparison.OrdinalIgnoreCase) ?? false));
        return Task.FromResult(new WorkflowIdeSymbolsDto
        {
            DocumentVersion = input.DocumentVersion,
            Symbols = statements.Select(x => MapSymbol(x, x.NodeId ?? x.Method)).ToList(),
        });
    }

    private Task<WorkflowIdeLocationsDto> FindLocationsAsync(WorkflowIdeDocumentInput input, bool definitionsOnly)
    {
        Match token = Regex.Matches(input.SourceCode,
                @"[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*|\[\d+\])*")
            .Cast<Match>().FirstOrDefault(x =>
                x.Index <= input.Offset && x.Index + x.Length >= input.Offset)!;
        if (token is not null)
        {
            string root = Regex.Match(token.Value, @"^[A-Za-z_][A-Za-z0-9_]*").Value;
            MatchCollection occurrences = Regex.Matches(input.SourceCode, $@"\b{Regex.Escape(root)}\b");
            Match? declaration = Regex.Matches(input.SourceCode,
                    $@"\bvar\s+(?:{Regex.Escape(root)}\b|\([^)]*\b{Regex.Escape(root)}\b[^)]*\))")
                .Cast<Match>().FirstOrDefault();
            IEnumerable<Match> selected = definitionsOnly
                ? declaration is null ? [] : [declaration]
                : occurrences.Cast<Match>();
            return Task.FromResult(new WorkflowIdeLocationsDto
            {
                DocumentVersion = input.DocumentVersion,
                Locations = selected.Select(x => new WorkflowIdeSymbolDto
                {
                    Name = root,
                    Kind = definitionsOnly ? "variable-definition" : "variable-reference",
                    Range = MapRange(new WorkflowTextSpan(Position(input.SourceCode, x.Index),
                        Position(input.SourceCode, x.Index + x.Length))),
                }).ToList(),
            });
        }

        WorkflowSyntaxTree tree = WorkflowSyntaxParser.Parse(input.SourceCode);
        WorkflowSyntaxStatement? current = tree.FindAt(input.Offset);
        string? nodeId = current?.NodeId;
        IEnumerable<WorkflowSyntaxStatement> statements = nodeId is null
            ? []
            : tree.Statements.Where(x => x.NodeId == nodeId && (!definitionsOnly || x.Method == "Node"));
        return Task.FromResult(new WorkflowIdeLocationsDto
        {
            DocumentVersion = input.DocumentVersion,
            Locations = statements.Select(x => MapSymbol(x, nodeId!)).ToList(),
        });
    }

    private async Task<string?> TryGetVariableSchemaAsync(string source, string root)
    {
        try
        {
            (_, GraphDataModel graph) = CSharpWorkflowScript.Parse(source);
            NodeModel? start = graph.Nodes.FirstOrDefault(x => x.Type == "start-node");
            string? port = start?.Properties?.OutputBindings?.FirstOrDefault(x => x.Value == root).Key;
            if (port is not null && start?.Properties?.OutputBindingSchemas?.GetValueOrDefault(port) is { } inputSchema)
                return inputSchema;
        }
        catch (FormatException) { }
        Match declaration = Regex.Matches(source,
            @"var\s+(?:(?<single>[A-Za-z_][A-Za-z0-9_]*)|\((?<tuple>[^)]*)\))\s*=\s*(?<op>[A-Za-z_][A-Za-z0-9_]*)\s*\(",
            RegexOptions.Singleline).Cast<Match>().LastOrDefault(x =>
                x.Groups["single"].Value == root
                || x.Groups["tuple"].Value.Split(',').Any(v => v.Trim() == root))!;
        if (declaration is null) return null;
        OperatorDescriptor? descriptor = (await _registry.GetAllOperatorsAsync()).FirstOrDefault(x =>
            x.TypeFullName.EndsWith("." + declaration.Groups["op"].Value, StringComparison.Ordinal));
        if (descriptor is null) return null;
        OperatorParametersDescriptor? contract = await _registry.GetParametersAsync(descriptor.Id);
        string[] outputs = declaration.Groups["single"].Success
            ? [declaration.Groups["single"].Value]
            : declaration.Groups["tuple"].Value.Split(',').Select(x => x.Trim()).ToArray();
        return contract?.Outputs.ElementAtOrDefault(Array.IndexOf(outputs, root))?.JsonSchema;
    }

    private static (string Source, int Line, int Column) BuildVirtualTypeSource(
        string schemaJson, IReadOnlyList<string> targetPath)
    {
        using JsonDocument document = JsonDocument.Parse(schemaJson);
        List<string> lines = [];
        int targetLine = 1, targetColumn = 1;
        void WriteType(JsonElement rawSchema, string typeName, IReadOnlyList<string> path)
        {
            JsonElement schema = EffectiveSchema(rawSchema);
            lines.Add($"public sealed class {typeName}");
            lines.Add("{");
            if (schema.TryGetProperty("properties", out JsonElement properties))
                foreach (JsonProperty property in properties.EnumerateObject())
                {
                    JsonElement propertySchema = EffectiveSchema(property.Value);
                    string clrType = SchemaClrName(propertySchema, property.Name);
                    string line = $"    public {clrType} {property.Name} {{ get; init; }}";
                    lines.Add(line);
                    if (path.Count == 1 && property.Name == path[0])
                    {
                        targetLine = lines.Count;
                        targetColumn = line.IndexOf(property.Name, StringComparison.Ordinal) + 1;
                    }
                }
            lines.Add("}");
            if (schema.TryGetProperty("properties", out JsonElement nestedProperties))
                foreach (JsonProperty property in nestedProperties.EnumerateObject())
                {
                    JsonElement nested = EffectiveSchema(property.Value);
                    if (nested.TryGetProperty("type", out JsonElement nestedType)
                        && nestedType.GetString() == "object"
                        && nested.TryGetProperty("properties", out _))
                    {
                        lines.Add(string.Empty);
                        string nestedName = char.ToUpperInvariant(property.Name[0]) + property.Name[1..] + "Details";
                        IReadOnlyList<string> nestedPath = path.Count > 1 && property.Name == path[0]
                            ? path.Skip(1).ToArray() : [];
                        WriteType(nested, nestedName, nestedPath);
                    }
                }
        }
        string title = EffectiveSchema(document.RootElement).TryGetProperty("title", out JsonElement titleNode)
            ? titleNode.GetString() ?? "WorkflowType" : "WorkflowType";
        WriteType(document.RootElement, Regex.Replace(title, "`.*$", string.Empty), targetPath);
        return (string.Join(Environment.NewLine, lines), targetLine, targetColumn);
    }

    private static string SchemaClrName(JsonElement schema, string memberName)
    {
        if (schema.TryGetProperty("enum", out _)) return nameof(InspectionResultCode);
        return schema.TryGetProperty("type", out JsonElement type) ? type.GetString() switch
        {
            "boolean" => "bool", "integer" => "int", "number" => "double",
            "string" => "string", "array" => "IReadOnlyList<object>",
            "object" => char.ToUpperInvariant(memberName[0]) + memberName[1..] + "Details",
            _ => "object",
        } : "object";
    }

    private static IEnumerable<NodeModel> EnumerateNodes(GraphDataModel graph)
    {
        foreach (NodeModel node in graph.Nodes)
        {
            yield return node;
            if (node.Properties?.InnerGraphData is { } inner)
                foreach (NodeModel child in EnumerateNodes(inner)) yield return child;
        }
    }

    internal static string BuildOperatorCompletion(
        string className,
        OperatorParametersDescriptor? parameters,
        IReadOnlyDictionary<string, JsonElement>? fixedConfigValues = null)
    {
        IReadOnlyList<ParameterDescriptor> inputs = parameters?.Inputs ?? [];
        IReadOnlyList<ParameterDescriptor> outputs = parameters?.Outputs ?? [];
        IReadOnlyList<ConfigParameterDescriptor> config = parameters?.Config ?? [];
        int placeholder = 1;
        string assignment = outputs.Count switch
        {
            0 => string.Empty,
            1 => $"var ${{{placeholder++}:{CompletionName(outputs[0].ParameterName, "output")}}} = ",
            _ => "var (" + string.Join(
                    ", ",
                    outputs.Select(x =>
                        $"${{{placeholder++}:{CompletionName(x.ParameterName, "output")}}}"))
                + ") = ",
        };
        List<string> arguments = [];
        arguments.AddRange(
            inputs.Select(x =>
                $"${{{placeholder++}:{CompletionName(x.ParameterName, "input")}}}"));
        foreach (ConfigParameterDescriptor parameter in config)
        {
            if (fixedConfigValues?.TryGetValue(parameter.Name, out JsonElement fixedValue) == true)
            {
                arguments.Add($"{parameter.Name}: {CompletionLiteral(fixedValue)}");
                continue;
            }

            arguments.Add(
                $"{parameter.Name}: ${{{placeholder++}:{CompletionDefault(parameter)}}}");
        }
        if (arguments.Count == 0)
            return $"{assignment}{className}();";
        return $"{assignment}{className}(\n    "
            + string.Join(",\n    ", arguments)
            + "\n);";
    }

    private static string BuildOperatorDocumentation(
        OperatorDescriptor descriptor,
        OperatorParametersDescriptor? parameters)
    {
        List<string> lines = [];
        if (!string.IsNullOrWhiteSpace(descriptor.Description))
            lines.Add(descriptor.Description);
        if (parameters is not null)
        {
            if (parameters.Inputs.Count > 0)
            {
                lines.Add("入参：");
                lines.AddRange(parameters.Inputs.Select(x =>
                    $"- `{x.ParameterName}`（{x.DisplayName ?? x.ParameterName}，{x.ParameterTypeName}）：{x.Description}"));
            }
            if (parameters.Config.Count > 0)
            {
                lines.Add("配置参数：");
                lines.AddRange(parameters.Config.Select(x =>
                    $"- `{x.Name}`（{x.DisplayName ?? x.Name}，{x.ParameterTypeName}）：{x.Description}"));
            }
            if (parameters.Outputs.Count > 0)
            {
                lines.Add("出参：");
                lines.AddRange(parameters.Outputs.Select(x =>
                    $"- `{x.ParameterName}`（{x.DisplayName ?? x.ParameterName}，{x.ParameterTypeName}）：{x.Description}"));
            }
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string CompletionName(string? name, string fallback)
    {
        string value = string.IsNullOrWhiteSpace(name) ? fallback : name;
        return Regex.Replace(value, @"[^A-Za-z0-9_]", "_");
    }

    private static string CompletionDefault(ConfigParameterDescriptor parameter)
    {
        string value = Convert.ToString(
            parameter.DefaultValue,
            System.Globalization.CultureInfo.InvariantCulture) ?? "value";
        if (parameter.ParameterTypeName == typeof(string).FullName
            && !(value.StartsWith('"') && value.EndsWith('"')))
            value = JsonSerializer.Serialize(value);
        return value.Replace("$", "\\$", StringComparison.Ordinal)
            .Replace("}", "\\}", StringComparison.Ordinal);
    }

    private static string CompletionLiteral(JsonElement value) =>
        value.GetRawText()
            .Replace("$", "\\$", StringComparison.Ordinal)
            .Replace("}", "\\}", StringComparison.Ordinal);

    private static void ValidateSnippetConfigValue(
        ConfigParameterDescriptor parameter,
        JsonElement value)
    {
        // ProductModelSelect is 9. CalibProjectSelect is intentionally appended as 11 so the
        // numeric metadata contract remains compatible with existing clients and persisted data.
        bool resourceSelector = (int)parameter.ControlType is 9 or 11;
        if (resourceSelector)
        {
            if (value.ValueKind != JsonValueKind.String
                || !Guid.TryParse(value.GetString(), out Guid id)
                || id == Guid.Empty)
            {
                throw new UserFriendlyException(
                    $"配置参数 '{parameter.Name}' 必须是有效的资源 GUID。");
            }
            return;
        }

        Type? target = ValueCoercion.ResolveType(parameter.ParameterTypeName);
        Type? actual = target is null ? null : Nullable.GetUnderlyingType(target) ?? target;
        bool valid = actual switch
        {
            null => true,
            _ when actual == typeof(string) => value.ValueKind == JsonValueKind.String,
            _ when actual == typeof(bool) => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            _ when actual == typeof(int) => value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out _),
            _ when actual == typeof(long) => value.ValueKind == JsonValueKind.Number
                && value.TryGetInt64(out _),
            _ when actual == typeof(float) || actual == typeof(double) || actual == typeof(decimal) =>
                value.ValueKind == JsonValueKind.Number,
            _ when actual.IsEnum => value.ValueKind == JsonValueKind.String
                && Enum.TryParse(actual, value.GetString(), ignoreCase: true, out _),
            _ => true,
        };
        if (!valid)
            throw new UserFriendlyException(
                $"配置参数 '{parameter.Name}' 不是有效的 {parameter.ParameterTypeName} 值。");

        if ((int)parameter.ControlType == 6
            && value.ValueKind == JsonValueKind.Number
            && TryReadNumericRange(parameter.ValueLimit, out double min, out double max)
            && value.TryGetDouble(out double number)
            && (number < min || number > max))
        {
            throw new UserFriendlyException(
                $"配置参数 '{parameter.Name}' 必须在 {min} 到 {max} 之间。");
        }
    }

    private static bool TryReadNumericRange(object? valueLimit, out double min, out double max)
    {
        min = default;
        max = default;
        if (valueLimit is null)
            return false;

        JsonElement limits = valueLimit is JsonElement json
            ? json
            : JsonSerializer.SerializeToElement(valueLimit, valueLimit.GetType());
        if (limits.ValueKind != JsonValueKind.Array || limits.GetArrayLength() < 2)
            return false;
        JsonElement.ArrayEnumerator values = limits.EnumerateArray();
        if (!values.MoveNext() || !values.Current.TryGetDouble(out min))
            return false;
        if (!values.MoveNext() || !values.Current.TryGetDouble(out max))
            return false;
        return true;
    }

    private static IReadOnlyList<string> ReadDeclaredVariables(string source)
    {
        HashSet<string> variables = new(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(
                     source,
                     @"\bvar\s+(?<single>[A-Za-z_][A-Za-z0-9_]*)\s*=|\bvar\s*\((?<tuple>[^)]*)\)\s*=",
                     RegexOptions.Multiline))
        {
            if (match.Groups["single"].Success)
                variables.Add(match.Groups["single"].Value);
            else
            {
                foreach (string item in match.Groups["tuple"].Value.Split(','))
                {
                    string variable = item.Trim();
                    if (Regex.IsMatch(variable, @"^[A-Za-z_][A-Za-z0-9_]*$"))
                        variables.Add(variable);
                }
            }
        }
        foreach (Match metadata in Regex.Matches(
                     source,
                     @"""externalInputs""\s*:\s*\[(?<items>[^\]]*)\]",
                     RegexOptions.Multiline))
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(
                    "[" + metadata.Groups["items"].Value + "]");
                foreach (JsonElement item in document.RootElement.EnumerateArray())
                {
                    string? variable = item.GetString();
                    if (!string.IsNullOrWhiteSpace(variable))
                        variables.Add(variable);
                }
            }
            catch (JsonException) { }
        }
        return variables.Order(StringComparer.Ordinal).ToList();
    }

    private static WorkflowIdeDiagnosticDto MapDiagnostic(WorkflowSyntaxDiagnostic x)
    {
        WorkflowIdeDiagnosticDto result = new()
        {
            Code = x.Code, Severity = x.Severity, Message = x.Message, NodeId = x.NodeId,
            StatementId = x.StatementId, Range = MapRange(x.Span),
        };
        if (x.Code == "WFS1001")
        {
            WorkflowIdePositionDto end = result.Range.End;
            result.Fixes.Add(new WorkflowIdeTextEditDto
            {
                Range = new WorkflowIdeRangeDto { Start = end, End = end },
                NewText = ";",
            });
        }
        return result;
    }

    private static WorkflowIdeSymbolDto MapSymbol(WorkflowSyntaxStatement x, string name) => new()
    {
        Name = name, Kind = x.Method.ToLowerInvariant(), NodeId = x.NodeId,
        StatementId = x.StatementId, Range = MapRange(x.Span),
    };

    private static WorkflowIdeRangeDto MapRange(WorkflowTextSpan span) => new()
    {
        Start = new() { Offset = span.Start.Offset, Line = span.Start.Line, Column = span.Start.Column },
        End = new() { Offset = span.End.Offset, Line = span.End.Line, Column = span.End.Column },
    };

    private static WorkflowTextPosition Position(string source, int offset)
    {
        int line = 1, start = 0;
        for (int i = 0; i < Math.Min(offset, source.Length); i++)
            if (source[i] == '\n') { line++; start = i + 1; }
        return new(offset, line, offset - start + 1);
    }

    private static WorkflowRefactorPreviewDto RefactorError(
        WorkflowIdeDocumentInput input,
        string code,
        string message
    ) => new()
    {
        DocumentVersion = input.DocumentVersion,
        SourceCode = input.SourceCode,
        Diagnostics =
        [
            new WorkflowIdeDiagnosticDto
            {
                Code = code,
                Message = message,
                Range = new WorkflowIdeRangeDto(),
            },
        ],
    };
}
