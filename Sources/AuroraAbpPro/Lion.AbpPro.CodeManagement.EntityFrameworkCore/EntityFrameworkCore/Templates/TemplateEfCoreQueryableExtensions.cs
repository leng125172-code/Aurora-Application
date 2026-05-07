using Lion.AbpPro.CodeManagement.Templates.Aggregates;

namespace Lion.AbpPro.CodeManagement.EntityFrameworkCore.Templates;

public static class TemplateEfCoreQueryableExtensions
{
    public static IQueryable<Template> IncludeDetails(
        this IQueryable<Template> queryable,
        bool include = true
    )
    {
        return !include ? queryable : queryable.Include(x => x.TemplateDetails);
    }
}
