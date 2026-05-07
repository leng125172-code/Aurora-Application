using System.Threading.Tasks;
using JetBrains.Annotations;

namespace Volo.Abp.Features;

public interface IFeatureChecker
{
    Task<string?> GetOrNullAsync([NotNull] string name);

    Task<bool> IsEnabledAsync(string name);
}
