using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using Volo.Abp.Application.Dtos;

namespace Lion.AbpPro.DynamicMenuManagement.Menus;

[Route("Menus")]
public class MenuController : DynamicMenuManagementController, IMenuAppService
{
    private readonly IMenuAppService _menuAppService;

    public MenuController(IMenuAppService menuAppService)
    {
        _menuAppService = menuAppService;
    }

    [HttpPost("UserMenu")]
    [SwaggerOperation(summary: "获取用户菜单", Tags = new[] { "Menus" })]
    public async Task<List<GetMenuTreeOutput>> GetUserMenuAsync()
    {
        return await _menuAppService.GetUserMenuAsync();
    }

    [HttpPost("Page")]
    [SwaggerOperation(summary: "分页查询菜单", Tags = new[] { "Menus" })]
    public async Task<PagedResultDto<PageMenuOutput>> PageAsync(PageMenuInput input)
    {
        return await _menuAppService.PageAsync(input);
    }

    [HttpPost("Create")]
    [SwaggerOperation(summary: "创建菜单", Tags = new[] { "Menus" })]
    public async Task CreateAsync(CreateMenuInput input)
    {
        await _menuAppService.CreateAsync(input);
    }

    [HttpPost("Update")]
    [SwaggerOperation(summary: "编辑菜单", Tags = new[] { "Menus" })]
    public async Task UpdateAsync(UpdateMenuInput input)
    {
        await _menuAppService.UpdateAsync(input);
    }

    [HttpPost("Delete")]
    [SwaggerOperation(summary: "删除菜单", Tags = new[] { "Menus" })]
    public async Task DeleteAsync(DeleteMenuInput input)
    {
        await _menuAppService.DeleteAsync(input);
    }

    [HttpPost("Tree")]
    [SwaggerOperation(summary: "获取菜单树", Tags = new[] { "Menus" })]
    public async Task<List<GetMenuTreeOutput>> GetMenuTreeAsync()
    {
        return await _menuAppService.GetMenuTreeAsync();
    }
}
