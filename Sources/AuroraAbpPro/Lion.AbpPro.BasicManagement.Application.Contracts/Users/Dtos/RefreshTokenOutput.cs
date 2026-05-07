namespace Lion.AbpPro.BasicManagement.Users.Dtos;

public class RefreshTokenOutput
{
    public bool Success { get; set; }

    public string Message { get; set; } = "Success";

    public string Token { get; set; }

    public string RefreshToken { get; set; }
}
