namespace Lion.AbpPro.BasicManagement.Users.Dtos;

public class SetUserActiveInput
{
    public Guid UserId { get; set; }

    public bool IsActive { get; set; }
}
