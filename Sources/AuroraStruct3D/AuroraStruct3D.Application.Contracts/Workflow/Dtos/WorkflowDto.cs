using System.Text.Json;
using Volo.Abp.Application.Dtos;

namespace AuroraStruct3D.Workflow.Dtos;

/// <summary>
/// 工作流输出 DTO。
/// 作为新建 / 回显 / 修改保存接口的统一响应。
/// <see cref="GraphData"/> 以原生 JSON 形式内联返回，前端可直接渲染画布。
/// </summary>
public class WorkflowDto : FullAuditedEntityDto<Guid>
{
    /// <summary>所属项目唯一标识。</summary>
    public Guid ProjectId { get; set; }

    /// <summary>工作流名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>工作流画布数据 JSON（graphData，原生对象，非转义字符串）。</summary>
    public JsonElement GraphData { get; set; }

    /// <summary>输出变量配置（变量名列表的 JSON 数组）。</summary>
    public string? OutputVariables { get; set; }
}

/// <summary>
/// 工作流输出变量配置 DTO。
/// </summary>
public class WorkflowOutputConfigDto
{
    /// <summary>工作流 ID。</summary>
    public Guid WorkflowId { get; set; }

    /// <summary>输出变量名列表。</summary>
    public List<string> OutputVariables { get; set; } = new();
}

/// <summary>工作流变量可选择的输出路径。</summary>
public class WorkflowOutputPathListDto
{
    public Guid WorkflowId { get; set; }
    public string VariableName { get; set; } = string.Empty;
    public string ValueType { get; set; } = string.Empty;
    public List<WorkflowOutputPathDto> Items { get; set; } = new();
}

/// <summary>单个可选择的输出路径。</summary>
public class WorkflowOutputPathDto
{
    public string Path { get; set; } = string.Empty;
    public string SchemaPath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string ValueType { get; set; } = string.Empty;
    public bool IsLeaf { get; set; }
    public bool IsArray { get; set; }
    public bool IsNullable { get; set; }
    public bool TemplateRenderable { get; set; } = true;
    public int Depth { get; set; }
}

public class WorkflowSourceDto
{
    public Guid WorkflowId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SourceCode { get; set; } = string.Empty;
    public string SourceHash { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string SemanticHash { get; set; } = string.Empty;
    public string ProgramHash { get; set; } = string.Empty;
    public string OperatorContractHash { get; set; } = string.Empty;
    public int LanguageVersion { get; set; }
    public int Revision { get; set; }
    public string ConcurrencyStamp { get; set; } = string.Empty;
    public JsonElement GraphData { get; set; }
}

public class UpdateWorkflowSourceInput
{
    public string SourceCode { get; set; } = string.Empty;
    public int? ExpectedRevision { get; set; }
    public int? BaseRevision { get; set; }
    public int DocumentVersion { get; set; }
    public string EditOrigin { get; set; } = "source";
    public string? ConcurrencyStamp { get; set; }
}

public class WorkflowSourceConversionInput
{
    public string Name { get; set; } = string.Empty;
    public JsonElement GraphData { get; set; }
}

public class WorkflowSourceParseInput
{
    public string SourceCode { get; set; } = string.Empty;
}

public class WorkflowSourceValidationDto
{
    public bool IsValid { get; set; }
    public string? Name { get; set; }
    public string? SourceHash { get; set; }
    public string? ProgramHash { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<WorkflowSourceDiagnosticDto> Diagnostics { get; set; } = new();
}

public class WorkflowSourceDiagnosticDto
{
    public string Code { get; set; } = string.Empty;
    public string Severity { get; set; } = "error";
    public string Message { get; set; } = string.Empty;
    public int Line { get; set; }
    public int Column { get; set; }
    public string? NodeId { get; set; }
}

public class WorkflowSourceMigrationResultDto
{
    public int ScannedCount { get; set; }
    public int MigratedCount { get; set; }
    public List<Guid> FailedWorkflowIds { get; set; } = new();
}

public class WorkflowMigrationItemDto
{
    public Guid? SnapshotId { get; set; }
    public Guid WorkflowId { get; set; }
    public Guid ProjectId { get; set; }
    public string WorkflowName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Diagnostic { get; set; }
    public string? BeforeContentHash { get; set; }
    public string? AfterContentHash { get; set; }
    public string? ProgramHash { get; set; }
    public string? BeforeSourceCode { get; set; }
    public string? BeforeGraphData { get; set; }
    public int BeforeLanguageVersion { get; set; }
    public string? BeforeSemanticHash { get; set; }
    public string? BeforeProgramHash { get; set; }
    public string? BeforeOperatorContractHash { get; set; }
    public string? AfterSourceCode { get; set; }
    public string? AfterGraphData { get; set; }
    public List<string> Changes { get; set; } = [];
    public List<WorkflowMigrationPortChangeDto> PortChanges { get; set; } = [];
    public List<WorkflowMigrationTextChangeDto> SourceChanges { get; set; } = [];
    public bool RolledBack { get; set; }
}

public class WorkflowMigrationPortChangeDto
{
    public string NodeId { get; set; } = string.Empty;
    public Guid OperatorId { get; set; }
    public string OldPort { get; set; } = string.Empty;
    public string OldVariable { get; set; } = string.Empty;
    public string NewPort { get; set; } = "result";
    public string NewExpression { get; set; } = string.Empty;
}

public class WorkflowMigrationTextChangeDto
{
    public int StartLine { get; set; }
    public int DeleteLineCount { get; set; }
    public List<string> NewLines { get; set; } = [];
}

public class WorkflowMigrationPreviewDto
{
    public int ScannedCount { get; set; }
    public int MigratableCount { get; set; }
    public List<WorkflowMigrationItemDto> Items { get; set; } = [];
}

public class WorkflowMigrationBatchDto
{
    public Guid BatchId { get; set; }
    public WorkflowMigrationBatchStatus Status { get; set; }
    public int ScannedCount { get; set; }
    public int MigratedCount { get; set; }
    public int FailedCount { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<WorkflowMigrationItemDto> Items { get; set; } = [];
}
