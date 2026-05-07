using Lion.AbpPro.CodeManagement.EnumTypes.Aggregates;

namespace Lion.AbpPro.CodeManagement.EntityFrameworkCore.EnumTypes;

public static class EnumTypeEfCoreQueryableExtensions
{
    public static IQueryable<EnumType> IncludeDetails(
        this IQueryable<EnumType> queryable,
        bool include = true
    )
    {
        return !include ? queryable : queryable.Include(x => x.EnumTypeProperties);
    }
}
