using System.Text.Json;
using AuroraStruct3D.OpenCV.Registry;
using AuroraStruct3D.OpenCV.Workflow.Compilation;
using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;
using AuroraStruct3D.OpenCV.Workflow.Scripting;
using AuroraStruct3D.Workflow.Dtos;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Volo.Abp;
using Volo.Abp.Data;
using System.Text.RegularExpressions;

namespace AuroraStruct3D.Workflow;

[Route("api/app/workflow/source")]
public sealed class WorkflowIdeAppService : ApplicationService
{
    private static readonly IReadOnlyDictionary<string, string[]> Signatures =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Workflow"] = ["name", "languageVersion"],
            ["GraphBegin"] = ["ownerNodeId"],
            ["GraphEnd"] = [],
            ["Node"] = ["id", "type", "x", "y", "text", "textX", "textY"],
            ["Input"] = ["nodeId", "port", "value", "source"],
            ["Output"] = ["nodeId", "port", "value", "source"],
            ["Param"] = ["nodeId", "name", "value", "source"],
            ["Edge"] = ["id", "sourceNodeId", "targetNodeId", "sourceAnchor", "targetAnchor", "branch"],
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
        List<WorkflowIdeCompletionDto> items = Signatures.Select(x =>
            new WorkflowIdeCompletionDto
            {
                Label = x.Key,
                Kind = "method",
                InsertText = $"{x.Key}({string.Join(", ", x.Value.Select((_, i) => $"${{{i + 1}}}"))});",
                Detail = $"Workflow DSL · {x.Value.Length} parameters",
                Documentation = string.Join(", ", x.Value),
            }
        ).ToList();
        foreach (OperatorDescriptor descriptor in await _registry.GetAllOperatorsAsync())
        {
            items.Add(
                new WorkflowIdeCompletionDto
                {
                    Label = descriptor.DisplayName,
                    Kind = "operator",
                    InsertText = descriptor.Id.ToString(),
                    Detail = descriptor.Category,
                    Documentation = descriptor.Description,
                }
            );
        }
        return new() { DocumentVersion = input.DocumentVersion, Items = items };
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
    public Task<WorkflowIdeSignatureDto> SignatureHelpAsync(WorkflowIdeDocumentInput input)
    {
        WorkflowSyntaxStatement? statement = WorkflowSyntaxParser.Parse(input.SourceCode).FindAt(input.Offset);
        string[] parameters = statement is not null && Signatures.TryGetValue(statement.Method, out string[]? found) ? found : [];
        return Task.FromResult(
            new WorkflowIdeSignatureDto
            {
                DocumentVersion = input.DocumentVersion,
                Label = statement is null ? null : $"{statement.Method}({string.Join(", ", parameters)})",
                Parameters = parameters.ToList(),
                ActiveParameter = statement?.Arguments.Count == 0 ? 0 : Math.Max(0, statement!.Arguments.Count - 1),
            }
        );
    }

    [HttpPost("document-symbols")]
    public Task<WorkflowIdeSymbolsDto> DocumentSymbolsAsync(WorkflowIdeDocumentInput input)
    {
        List<WorkflowIdeSymbolDto> symbols = WorkflowSyntaxParser.Parse(input.SourceCode).Statements
            .Where(x => x.Method is "Workflow" or "GraphBegin" or "Node")
            .Select(x => MapSymbol(x, x.NodeId ?? x.Method))
            .ToList();
        return Task.FromResult(new WorkflowIdeSymbolsDto { DocumentVersion = input.DocumentVersion, Symbols = symbols });
    }

    [HttpPost("definition")]
    public Task<WorkflowIdeLocationsDto> DefinitionAsync(WorkflowIdeDocumentInput input) =>
        FindLocationsAsync(input, definitionsOnly: true);

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
        List<WorkflowIdeSemanticTokenDto> tokens = WorkflowSyntaxParser.Parse(input.SourceCode).Statements
            .Select(x => new WorkflowIdeSemanticTokenDto
            {
                Range = MapRange(x.Span),
                Type = x.Method switch
                {
                    "Node" => "class",
                    "Param" or "Input" or "Output" => "property",
                    _ => "function",
                },
            }).ToList();
        return Task.FromResult(new WorkflowIdeSemanticTokensDto { DocumentVersion = input.DocumentVersion, Tokens = tokens });
    }

    [HttpPost("map-position")]
    public Task<WorkflowIdeMapPositionDto> MapPositionAsync(WorkflowIdeMapPositionInput input)
    {
        WorkflowSyntaxTree tree = WorkflowSyntaxParser.Parse(input.SourceCode);
        WorkflowSyntaxStatement? statement =
            input.StatementId is not null ? tree.Statements.FirstOrDefault(x => x.StatementId == input.StatementId)
            : input.NodeId is not null ? tree.Statements.FirstOrDefault(x => x.NodeId == input.NodeId)
            : tree.FindAt(input.Offset);
        return Task.FromResult(new WorkflowIdeMapPositionDto
        {
            DocumentVersion = input.DocumentVersion,
            StatementId = statement?.StatementId,
            NodeId = statement?.NodeId,
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
        string quotedOld = JsonSerializer.Serialize(input.OldName);
        string quotedNew = JsonSerializer.Serialize(input.NewName);
        MatchCollection matches = Regex.Matches(input.SourceCode, Regex.Escape(quotedOld));
        if (matches.Count == 0)
            return Task.FromResult(RefactorError(input, "WFR4002", "未找到变量定义或引用。"));
        if (!string.Equals(input.OldName, input.NewName, StringComparison.Ordinal)
            && input.SourceCode.Contains(quotedNew, StringComparison.Ordinal))
            return Task.FromResult(RefactorError(input, "WFR4003", "目标变量名已存在。"));
        List<WorkflowIdeTextEditDto> edits = matches.Cast<Match>().Select(x =>
            new WorkflowIdeTextEditDto
            {
                Range = MapRange(new WorkflowTextSpan(
                    Position(input.SourceCode, x.Index),
                    Position(input.SourceCode, x.Index + x.Length))),
                NewText = quotedNew,
            }).ToList();
        string source = input.SourceCode;
        foreach (Match match in matches.Cast<Match>().Reverse())
            source = source.Remove(match.Index, match.Length).Insert(match.Index, quotedNew);
        return Task.FromResult(new WorkflowRefactorPreviewDto
        {
            DocumentVersion = input.DocumentVersion,
            SourceCode = source,
            Edits = edits,
        });
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
