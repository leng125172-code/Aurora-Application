using System.Text.Json;

namespace AuroraStruct3D.Workflow.Dtos;

public class WorkflowIdeDocumentInput
{
    public string SourceCode { get; set; } = string.Empty;
    public int DocumentVersion { get; set; }
    public int Offset { get; set; }
    public Guid? WorkflowId { get; set; }
    public Guid? ProjectId { get; set; }
}

public class WorkflowIdePositionDto
{
    public int Offset { get; set; }
    public int Line { get; set; }
    public int Column { get; set; }
}

public class WorkflowIdeRangeDto
{
    public WorkflowIdePositionDto Start { get; set; } = new();
    public WorkflowIdePositionDto End { get; set; } = new();
}

public class WorkflowIdeDiagnosticDto
{
    public string Code { get; set; } = string.Empty;
    public string Severity { get; set; } = "error";
    public string Message { get; set; } = string.Empty;
    public WorkflowIdeRangeDto Range { get; set; } = new();
    public string? NodeId { get; set; }
    public string? StatementId { get; set; }
    public List<WorkflowIdeTextEditDto> Fixes { get; set; } = [];
}

public class WorkflowIdeDiagnosticsDto
{
    public int DocumentVersion { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string? SemanticHash { get; set; }
    public List<WorkflowIdeDiagnosticDto> Diagnostics { get; set; } = [];
}

public class WorkflowIdeCodeActionsDto
{
    public int DocumentVersion { get; set; }
    public List<WorkflowIdeCodeActionDto> Actions { get; set; } = [];
}

public class WorkflowIdeCodeActionDto
{
    public string Title { get; set; } = string.Empty;
    public string Kind { get; set; } = "quickfix";
    public string? DiagnosticCode { get; set; }
    public List<WorkflowIdeTextEditDto> Edits { get; set; } = [];
}

public class WorkflowIdeTextEditDto
{
    public WorkflowIdeRangeDto Range { get; set; } = new();
    public string NewText { get; set; } = string.Empty;
}

public class WorkflowIdeCompletionDto
{
    public string Label { get; set; } = string.Empty;
    public string Kind { get; set; } = "method";
    public string InsertText { get; set; } = string.Empty;
    public string? Detail { get; set; }
    public string? Documentation { get; set; }
    public WorkflowIdeRangeDto? ReplacementRange { get; set; }
    public WorkflowTypeSymbolDto? TypeSymbol { get; set; }
    public string? SortText { get; set; }
}

public class WorkflowIdeCompletionListDto
{
    public int DocumentVersion { get; set; }
    public List<WorkflowIdeCompletionDto> Items { get; set; } = [];
}

public class WorkflowIdeHoverDto
{
    public int DocumentVersion { get; set; }
    public string? Markdown { get; set; }
    public WorkflowIdeRangeDto? Range { get; set; }
}

public class WorkflowIdeSignatureDto
{
    public int DocumentVersion { get; set; }
    public string? Label { get; set; }
    public int ActiveParameter { get; set; }
    public List<string> Parameters { get; set; } = [];
}

public class WorkflowIdeSymbolDto
{
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "node";
    public string? NodeId { get; set; }
    public string? StatementId { get; set; }
    public WorkflowIdeRangeDto Range { get; set; } = new();
    public string? Uri { get; set; }
    public string? VirtualSource { get; set; }
}

public class WorkflowIdeSymbolsDto
{
    public int DocumentVersion { get; set; }
    public List<WorkflowIdeSymbolDto> Symbols { get; set; } = [];
}

public class WorkflowIdeLocationsDto
{
    public int DocumentVersion { get; set; }
    public List<WorkflowIdeSymbolDto> Locations { get; set; } = [];
}

public class WorkflowIdeFormatDto
{
    public int DocumentVersion { get; set; }
    public string SourceCode { get; set; } = string.Empty;
    public List<WorkflowIdeTextEditDto> Edits { get; set; } = [];
}

public class WorkflowIdeSemanticTokenDto
{
    public WorkflowIdeRangeDto Range { get; set; } = new();
    public string Type { get; set; } = string.Empty;
}

public class WorkflowIdeSemanticTokensDto
{
    public int DocumentVersion { get; set; }
    public List<WorkflowIdeSemanticTokenDto> Tokens { get; set; } = [];
}

public class WorkflowIdeMapPositionInput : WorkflowIdeDocumentInput
{
    public string? StatementId { get; set; }
    public string? NodeId { get; set; }
    public string? PortName { get; set; }
    public string? PortDirection { get; set; }
}

public class WorkflowIdeMapPositionDto
{
    public int DocumentVersion { get; set; }
    public string? StatementId { get; set; }
    public string? NodeId { get; set; }
    public string? PortName { get; set; }
    public string? PortDirection { get; set; }
    public WorkflowIdeRangeDto? Range { get; set; }
}

public class WorkflowGraphPatchInput : WorkflowIdeDocumentInput
{
    public int BaseRevision { get; set; }
    public string? ConcurrencyStamp { get; set; }
    public JsonElement GraphData { get; set; }
}

public class WorkflowGraphPatchDto
{
    public int DocumentVersion { get; set; }
    public bool HasConflict { get; set; }
    public string SourceCode { get; set; } = string.Empty;
    public JsonElement GraphData { get; set; }
    public List<WorkflowIdeDiagnosticDto> Diagnostics { get; set; } = [];
    public List<WorkflowIdeRangeDto> Conflicts { get; set; } = [];
}

public class WorkflowSourceDraftInput
{
    public string SourceCode { get; set; } = string.Empty;
    public JsonElement GraphData { get; set; }
    public int BaseRevision { get; set; }
}

public class WorkflowSourceDraftDto
{
    public Guid Id { get; set; }
    public Guid WorkflowId { get; set; }
    public int BaseRevision { get; set; }
    public string SourceCode { get; set; } = string.Empty;
    public JsonElement GraphData { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public DateTime CreationTime { get; set; }
}

public class WorkflowSourceVersionDto : WorkflowSourceDraftDto
{
    public int Revision { get; set; }
    public string SemanticHash { get; set; } = string.Empty;
    public string ProgramHash { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
}

public class WorkflowSourceVersionDiffDto
{
    public int FromRevision { get; set; }
    public int ToRevision { get; set; }
    public string FromContentHash { get; set; } = string.Empty;
    public string ToContentHash { get; set; } = string.Empty;
    public List<WorkflowIdeTextEditDto> Edits { get; set; } = [];
}

public class WorkflowRenameInput : WorkflowIdeDocumentInput
{
    public string OldName { get; set; } = string.Empty;
    public string NewName { get; set; } = string.Empty;
}

public class WorkflowRefactorPreviewDto
{
    public int DocumentVersion { get; set; }
    public string SourceCode { get; set; } = string.Empty;
    public List<WorkflowIdeTextEditDto> Edits { get; set; } = [];
    public List<WorkflowIdeDiagnosticDto> Diagnostics { get; set; } = [];
}

public class WorkflowExtractSubgraphInput : WorkflowIdeDocumentInput
{
    public string ContainerNodeId { get; set; } = string.Empty;
    public List<string> StatementIds { get; set; } = [];
}

public class WorkflowSymbolSearchInput : WorkflowIdeDocumentInput
{
    public string? Query { get; set; }
    public string? Kind { get; set; }
}

public enum WorkflowTypeKind
{
    Unknown,
    Primitive,
    Object,
    Array,
    Image,
    PointCloud,
    Region,
    FileReference,
    Nullable,
    Union,
    Enum,
}

public class WorkflowTypeSymbolDto
{
    public WorkflowTypeKind Kind { get; set; }
    public string Name { get; set; } = "unknown";
    public bool Nullable { get; set; }
    public WorkflowTypeSymbolDto? ElementType { get; set; }
    public Dictionary<string, WorkflowTypeSymbolDto> Properties { get; set; } = [];
    public List<string> EnumValues { get; set; } = [];
    public string? Documentation { get; set; }
}
