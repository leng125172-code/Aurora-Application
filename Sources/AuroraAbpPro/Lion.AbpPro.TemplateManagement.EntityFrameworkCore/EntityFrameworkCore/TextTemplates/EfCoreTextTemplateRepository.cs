using Lion.AbpPro.TemplateManagement.TextTemplates;

namespace Lion.AbpPro.TemplateManagement.EntityFrameworkCore.TextTemplates;

/// <summary>
/// 模板 仓储Ef core 实现
/// </summary>
public class EfCoreTextTemplateRepository
    : EfCoreRepository<ITemplateManagementDbContext, TextTemplate, Guid>,
        ITextTemplateRepository
{
    public EfCoreTextTemplateRepository(
        IDbContextProvider<ITemplateManagementDbContext> dbContextProvider
    )
        : base(dbContextProvider) { }

    public async Task<List<TextTemplate>> GetListAsync(
        string code,
        string name,
        string content,
        DateTime? startDateTime = null,
        DateTime? endDateTime = null,
        int maxResultCount = 10,
        int skipCount = 0
    )
    {
        return await (await GetDbSetAsync())
            .WhereIf(code.IsNotNullOrWhiteSpace(), e => e.Code.Contains(code))
            .WhereIf(content.IsNotNullOrWhiteSpace(), e => e.Content.Contains(content))
            .WhereIf(name.IsNotNullOrWhiteSpace(), e => e.Name.Contains(name))
            .WhereIf(startDateTime.HasValue, e => e.CreationTime >= startDateTime.Value)
            .WhereIf(endDateTime.HasValue, e => e.CreationTime <= endDateTime.Value)
            .OrderByDescending(e => e.CreationTime)
            .PageBy(skipCount, maxResultCount)
            .ToListAsync();
    }

    public async Task<long> GetCountAsync(
        string code,
        string name,
        string content,
        DateTime? startDateTime = null,
        DateTime? endDateTime = null
    )
    {
        return await (await GetDbSetAsync())
            .WhereIf(code.IsNotNullOrWhiteSpace(), e => e.Code.Contains(code))
            .WhereIf(content.IsNotNullOrWhiteSpace(), e => e.Content.Contains(content))
            .WhereIf(name.IsNotNullOrWhiteSpace(), e => e.Name.Contains(name))
            .WhereIf(startDateTime.HasValue, e => e.CreationTime >= startDateTime.Value)
            .WhereIf(endDateTime.HasValue, e => e.CreationTime <= endDateTime.Value)
            .CountAsync();
    }

    public async Task<TextTemplate> FindByCodeAsync(string code)
    {
        return await (await GetDbSetAsync()).FirstOrDefaultAsync(e => e.Code == code);
    }
}
