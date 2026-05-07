namespace Lion.AbpPro.BasicManagement.Users.Dtos;

public class EnabledTwoFactorInput
{
    /// <summary>
    /// 验证码
    /// </summary>
    [Required]
    public string Code { get; set; }

    /// <summary>
    /// 密钥
    /// </summary>
    [Required]
    public string Secret { get; set; }
}
