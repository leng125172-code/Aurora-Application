using System.Text.Json;
using AuroraStruct3D.OpenCV.Workflow.Scripting;
using AuroraStruct3D.Workflow.Dtos;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Volo.Abp;

namespace AuroraStruct3D.Workflow;

[Route("api/app/workflow")]
public sealed class WorkflowSourceHistoryAppService : ApplicationService
{
    private readonly IRepository<WorkflowDefinition, Guid> _workflows;
    private readonly IRepository<WorkflowSourceVersion, Guid> _versions;

    public WorkflowSourceHistoryAppService(
        IRepository<WorkflowDefinition, Guid> workflows,
        IRepository<WorkflowSourceVersion, Guid> versions
    )
    {
        _workflows = workflows;
        _versions = versions;
    }

    [HttpGet("{id:guid}/source/versions")]
    public async Task<List<WorkflowSourceVersionDto>> GetVersionsAsync(Guid id)
    {
        List<WorkflowSourceVersion> versions = await AsyncExecuter.ToListAsync(
            (await _versions.GetQueryableAsync())
                .Where(x => x.WorkflowId == id)
                .OrderByDescending(x => x.Revision)
        );
        return versions.Select(MapVersion).ToList();
    }

    [HttpGet("{id:guid}/source/versions/{revision:int}")]
    public async Task<WorkflowSourceVersionDto> GetVersionAsync(Guid id, int revision) =>
        MapVersion(await GetVersionEntityAsync(id, revision));

    [HttpGet("{id:guid}/source/versions/diff")]
    public async Task<WorkflowSourceVersionDiffDto> DiffAsync(Guid id, int from, int to)
    {
        WorkflowSourceVersion left = await GetVersionEntityAsync(id, from);
        WorkflowSourceVersion right = await GetVersionEntityAsync(id, to);
        WorkflowTextPosition end = Position(left.SourceCode, left.SourceCode.Length);
        return new WorkflowSourceVersionDiffDto
        {
            FromRevision = from,
            ToRevision = to,
            FromContentHash = left.ContentHash,
            ToContentHash = right.ContentHash,
            Edits = left.ContentHash == right.ContentHash
                ? []
                :
                [
                    new WorkflowIdeTextEditDto
                    {
                        Range = new WorkflowIdeRangeDto
                        {
                            Start = new WorkflowIdePositionDto { Line = 1, Column = 1 },
                            End = new WorkflowIdePositionDto
                            {
                                Offset = end.Offset, Line = end.Line, Column = end.Column,
                            },
                        },
                        NewText = right.SourceCode,
                    },
                ],
        };
    }

    [HttpPost("{id:guid}/source/versions/{revision:int}/restore")]
    public async Task<WorkflowSourceVersionDto> RestoreAsync(Guid id, int revision)
    {
        WorkflowSourceVersion version = await GetVersionEntityAsync(id, revision);
        WorkflowDefinition workflow = await _workflows.GetAsync(id);
        (string name, _) = CSharpWorkflowScript.Parse(version.SourceCode);
        string semanticHash = WorkflowProgramHash.ComputeSemanticHash(version.SourceCode);
        string operatorHash = WorkflowProgramHash.ComputeOperatorContractHash();
        workflow.UpdateSource(
            name, version.SourceCode, version.GraphData,
            WorkflowProgramHash.ComputeContentHash(version.SourceCode),
            WorkflowProgramHash.ComputeProgramHash(version.SourceCode),
            CSharpWorkflowScript.LanguageVersion, semanticHash, operatorHash);
        await _workflows.UpdateAsync(workflow);
        return await CreateVersionAsync(id);
    }

    [HttpPost("{id:guid}/source/versions/{revision:int}/publish")]
    public async Task<WorkflowSourceVersionDto> PublishAsync(Guid id, int revision)
    {
        WorkflowSourceVersion version = await GetVersionEntityAsync(id, revision);
        if (!version.IsPublished)
        {
            version.Publish();
            await _versions.UpdateAsync(version, autoSave: true);
        }
        return MapVersion(version);
    }

    [HttpPost("{id:guid}/source/versions")]
    public async Task<WorkflowSourceVersionDto> CreateVersionAsync(Guid id, bool publish = false)
    {
        WorkflowDefinition workflow = await _workflows.GetAsync(id);
        WorkflowSourceVersion? existing = await _versions.FirstOrDefaultAsync(x =>
            x.WorkflowId == id && x.Revision == workflow.SourceRevision
        );
        if (existing is not null)
            return MapVersion(existing);
        string source = workflow.SourceCode ?? string.Empty;
        WorkflowSourceVersion version = new(
            GuidGenerator.Create(), id, workflow.SourceRevision, source, workflow.GraphData,
            WorkflowProgramHash.ComputeContentHash(source),
            WorkflowProgramHash.ComputeSemanticHash(source),
            WorkflowProgramHash.ComputeProgramHash(source),
            publish
        );
        await _versions.InsertAsync(version, autoSave: true);
        return MapVersion(version);
    }

    private static WorkflowSourceVersionDto MapVersion(WorkflowSourceVersion x) => new()
    {
        Id = x.Id, WorkflowId = x.WorkflowId, BaseRevision = x.Revision,
        Revision = x.Revision, SourceCode = x.SourceCode, GraphData = Parse(x.GraphData),
        ContentHash = x.ContentHash, SemanticHash = x.SemanticHash,
        ProgramHash = x.ProgramHash, IsPublished = x.IsPublished,
        CreationTime = x.CreationTime,
    };

    private static JsonElement Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private async Task<WorkflowSourceVersion> GetVersionEntityAsync(Guid workflowId, int revision) =>
        await _versions.FirstOrDefaultAsync(x => x.WorkflowId == workflowId && x.Revision == revision)
        ?? throw new UserFriendlyException($"工作流版本不存在：{revision}");

    private static WorkflowTextPosition Position(string source, int offset)
    {
        int line = 1, lineStart = 0;
        for (int i = 0; i < Math.Min(offset, source.Length); i++)
            if (source[i] == '\n') { line++; lineStart = i + 1; }
        return new WorkflowTextPosition(offset, line, offset - lineStart + 1);
    }
}
