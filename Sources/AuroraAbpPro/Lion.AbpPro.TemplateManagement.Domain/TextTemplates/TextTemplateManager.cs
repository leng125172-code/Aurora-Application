using Mapster;

namespace Lion.AbpPro.TemplateManagement.TextTemplates;

public class TextTemplateManager : DomainService
{
    private readonly ITextTemplateRepository _textTemplateRepository;
    private readonly IObjectMapper _objectMapper;
    private readonly ICurrentTenant _currentTenant;

    public TextTemplateManager(
        ITextTemplateRepository textTemplateRepository,
        IObjectMapper objectMapper,
        ICurrentTenant currentTenant
    )
    {
        _textTemplateRepository = textTemplateRepository;
        _objectMapper = objectMapper;
        _currentTenant = currentTenant;
    }

    public async Task<List<TextTemplateDto>> GetListAsync(
        string code = null,
        string name = null,
        string content = null,
        DateTime? startDateTime = null,
        DateTime? endDateTime = null,
        int maxResultCount = 10,
        int skipCount = 0
    )
    {
        var list = await _textTemplateRepository.GetListAsync(
            code,
            name,
            content,
            startDateTime,
            endDateTime,
            maxResultCount,
            skipCount
        );
        return list.Adapt<List<TextTemplateDto>>();
    }

    public async Task<long> GetCountAsync(
        string code,
        string name,
        string content,
        DateTime? startDateTime = null,
        DateTime? endDateTime = null
    )
    {
        return await _textTemplateRepository.GetCountAsync(
            code,
            name,
            content,
            startDateTime,
            endDateTime
        );
    }

    /// <summary>
    /// 创建模板
    /// </summary>
    public async Task<TextTemplateDto> CreateAsync(
        Guid id,
        string name,
        string code,
        string content,
        string cultureName
    )
    {
        var entity = new TextTemplate(id, name, code, content, cultureName, _currentTenant.Id);
        entity = await _textTemplateRepository.InsertAsync(entity);
        return entity.Adapt<TextTemplateDto>();
    }

    /// <summary>
    /// 更新模板
    /// </summary>
    public async Task<TextTemplateDto> UpdateAsync(
        Guid id,
        string name,
        string code,
        string content,
        string cultureName
    )
    {
        var entity = await _textTemplateRepository.FindAsync(id);
        if (entity == null)
            throw new UserFriendlyException($"模板不存在");
        entity.Update(name, code, content, cultureName);
        entity = await _textTemplateRepository.UpdateAsync(entity);
        return entity.Adapt<TextTemplateDto>();
    }

    /// <summary>
    /// 删除模板
    /// </summary>
    public async Task DeleteAsync(Guid id)
    {
        var entity = await _textTemplateRepository.FindAsync(id);
        if (entity == null)
            throw new UserFriendlyException($"模板不存在");
        await _textTemplateRepository.DeleteAsync(entity);
    }

    /// <summary>
    /// 通过code查询模板
    /// </summary>
    public async Task<TextTemplateDto> FindByCodeAsync(string code)
    {
        var entity = await _textTemplateRepository.FindByCodeAsync(code);
        return entity.Adapt<TextTemplateDto>();
    }
}
