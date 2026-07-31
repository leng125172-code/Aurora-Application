using System.Collections.Concurrent;
using AuroraStruct3D.OpenCV.Workflow.Scripting;
using Volo.Abp.DependencyInjection;

namespace AuroraStruct3D.Workflow;

public sealed record WorkflowLanguageDocument(
    string ContentHash,
    int DocumentVersion,
    WorkflowSyntaxTree SyntaxTree,
    WorkflowIntermediateRepresentation? IntermediateRepresentation
);

public interface IWorkflowLanguageDocumentService
{
    Task<WorkflowLanguageDocument> GetAsync(
        string sourceCode,
        int documentVersion,
        bool includeSemantic,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// Short-lived, cancellation-aware document cache shared by all language endpoints.
/// Syntax parsing is immediate; semantic IR creation is cached only for valid documents.
/// </summary>
public sealed class WorkflowLanguageDocumentService
    : IWorkflowLanguageDocumentService, ISingletonDependency
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
    private const int Capacity = 128;
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);

    public Task<WorkflowLanguageDocument> GetAsync(
        string sourceCode,
        int documentVersion,
        bool includeSemantic,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        string contentHash = WorkflowProgramHash.ComputeContentHash(sourceCode);
        string key = $"{contentHash}:{documentVersion}:{includeSemantic}";
        DateTime now = DateTime.UtcNow;
        if (_entries.TryGetValue(key, out CacheEntry? cached) && now - cached.LastAccess <= Lifetime)
        {
            cached.LastAccess = now;
            return Task.FromResult(cached.Document);
        }

        WorkflowSyntaxTree syntaxTree = WorkflowSyntaxParser.Parse(sourceCode);
        WorkflowIntermediateRepresentation? ir = null;
        if (includeSemantic && !syntaxTree.Diagnostics.Any(x => x.Severity == "error"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ir = WorkflowSyntaxParser.BuildIr(sourceCode);
        }

        WorkflowLanguageDocument document = new(contentHash, documentVersion, syntaxTree, ir);
        _entries[key] = new CacheEntry(document, now);
        Trim(now);
        return Task.FromResult(document);
    }

    private void Trim(DateTime now)
    {
        foreach ((string key, CacheEntry value) in _entries)
            if (now - value.LastAccess > Lifetime)
                _entries.TryRemove(key, out _);
        if (_entries.Count <= Capacity)
            return;
        foreach (string key in _entries.OrderBy(x => x.Value.LastAccess)
                     .Take(_entries.Count - Capacity).Select(x => x.Key))
            _entries.TryRemove(key, out _);
    }

    private sealed class CacheEntry(WorkflowLanguageDocument document, DateTime lastAccess)
    {
        public WorkflowLanguageDocument Document { get; } = document;
        public DateTime LastAccess { get; set; } = lastAccess;
    }
}
