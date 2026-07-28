using System.Text.Json;
using AuroraStruct3D.OpenCV.Workflow.Compilation;
using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;
using AuroraStruct3D.OpenCV.Workflow.Scripting;
using AuroraStruct3D.Workflow.Dtos;
using AuroraStruct3D.Workflow.Runtime;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;

namespace AuroraStruct3D.Workflow;

[Route("api/app/workflow/migration-batches")]
public sealed class WorkflowMigrationBatchAppService : ApplicationService
{
    private readonly IRepository<WorkflowDefinition, Guid> _workflows;
    private readonly IRepository<WorkflowMigrationBatch, Guid> _batches;
    private readonly IWorkflowProgramCache _programCache;

    public WorkflowMigrationBatchAppService(
        IRepository<WorkflowDefinition, Guid> workflows,
        IRepository<WorkflowMigrationBatch, Guid> batches,
        IWorkflowProgramCache programCache)
    {
        _workflows = workflows;
        _batches = batches;
        _programCache = programCache;
    }

    [HttpPost]
    public async Task<WorkflowMigrationBatchDto> StartAsync(int batchSize = 100)
    {
        WorkflowMigrationBatch batch = new(GuidGenerator.Create());
        batch.Start();
        await _batches.InsertAsync(batch);
        List<WorkflowDefinition> workflows = await AsyncExecuter.ToListAsync(
            (await _workflows.GetQueryableAsync())
                .Where(x => x.SourceCode == null || x.SourceCode == string.Empty)
                .OrderBy(x => x.CreationTime)
                .Take(Math.Clamp(batchSize, 1, 1000)));
        return await ExecuteAsync(batch, workflows, []);
    }

    [HttpGet("{batchId:guid}")]
    public async Task<WorkflowMigrationBatchDto> GetAsync(Guid batchId) =>
        Map(await _batches.GetAsync(batchId));

    [HttpPost("{batchId:guid}/retry")]
    public async Task<WorkflowMigrationBatchDto> RetryAsync(Guid batchId)
    {
        WorkflowMigrationBatch batch = await _batches.GetAsync(batchId);
        List<Guid> failedIds = ReadItems(batch.ResultsJson)
            .Where(x => !x.Success).Select(x => x.WorkflowId).Distinct().ToList();
        if (failedIds.Count == 0)
            return Map(batch);
        batch.IncrementRetry();
        batch.Start();
        List<WorkflowDefinition> workflows = await AsyncExecuter.ToListAsync(
            (await _workflows.GetQueryableAsync()).Where(x => failedIds.Contains(x.Id)));
        List<WorkflowMigrationItemDto> completed = ReadItems(batch.ResultsJson)
            .Where(x => x.Success).ToList();
        return await ExecuteAsync(batch, workflows, completed);
    }

    private async Task<WorkflowMigrationBatchDto> ExecuteAsync(
        WorkflowMigrationBatch batch,
        IReadOnlyList<WorkflowDefinition> workflows,
        IReadOnlyList<WorkflowMigrationItemDto> completedItems)
    {
        List<WorkflowMigrationItemDto> items = [.. completedItems];
        foreach (WorkflowDefinition workflow in workflows)
        {
            WorkflowMigrationItemDto item = new()
            {
                WorkflowId = workflow.Id,
                ProjectId = workflow.ProjectId,
                WorkflowName = workflow.Name,
                BeforeContentHash = workflow.SourceHash,
            };
            try
            {
                (_, GraphDataModel graph) = WorkflowGraphCompiler.ParseContent(workflow.GraphData);
                string source = CSharpWorkflowScript.Generate(workflow.Name, graph);
                string contentHash = WorkflowProgramHash.ComputeContentHash(source);
                string semanticHash = WorkflowProgramHash.ComputeSemanticHash(source);
                string operatorHash = WorkflowProgramHash.ComputeOperatorContractHash();
                string programHash = WorkflowProgramHash.ComputeProgramHash(source);
                await _programCache.GetOrAddAsync(programHash, source);
                workflow.UpdateSource(
                    workflow.Name, source, workflow.GraphData, contentHash, programHash,
                    CSharpWorkflowScript.LanguageVersion, semanticHash, operatorHash);
                await _workflows.UpdateAsync(workflow);
                item.Success = true;
                item.AfterContentHash = contentHash;
                item.ProgramHash = programHash;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                item.Diagnostic = ex.Message;
            }
            items.Add(item);
        }
        int migrated = items.Count(x => x.Success);
        batch.Complete(items.Count, migrated, items.Count - migrated, JsonSerializer.Serialize(items));
        await _batches.UpdateAsync(batch, autoSave: true);
        return Map(batch);
    }

    private static WorkflowMigrationBatchDto Map(WorkflowMigrationBatch batch) => new()
    {
        BatchId = batch.Id,
        Status = batch.Status,
        ScannedCount = batch.ScannedCount,
        MigratedCount = batch.MigratedCount,
        FailedCount = batch.FailedCount,
        RetryCount = batch.RetryCount,
        CreationTime = batch.CreationTime,
        CompletedAt = batch.CompletedAt,
        Items = ReadItems(batch.ResultsJson),
    };

    private static List<WorkflowMigrationItemDto> ReadItems(string json) =>
        JsonSerializer.Deserialize<List<WorkflowMigrationItemDto>>(json) ?? [];
}
