namespace Lion.AbpPro.ImportExport.Import.Excel;

/// <summary>
/// 导入Excel 贡献者
/// </summary>IImportExcelContributor
public interface IImportExcelContributor
{
    /// <summary>
    /// 导入名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 导入Excel
    /// </summary>
    /// <param name="id">导入id</param>
    Task ExecuteAsync(Guid id);

    /// <summary>
    /// 获取导入模板
    /// </summary>
    Task<ExcelTemplateResult> GetTemplateAsync();
}
