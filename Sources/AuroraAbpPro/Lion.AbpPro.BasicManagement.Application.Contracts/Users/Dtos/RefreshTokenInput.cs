namespace Lion.AbpPro.BasicManagement.Users.Dtos;

public class RefreshTokenInput
{
    public Guid UserId { get; set; }

    [Required]
    public string RefreshToken { get; set; }
}
