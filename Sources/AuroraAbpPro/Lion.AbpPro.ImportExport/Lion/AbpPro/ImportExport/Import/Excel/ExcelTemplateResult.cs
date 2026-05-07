namespace Lion.AbpPro.ImportExport.Import.Excel;

public class ExcelTemplateResult
{
    /// <summary>
    /// 导入名称
    /// </summary>
    public string ImportName { get; set; }

    /// <summary>
    /// 下载Url
    /// </summary>
    public string TemplateUrl { get; set; }

    /// <summary>
    /// 下载文件二进制
    /// </summary>
    public byte[] TemplateBytes { get; set; }
}
