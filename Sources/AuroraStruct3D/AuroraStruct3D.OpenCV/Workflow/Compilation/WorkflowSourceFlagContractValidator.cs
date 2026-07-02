using AuroraStruct3D.OpenCV.Workflow.Compilation.Model;

namespace AuroraStruct3D.OpenCV.Workflow.Compilation;

/// <summary>
/// Validates strict source-flag contract for workflow node properties.
/// </summary>
public static class WorkflowSourceFlagContractValidator
{
    public static List<string> Validate(GraphDataModel graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        List<string> errors = new();
        ValidateScope(graph, errors);
        return errors;
    }

    private static void ValidateScope(GraphDataModel graph, List<string> errors)
    {
        foreach (NodeModel node in graph.Nodes)
        {
            NodePropertiesModel? properties = node.Properties;
            if (properties is null)
            {
                continue;
            }

            ValidateSection(
                node.Id,
                "params",
                properties.Params?.Keys,
                properties.ParamSources,
                errors
            );
            ValidateSection(
                node.Id,
                "inputBindings",
                properties.InputBindings?.Keys,
                properties.InputBindingSources,
                errors
            );
            ValidateSection(
                node.Id,
                "outputBindings",
                properties.OutputBindings?.Keys,
                properties.OutputBindingSources,
                errors
            );

            if (properties.InnerGraphData is not null)
            {
                ValidateScope(properties.InnerGraphData, errors);
            }
        }
    }

    private static void ValidateSection(
        string nodeId,
        string sectionName,
        IEnumerable<string>? valueKeys,
        Dictionary<string, string>? sourceMap,
        List<string> errors
    )
    {
        HashSet<string> keys = (valueKeys ?? Enumerable.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);

        if (keys.Count == 0)
        {
            if (sourceMap is { Count: > 0 })
            {
                errors.Add(
                    $"Node '{nodeId}' section '{sectionName}' has no values but source map has entries."
                );
            }

            return;
        }

        if (sourceMap is null)
        {
            errors.Add($"Node '{nodeId}' section '{sectionName}' is missing required source map.");
            return;
        }

        HashSet<string> sourceKeys = sourceMap
            .Keys.Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.Ordinal);

        if (!keys.SetEquals(sourceKeys))
        {
            string missing = string.Join(
                ", ",
                keys.Except(sourceKeys, StringComparer.Ordinal)
                    .OrderBy(x => x, StringComparer.Ordinal)
            );
            string extra = string.Join(
                ", ",
                sourceKeys
                    .Except(keys, StringComparer.Ordinal)
                    .OrderBy(x => x, StringComparer.Ordinal)
            );

            if (!string.IsNullOrWhiteSpace(missing))
            {
                errors.Add(
                    $"Node '{nodeId}' section '{sectionName}' source map is missing keys: {missing}."
                );
            }

            if (!string.IsNullOrWhiteSpace(extra))
            {
                errors.Add(
                    $"Node '{nodeId}' section '{sectionName}' source map has extra keys: {extra}."
                );
            }
        }

        foreach ((string key, string source) in sourceMap)
        {
            if (
                string.Equals(sectionName, "outputBindings", StringComparison.Ordinal)
                && !string.Equals(source, NodeTypeTokens.SourceVariable, StringComparison.Ordinal)
            )
            {
                errors.Add(
                    $"Node '{nodeId}' section '{sectionName}' key '{key}' must use source 'variable'."
                );
                continue;
            }

            if (
                !string.Equals(source, NodeTypeTokens.SourceLiteral, StringComparison.Ordinal)
                && !string.Equals(source, NodeTypeTokens.SourceVariable, StringComparison.Ordinal)
            )
            {
                errors.Add(
                    $"Node '{nodeId}' section '{sectionName}' key '{key}' has invalid source '{source}'. Allowed values: literal, variable."
                );
            }
        }
    }
}
