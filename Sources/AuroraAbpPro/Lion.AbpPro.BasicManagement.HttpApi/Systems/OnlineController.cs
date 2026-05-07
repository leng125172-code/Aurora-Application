namespace Lion.AbpPro.BasicManagement.Systems;

[Route("Online")]
public class OnlineController : BasicManagementController, IOnlineAppService
{
    private readonly IOnlineAppService _onlineAppService;

    public OnlineController(IOnlineAppService onlineAppService)
    {
        _onlineAppService = onlineAppService;
    }

    [HttpPost("Page")]
    [SwaggerOperation(summary: "分页获取在线用户", Tags = new[] { "Online" })]
    public async Task<PagedResultDto<PageOnlineUserOutput>> PageAsync(PagingOnlineUserInput input)
    {
        return await _onlineAppService.PageAsync(input);
    }

    [HttpPost("ForceOut")]
    [SwaggerOperation(summary: "强制下线用户", Tags = new[] { "Online" })]
    public async Task ForceOutAsync(IdInput input)
    {
        await _onlineAppService.ForceOutAsync(input);
    }
}
