using System.Text.RegularExpressions;
using AuroraStruct3D.Workflow;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.DependencyInjection;

namespace AuroraStruct3D.Jobs;

public class WorkflowBlobReferenceScanner : ITransientDependency
{
    private readonly IRepository<WorkflowDefinition, Guid> _workflowRepository;

    private static readonly Regex BlobNameRegex = new(
        @"([a-f0-9]{32}/[\w-]+(?:\.[a-zA-Z]{3,4})?|workflow-image/[\w-]+(?:\.[a-zA-Z]{3,4})?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly string[] BlobKeyWords = { "blob_name", "blobName", "download_url", "downloadUrl", "preview_url", "previewUrl" };

    public WorkflowBlobReferenceScanner(IRepository<WorkflowDefinition, Guid> workflowRepository)
    {
        _workflowRepository = workflowRepository;
    }

    public async Task<HashSet<string>> GetAllReferencedBlobNamesAsync()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var allWorkflows = await _workflowRepository.GetListAsync();
        foreach (var workflow in allWorkflows)
        {
            var blobs = ExtractBlobNames(workflow.GraphData);
            foreach (var blob in blobs)
            {
                result.Add(blob);
            }
        }

        return result;
    }

    private static HashSet<string> ExtractBlobNames(string json)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }

        foreach (var match in BlobNameRegex.Matches(json))
        {
            if (match is System.Text.RegularExpressions.Match m)
            {
                result.Add(m.Value);
            }
        }

        return result;
    }
}