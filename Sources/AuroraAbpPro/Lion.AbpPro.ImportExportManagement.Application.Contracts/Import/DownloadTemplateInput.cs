using System.ComponentModel.DataAnnotations;

namespace Lion.AbpPro.ImportExportManagement.Import;

public class DownloadTemplateInput
{
    [Required(ErrorMessage = "导入贡献者不能为空")]
    public string Contributor { get; set; }
}
