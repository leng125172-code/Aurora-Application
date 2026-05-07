namespace Lion.AbpPro.TemplateManagement.TextTemplates;

public interface ITextTemplateRepository : IBasicRepository<TextTemplate, Guid>
{
    Task<List<TextTemplate>> GetListAsync(
        string code,
        string name,
        string content,
        DateTime? startDateTime = null,
        DateTime? endDateTime = null,
        int maxResultCount = 10,
        int skipCount = 0
    );

    Task<long> GetCountAsync(
        string code,
        string name,
        string content,
        DateTime? startDateTime = null,
        DateTime? endDateTime = null
    );

    Task<TextTemplate> FindByCodeAsync(string code);
}
