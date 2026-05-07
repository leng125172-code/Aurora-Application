using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace Lion.AbpPro.ImportExportManagement.Import;

public class ImportExcelInput
{
    /// <summary>
    /// 导入贡献值名称
    /// </summary>
    [Required(ErrorMessage = "导入贡献者名称不能为空")]
    public string ContributorName { get; set; }

    /// <summary>
    /// 导入文件
    /// </summary>
    public IFormFile File { get; set; }

    public string Remark { get; set; }
}
