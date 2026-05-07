using Lion.AbpPro.CodeManagement.EntityModels.Aggregates;

namespace Lion.AbpPro.CodeManagement.EntityFrameworkCore.EntityModels;

public static class EntityModelEfCoreQueryableExtensions
{
    public static IQueryable<EntityModel> IncludeDetails(
        this IQueryable<EntityModel> queryable,
        bool include = true
    )
    {
        return !include ? queryable : queryable.Include(x => x.EntityModelProperties);
    }
}
