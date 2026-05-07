namespace Lion.AbpPro.BasicManagement.ApplicationConfigurations;

public interface IAbpProBasicApplicationConfigurationAppService : IApplicationService
{
    Task<AbpProApplicationConfigurationDto> GetAsync();
}
