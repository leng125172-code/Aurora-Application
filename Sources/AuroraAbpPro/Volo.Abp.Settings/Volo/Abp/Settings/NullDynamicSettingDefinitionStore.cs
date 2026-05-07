using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;

namespace Volo.Abp.Settings;

public class NullDynamicSettingDefinitionStore
    : IDynamicSettingDefinitionStore,
        ISingletonDependency
{
    private static readonly Task<SettingDefinition?> CachedNullableSettingResult = Task.FromResult(
        (SettingDefinition?)null
    );
    private static readonly Task<SettingDefinition> CachedSettingResult = Task.FromResult(
        (SettingDefinition)null!
    );

    private static readonly Task<IReadOnlyList<SettingDefinition>> CachedSettingsResult =
        Task.FromResult(
            (IReadOnlyList<SettingDefinition>)Array.Empty<SettingDefinition>().ToImmutableList()
        );

    public Task<SettingDefinition> GetAsync(string name)
    {
        return CachedSettingResult;
    }

    public Task<IReadOnlyList<SettingDefinition>> GetAllAsync()
    {
        return CachedSettingsResult;
    }

    public Task<SettingDefinition?> GetOrNullAsync(string name)
    {
        return CachedNullableSettingResult;
    }
}
