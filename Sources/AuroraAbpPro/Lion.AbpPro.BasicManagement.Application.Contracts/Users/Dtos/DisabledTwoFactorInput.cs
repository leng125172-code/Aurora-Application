namespace Lion.AbpPro.BasicManagement.Users.Dtos;

public class DisabledTwoFactorInput
{
    /// <summary>
    /// 验证码
    /// </summary>
    [Required]
    public string Code { get; set; }
}
