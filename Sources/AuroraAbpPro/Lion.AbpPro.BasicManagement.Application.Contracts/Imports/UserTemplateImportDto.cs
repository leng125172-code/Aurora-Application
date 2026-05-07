using Magicodes.ExporterAndImporter.Core;

namespace Lion.AbpPro.BasicManagement.Imports;

public class UserTemplateImportDto
{
    [ImporterHeader(Name = "用户名", IsAllowRepeat = false)]
    [MaxLength(30, ErrorMessage = "用户名超出最大限制,请修改!")]
    [Required(ErrorMessage = "用户名不能为空")]
    public string UserName { get; set; }

    [ImporterHeader(Name = "名称")]
    [MaxLength(30, ErrorMessage = "名称超出最大限制,请修改!")]
    [Required(ErrorMessage = "名称不能为空")]
    public string Name { get; set; }

    [ImporterHeader(Name = "邮箱")]
    [Required(ErrorMessage = "邮箱不能为空")]
    [EmailAddress(ErrorMessage = "邮箱格式不正确")]
    public string Email { get; set; }

    [ImporterHeader(Name = "手机号")]
    [Required(ErrorMessage = "手机号不能为空")]
    public string Phone { get; set; }

    [ImporterHeader(Name = "密码")]
    [Required(ErrorMessage = "密码不能为空")]
    public string Password { get; set; }

    [ImporterHeader(Name = "角色")]
    public string RoleNames { get; set; }
}
