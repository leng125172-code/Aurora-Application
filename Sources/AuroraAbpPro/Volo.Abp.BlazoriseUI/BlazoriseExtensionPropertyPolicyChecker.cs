using System.Threading.Tasks;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Features;
using Volo.Abp.GlobalFeatures;
using Volo.Abp.ObjectExtending;

namespace Volo.Abp.BlazoriseUI;

[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(ExtensionPropertyPolicyChecker))]
public class BlazoriseExtensionPropertyPolicyChecker : ExtensionPropertyPolicyChecker
{
    protected IFeatureChecker FeatureChecker { get; }
    protected IPermissionChecker PermissionChecker { get; }

    public BlazoriseExtensionPropertyPolicyChecker(
        IFeatureChecker featureChecker,
        IPermissionChecker permissionChecker
    )
    {
        FeatureChecker = featureChecker;
        PermissionChecker = permissionChecker;
    }

    protected override Task<bool> CheckGlobalFeaturesAsync(string featureName)
    {
        return Task.FromResult<bool>(GlobalFeatureManager.Instance.IsEnabled(featureName));
    }

    protected override async Task<bool> CheckFeaturesAsync(string featureName)
    {
        return await FeatureChecker.IsEnabledAsync(featureName);
    }

    protected override async Task<bool> CheckPermissionsAsync(string permissionName)
    {
        return await PermissionChecker.IsGrantedAsync(permissionName);
    }
}
