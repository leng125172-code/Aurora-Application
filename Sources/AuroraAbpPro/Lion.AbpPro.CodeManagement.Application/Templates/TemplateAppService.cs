using Lion.AbpPro.CodeManagement.Templates.Dto;

namespace Lion.AbpPro.CodeManagement.Templates;

[Authorize(policy: CodeManagementPermissions.CodeManagement.Template.Default)]
public class TemplateAppService : CodeManagementAppService, ITemplateAppService
{
    private readonly TemplateManager _templateManager;

    public TemplateAppService(TemplateManager templateManager)
    {
        _templateManager = templateManager;
    }

    public async Task<List<TemplateDto>> AllAsync()
    {
        return await _templateManager.GetListAsync(maxResultCount: int.MaxValue);
    }

    public async Task<PagedResultDto<TemplateDto>> PageAsync(PageTemplateInput input)
    {
        var result = new PagedResultDto<TemplateDto>();
        var totalCount = await _templateManager.GetCountAsync(input.Filter);
        result.TotalCount = totalCount;
        if (totalCount <= 0)
            return result;

        var list = await _templateManager.GetListAsync(
            input.Filter,
            input.PageSize,
            input.SkipCount,
            false
        );
        result.Items = list;

        return result;
    }

    public async Task<List<TemplateDto>> ListAsync()
    {
        var list = await _templateManager.GetListAsync(null, Int32.MaxValue, 0, false);
        return list;
    }

    [Authorize(policy: CodeManagementPermissions.CodeManagement.Template.Create)]
    public Task CreateAsync(CreateTemplateInput input)
    {
        return _templateManager.CreateAsync(input.Name, input.Remark);
    }

    [Authorize(policy: CodeManagementPermissions.CodeManagement.Template.Update)]
    public Task UpdateAsync(UpdateTemplateInput input)
    {
        return _templateManager.UpdateAsync(input.Id, input.Name, input.Remark);
    }

    [Authorize(policy: CodeManagementPermissions.CodeManagement.Template.Delete)]
    public Task DeleteAsync(DeleteTemplateInput input)
    {
        return _templateManager.DeleteAsync(input.Id);
    }

    public Task CreateDetailAsync(CreateTemplateDetailInput input)
    {
        return _templateManager.CreateDetailAsync(
            input.TemplateId,
            input.TemplateType,
            input.ControlType,
            input.Name,
            input.Description,
            input.Content,
            input.ParentId
        );
    }

    [Authorize(policy: CodeManagementPermissions.CodeManagement.Template.Create)]
    public Task UpdateDetailAsync(UpdateTemplateDetailInput input)
    {
        return _templateManager.UpdateDetailAsync(
            input.TemplateId,
            input.TemplateDetailId,
            input.Name,
            input.Description,
            input.ControlType
        );
    }

    [Authorize(policy: CodeManagementPermissions.CodeManagement.Template.Update)]
    public Task UpdateDetailAsync(UpdateTemplateDetailContentInput input)
    {
        return _templateManager.UpdateDetailAsync(
            input.TemplateId,
            input.TemplateDetailId,
            input.Content
        );
    }

    [Authorize(policy: CodeManagementPermissions.CodeManagement.Template.Delete)]
    public Task DeleteDetailAsync(DeleteTemplateDetailInput input)
    {
        return _templateManager.DeleteDetailAsync(input.TemplateId, input.TemplateDetailId);
    }

    public List<KeyValuePair<string, int>> GetControlTypeAsync()
    {
        return EnumExtensions.GetEntityStringIntKeyValueList<ControlType>();
    }

    public List<KeyValuePair<string, int>> GetTemplateTypeAsync()
    {
        return EnumExtensions.GetEntityStringIntKeyValueList<TemplateType>();
    }

    [Authorize(policy: CodeManagementPermissions.CodeManagement.Template.Update)]
    public Task CopyTemplateAsync(CopyTemplateInput input)
    {
        return _templateManager.CopyAsync(input.Id, input.Name, input.Remark);
    }

    public async Task<List<GetTemplateTreeOutput>> TemplateTreeAsync(GetTemplteTreeInput input)
    {
        var result = await _templateManager.TemplateTreeAsync(input.TemplateId);
        return result.Adapt<List<GetTemplateTreeOutput>>();
    }
}
