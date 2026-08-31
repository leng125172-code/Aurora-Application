using System.Text.Json;
using AuroraStruct3D.OpenCV.Workflow.Compilation;
using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;
using AuroraStruct3D.OpenCV.Workflow.Scripting;
using AuroraStruct3D.OpenCV.Registry;
using AuroraStruct3D.Workflow.Dtos;
using AuroraStruct3D.Workflow.Runtime;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace AuroraStruct3D.Workflow;

[Authorize(AuroraStruct3D.Permissions.AuroraStruct3DAccessPermissions.Management)]
[Route("api/app/workflow/migration-batches")]
public sealed class WorkflowMigrationBatchAppService : ApplicationService
{
    private readonly IRepository<WorkflowDefinition, Guid> _workflows;
    private readonly IRepository<WorkflowMigrationBatch, Guid> _batches;
    private readonly IRepository<WorkflowMigrationSnapshot, Guid> _snapshots;
    private readonly IWorkflowProgramCache _programCache;
    private readonly IOperatorRegistry _operatorRegistry;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public WorkflowMigrationBatchAppService(
        IRepository<WorkflowDefinition, Guid> workflows,
        IRepository<WorkflowMigrationBatch, Guid> batches,
        IRepository<WorkflowMigrationSnapshot, Guid> snapshots,
        IWorkflowProgramCache programCache,
        IOperatorRegistry operatorRegistry,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _workflows = workflows;
        _batches = batches;
        _snapshots = snapshots;
        _programCache = programCache;
        _operatorRegistry = operatorRegistry;
        _unitOfWorkManager = unitOfWorkManager;
    }

    [HttpGet("preview")]
    public async Task<WorkflowMigrationPreviewDto> PreviewAsync(int batchSize = 100)
    {
        List<WorkflowDefinition> workflows = await GetCandidatesAsync(batchSize);
        List<WorkflowMigrationItemDto> items = [];
        foreach (WorkflowDefinition workflow in workflows)
            items.Add(await PrepareItemAsync(workflow));
        return new WorkflowMigrationPreviewDto
        {
            ScannedCount = items.Count,
            MigratableCount = items.Count(x => x.Success),
            Items = items,
        };
    }

    [HttpPost]
    public async Task<WorkflowMigrationBatchDto> StartAsync(int batchSize = 100)
    {
        WorkflowMigrationBatch batch = new(GuidGenerator.Create());
        batch.Start();
        await _batches.InsertAsync(batch);
        List<WorkflowDefinition> workflows = await GetCandidatesAsync(batchSize);
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

    [HttpPost("{batchId:guid}/items/{workflowId:guid}/rollback")]
    public async Task<WorkflowMigrationBatchDto> RollbackItemAsync(Guid batchId, Guid workflowId)
    {
        WorkflowMigrationBatch batch = await _batches.GetAsync(batchId);
        List<WorkflowMigrationItemDto> items = ReadItems(batch.ResultsJson);
        WorkflowMigrationItemDto item = items.FirstOrDefault(x => x.WorkflowId == workflowId)
            ?? throw new UserFriendlyException("迁移批次中不存在该工作流。");
        if (!item.Success)
            throw new UserFriendlyException("失败的迁移项没有可回滚的新版本。");
        await RestoreAsync(batchId, item);
        item.RolledBack = true;
        batch.Complete(items.Count, items.Count(x => x.Success && !x.RolledBack),
            items.Count(x => !x.Success), JsonSerializer.Serialize(items));
        await _batches.UpdateAsync(batch, autoSave: true);
        return Map(batch);
    }

    [HttpPost("{batchId:guid}/rollback")]
    public async Task<WorkflowMigrationBatchDto> RollbackBatchAsync(Guid batchId)
    {
        WorkflowMigrationBatch batch = await _batches.GetAsync(batchId);
        List<WorkflowMigrationItemDto> items = ReadItems(batch.ResultsJson);
        foreach (WorkflowMigrationItemDto item in items.Where(x => x.Success && !x.RolledBack))
        {
            try
            {
                await RestoreAsync(batchId, item);
                item.RolledBack = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                item.Diagnostic = "回滚失败：" + ex.Message;
            }
        }
        batch.Complete(items.Count, items.Count(x => x.Success && !x.RolledBack),
            items.Count(x => !x.Success || (x.Success && !x.RolledBack)), JsonSerializer.Serialize(items));
        await _batches.UpdateAsync(batch, autoSave: true);
        return Map(batch);
    }

    private async Task<WorkflowMigrationBatchDto> ExecuteAsync(
        WorkflowMigrationBatch batch,
        IReadOnlyList<WorkflowDefinition> workflows,
        IReadOnlyList<WorkflowMigrationItemDto> completedItems)
    {
        List<WorkflowMigrationItemDto> items = [.. completedItems];
        foreach (WorkflowDefinition workflow in workflows)
        {
            WorkflowMigrationItemDto item = await PrepareItemAsync(workflow);
            try
            {
                using IUnitOfWork unitOfWork = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
                if (!item.Success) throw new InvalidOperationException(item.Diagnostic);
                GraphDataModel graph = JsonSerializer.Deserialize<GraphDataModel>(item.AfterGraphData!)!;
                string source = item.AfterSourceCode!;
                string contentHash = WorkflowProgramHash.ComputeContentHash(source);
                string semanticHash = WorkflowProgramHash.ComputeSemanticHash(source);
                string operatorHash = WorkflowProgramHash.ComputeOperatorContractHash();
                string programHash = WorkflowProgramHash.ComputeProgramHash(source);
                await _programCache.GetOrAddAsync(programHash, source);
                WorkflowMigrationSnapshot snapshot = new(
                    GuidGenerator.Create(), batch.Id, workflow);
                await _snapshots.InsertAsync(snapshot, autoSave: true);
                item.SnapshotId = snapshot.Id;
                workflow.UpdateSource(
                    workflow.Name, source, item.AfterGraphData!, contentHash, programHash,
                    CSharpWorkflowScript.LanguageVersion, semanticHash, operatorHash);
                await _workflows.UpdateAsync(workflow, autoSave: true);
                await unitOfWork.CompleteAsync();
                item.Success = true;
                item.AfterContentHash = contentHash;
                item.ProgramHash = programHash;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                item.Success = false;
                item.SnapshotId = null;
                item.Diagnostic = ex.Message;
            }
            // 原始正文只保存在独立不可变快照中，批次 JSON 仅保存报告和快照引用。
            item.BeforeSourceCode = null;
            item.BeforeGraphData = null;
            item.AfterSourceCode = null;
            item.AfterGraphData = null;
            items.Add(item);
        }
        int migrated = items.Count(x => x.Success);
        batch.Complete(items.Count, migrated, items.Count - migrated, JsonSerializer.Serialize(items));
        await _batches.UpdateAsync(batch, autoSave: true);
        return Map(batch);
    }

    private async Task<List<WorkflowDefinition>> GetCandidatesAsync(int batchSize)
    {
        int take = Math.Clamp(batchSize, 1, 1000);
        List<WorkflowDefinition> candidates = await AsyncExecuter.ToListAsync(
            (await _workflows.GetQueryableAsync()).OrderBy(x => x.CreationTime).Take(5000));
        List<WorkflowDefinition> result = [];
        foreach (WorkflowDefinition workflow in candidates)
        {
            if (workflow.SourceCode is null or ""
                || workflow.LanguageVersion < CSharpWorkflowScript.LanguageVersion
                || await HasLegacyInspectionBindingsAsync(workflow.GraphData))
                result.Add(workflow);
            if (result.Count == take) break;
        }
        return result;
    }

    private async Task<bool> HasLegacyInspectionBindingsAsync(string graphData)
    {
        try
        {
            (_, GraphDataModel graph) = WorkflowGraphCompiler.ParseContent(graphData);
            foreach (NodeModel node in EnumerateNodes(graph))
            {
                if (!Guid.TryParse(node.Type, out Guid operatorId)
                    || node.Properties?.OutputBindings is not { } bindings) continue;
                OperatorParametersDescriptor? descriptor = await _operatorRegistry.GetParametersAsync(operatorId);
                ParameterDescriptor? result = descriptor?.Outputs.FirstOrDefault(x => x.ParameterName == "result");
                HashSet<string> currentPorts = (descriptor?.Outputs ?? [])
                    .Select(x => x.ParameterName ?? string.Empty)
                    .ToHashSet(StringComparer.Ordinal);
                if (result?.JsonSchema?.Contains("\"resultCode\"", StringComparison.Ordinal) == true
                    && bindings.Keys.Any(x => !currentPorts.Contains(x)))
                    return true;
            }
        }
        catch (Exception ex) when (ex is FormatException or JsonException) { }
        return false;
    }

    private async Task<WorkflowMigrationItemDto> PrepareItemAsync(WorkflowDefinition workflow)
    {
        WorkflowMigrationItemDto item = new()
        {
            WorkflowId = workflow.Id, ProjectId = workflow.ProjectId,
            WorkflowName = workflow.Name, BeforeContentHash = workflow.SourceHash,
            BeforeSourceCode = workflow.SourceCode, BeforeGraphData = workflow.GraphData,
            BeforeLanguageVersion = workflow.LanguageVersion,
            BeforeSemanticHash = workflow.SemanticHash, BeforeProgramHash = workflow.ProgramHash,
            BeforeOperatorContractHash = workflow.OperatorContractHash,
        };
        try
        {
            (_, GraphDataModel graph) = WorkflowGraphCompiler.ParseContent(workflow.GraphData);
            item.PortChanges = await WorkflowAppService.UpgradeLegacyResultBindingsAsync(graph, _operatorRegistry);
            item.AfterGraphData = JsonSerializer.Serialize(graph, GraphJson.Options);
            item.AfterSourceCode = CSharpWorkflowScript.Generate(workflow.Name, graph);
            CSharpWorkflowScript.Parse(item.AfterSourceCode);
            WorkflowValidationResult validation = await new WorkflowGraphValidator(_operatorRegistry)
                .ValidateAsync(graph);
            if (validation.HasErrors)
                throw new InvalidOperationException(string.Join(Environment.NewLine,
                    validation.Errors.Select(x => $"{x.NodeId}: {x.Message}")));
            item.Changes.Add("语言版本升级为 V3");
            if (!string.Equals(item.BeforeGraphData, item.AfterGraphData, StringComparison.Ordinal))
                item.Changes.Add("旧检测端口绑定升级为强类型结果成员路径");
            item.Success = true;
            item.AfterContentHash = WorkflowProgramHash.ComputeContentHash(item.AfterSourceCode);
            item.ProgramHash = WorkflowProgramHash.ComputeProgramHash(item.AfterSourceCode);
            item.SourceChanges = BuildTextChanges(item.BeforeSourceCode, item.AfterSourceCode);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            item.Success = false;
            item.Diagnostic = ex.Message;
        }
        return item;
    }

    private async Task RestoreAsync(Guid batchId, WorkflowMigrationItemDto item)
    {
        if (!item.Success || item.RolledBack) return;
        WorkflowMigrationSnapshot snapshot = item.SnapshotId is { } snapshotId
            ? await _snapshots.GetAsync(snapshotId)
            : (await AsyncExecuter.FirstOrDefaultAsync((await _snapshots.GetQueryableAsync())
                .Where(x => x.BatchId == batchId && x.WorkflowId == item.WorkflowId)))
                ?? throw new UserFriendlyException("找不到该迁移项的原始快照，无法安全回滚。");
        if (snapshot.BatchId != batchId || snapshot.WorkflowId != item.WorkflowId)
            throw new UserFriendlyException("迁移快照与批次或工作流不匹配，已拒绝回滚。");
        if (snapshot.RolledBackAt is not null) { item.RolledBack = true; return; }
        WorkflowDefinition workflow = await _workflows.GetAsync(item.WorkflowId);
        if (!string.Equals(workflow.ProgramHash, item.ProgramHash, StringComparison.Ordinal)
            || !string.Equals(workflow.SourceHash, item.AfterContentHash, StringComparison.Ordinal)
            || workflow.LanguageVersion != CSharpWorkflowScript.LanguageVersion)
            throw new UserFriendlyException("工作流在迁移后已被修改，为避免覆盖新版本，回滚已拒绝。");
        workflow.RestoreSourceSnapshot(snapshot.WorkflowName, snapshot.SourceCode,
            snapshot.GraphData, snapshot.SourceHash, snapshot.ProgramHash,
            snapshot.LanguageVersion, snapshot.SemanticHash, snapshot.OperatorContractHash);
        await _workflows.UpdateAsync(workflow, autoSave: true);
        snapshot.MarkRolledBack();
        await _snapshots.UpdateAsync(snapshot, autoSave: true);
    }

    private static List<WorkflowMigrationTextChangeDto> BuildTextChanges(string? before, string after)
    {
        string[] oldLines = (before ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        string[] newLines = after.Replace("\r\n", "\n").Split('\n');
        int prefix = 0;
        while (prefix < oldLines.Length && prefix < newLines.Length
            && oldLines[prefix] == newLines[prefix]) prefix++;
        int suffix = 0;
        while (suffix < oldLines.Length - prefix && suffix < newLines.Length - prefix
            && oldLines[^(suffix + 1)] == newLines[^(suffix + 1)]) suffix++;
        if (prefix == oldLines.Length && prefix == newLines.Length) return [];
        return [new WorkflowMigrationTextChangeDto
        {
            StartLine = prefix + 1,
            DeleteLineCount = oldLines.Length - prefix - suffix,
            NewLines = newLines.Skip(prefix).Take(newLines.Length - prefix - suffix).ToList(),
        }];
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

    private static IEnumerable<NodeModel> EnumerateNodes(GraphDataModel graph)
    {
        foreach (NodeModel node in graph.Nodes)
        {
            yield return node;
            if (node.Properties?.InnerGraphData is { } inner)
                foreach (NodeModel child in EnumerateNodes(inner)) yield return child;
        }
    }
}
