using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Volo.Abp.TextTemplating;

public class NullIDynamicTemplateDefinitionStore
    : IDynamicTemplateDefinitionStore,
        ISingletonDependency
{
    private static readonly Task<TemplateDefinition?> CachedTemplateResult = Task.FromResult(
        (TemplateDefinition?)null
    );

    private static readonly Task<IReadOnlyList<TemplateDefinition>> CachedTemplatesResult =
        Task.FromResult(
            (IReadOnlyList<TemplateDefinition>)Array.Empty<TemplateDefinition>().ToImmutableList()
        );

    public Task<TemplateDefinition> GetAsync(string name)
    {
        return CachedTemplateResult!;
    }

    public Task<IReadOnlyList<TemplateDefinition>> GetAllAsync()
    {
        return CachedTemplatesResult;
    }

    public Task<TemplateDefinition?> GetOrNullAsync(string name)
    {
        return CachedTemplateResult;
    }
}
