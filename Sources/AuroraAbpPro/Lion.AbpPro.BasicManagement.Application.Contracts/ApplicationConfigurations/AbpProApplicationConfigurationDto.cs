namespace Lion.AbpPro.BasicManagement.ApplicationConfigurations;

public class AbpProApplicationConfigurationDto
{
    public ApplicationOidcConfigurationDto OidcConfiguration { get; set; }

    public MultiTenancyInfoDto MultiTenancy { get; set; }

    public EncryptionConfiguration Encryption { get; set; }
}
