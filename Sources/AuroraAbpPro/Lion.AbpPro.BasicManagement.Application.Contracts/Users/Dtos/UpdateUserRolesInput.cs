namespace Lion.AbpPro.BasicManagement.Users.Dtos;

public class UpdateUserRolesInput
{
    public Guid UserId { get; set; }

    [Required]
    public List<string> RoleNames { get; set; } = new();
}
