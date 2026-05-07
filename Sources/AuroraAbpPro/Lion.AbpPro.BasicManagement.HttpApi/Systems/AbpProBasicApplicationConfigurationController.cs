using Lion.AbpPro.BasicManagement.ApplicationConfigurations;

namespace Lion.AbpPro.BasicManagement.Systems;

public class AbpProBasicApplicationConfigurationController
    : BasicManagementController,
        IAbpProBasicApplicationConfigurationAppService
{
    private readonly IAbpProBasicApplicationConfigurationAppService _abpProBasicApplicationConfigurationAppService;

    public AbpProBasicApplicationConfigurationController(
        IAbpProBasicApplicationConfigurationAppService abpProBasicApplicationConfigurationAppService
    )
    {
        _abpProBasicApplicationConfigurationAppService =
            abpProBasicApplicationConfigurationAppService;
    }

    [HttpGet("api/app/abp-pro-basic-application-configuration")]
    public Task<AbpProApplicationConfigurationDto> GetAsync()
    {
        return _abpProBasicApplicationConfigurationAppService.GetAsync();
    }
}
