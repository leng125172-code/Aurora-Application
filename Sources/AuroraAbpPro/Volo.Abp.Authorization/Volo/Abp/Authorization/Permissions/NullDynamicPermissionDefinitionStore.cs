using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Volo.Abp.Authorization.Permissions;

public class NullDynamicPermissionDefinitionStore
    : IDynamicPermissionDefinitionStore,
        ISingletonDependency
{
    private static readonly Task<PermissionDefinition?> CachedPermissionResult = Task.FromResult(
        (PermissionDefinition?)null
    );

    private static readonly Task<IReadOnlyList<PermissionDefinition>> CachedPermissionsResult =
        Task.FromResult(
            (IReadOnlyList<PermissionDefinition>)
                Array.Empty<PermissionDefinition>().ToImmutableList()
        );

    private static readonly Task<PermissionDefinition?> CachedResourcePermissionResult =
        Task.FromResult((PermissionDefinition?)null);

    private static readonly Task<
        IReadOnlyList<PermissionDefinition>
    > CachedResourcePermissionsResult = Task.FromResult(
        (IReadOnlyList<PermissionDefinition>)Array.Empty<PermissionDefinition>().ToImmutableList()
    );

    private static readonly Task<IReadOnlyList<PermissionGroupDefinition>> CachedGroupsResult =
        Task.FromResult(
            (IReadOnlyList<PermissionGroupDefinition>)
                Array.Empty<PermissionGroupDefinition>().ToImmutableList()
        );

    public Task<PermissionDefinition?> GetOrNullAsync(string name)
    {
        return CachedPermissionResult;
    }

    public Task<IReadOnlyList<PermissionDefinition>> GetPermissionsAsync()
    {
        return CachedPermissionsResult;
    }

    public Task<PermissionDefinition?> GetResourcePermissionOrNullAsync(
        string resourceName,
        string name
    )
    {
        return CachedResourcePermissionResult;
    }

    public Task<IReadOnlyList<PermissionDefinition>> GetResourcePermissionsAsync()
    {
        return CachedResourcePermissionsResult;
    }

    public Task<IReadOnlyList<PermissionGroupDefinition>> GetGroupsAsync()
    {
        return CachedGroupsResult;
    }
}
