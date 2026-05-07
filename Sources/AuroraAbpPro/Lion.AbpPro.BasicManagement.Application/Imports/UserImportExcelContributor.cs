using Lion.AbpPro.ImportExport.Import.Excel;
using Magicodes.ExporterAndImporter.Core.Models;
using Microsoft.AspNetCore.Identity;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace Lion.AbpPro.BasicManagement.Imports;

public class UserImportExcelContributor : ImportExcelContributor<UserTemplateImportDto>
{
    public override string Name => "导入用户";
    protected IOptions<IdentityOptions> IdentityOptions { get; }

    protected IdentityUserManager UserManager { get; }
    protected ICurrentTenant CurrentTenant { get; }

    public UserImportExcelContributor(
        IOptions<IdentityOptions> identityOptions,
        IdentityUserManager userManager,
        ICurrentTenant currentTenant
    )
    {
        IdentityOptions = identityOptions;
        UserManager = userManager;
        CurrentTenant = currentTenant;
    }

    protected override async Task<ImportExportResult> ImportAsync(
        Guid id,
        ICollection<UserTemplateImportDto> list
    )
    {
        var result = new ImportExportResult();

        var index = 2;

        Logger.LogInformation($"开始导入用户, 数量:{list.Count}");
        var hasError = false;
        foreach (var item in list)
        {
            try
            {
                await CreateUserAsync(item);
            }
            catch (Exception e)
            {
                var error = new DataRowErrorInfo();
                // todo 这里如果某一行有错误，可以把错误信息添加到error.FieldErrors中
                error.RowIndex = index; // 第几行
                error.FieldErrors.Add("用户名", e.Message); // 错误信息放到用户名那一列，错误信息为测试错误
                result.RowErrors.Add(error);
                hasError = true;
            }

            index++;
        }

        result.Success = !hasError;
        return result;
    }

    private async Task CreateUserAsync(UserTemplateImportDto item)
    {
        var user = new IdentityUser(
            GuidGenerator.Create(),
            item.UserName,
            item.Email,
            CurrentTenant.Id
        );
        user.Name = item.Name;

        (await UserManager.CreateAsync(user, item.Password)).CheckErrors();
        (await UserManager.SetPhoneNumberAsync(user, item.Phone)).CheckErrors();
        if (item.RoleNames.IsNotNullOrWhiteSpace())
        {
            (
                await UserManager.SetRolesAsync(
                    user,
                    item.RoleNames.Split(',', StringSplitOptions.RemoveEmptyEntries)
                )
            ).CheckErrors();
        }
    }
}
