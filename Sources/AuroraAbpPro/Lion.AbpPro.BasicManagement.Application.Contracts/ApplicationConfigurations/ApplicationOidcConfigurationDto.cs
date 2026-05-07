namespace Lion.AbpPro.BasicManagement.ApplicationConfigurations;

public class ApplicationOidcConfigurationDto
{
    public ApplicationOidcConfigurationDto()
    {
        OidcConfiguration = new List<OidcConfiguration>();
    }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; }

    public List<OidcConfiguration> OidcConfiguration { get; set; }
}

public class OidcConfiguration
{
    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 类型
    /// </summary>
    public string Type { get; set; }

    /// <summary>
    /// client_id
    /// </summary>
    public string ClientId { get; set; }

    /// <summary>
    /// 应用名称
    /// </summary>
    public string ClientName { get; set; }

    /// <summary>
    /// 应用图标
    /// </summary>
    public string Icon { get; set; }

    /// <summary>
    /// 认证地址
    /// </summary>
    public string AuthUri { get; set; }
}

public class EncryptionConfiguration
{
    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// 公钥
    /// </summary>
    public string PublicKey { get; set; }
}
