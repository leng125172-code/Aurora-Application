namespace Lion.AbpPro.ImportExport.Import.Excel;

public class ImportExportResult
{
    public bool Success { get; set; }

    public List<DataRowErrorInfo> RowErrors { get; set; } = new();
}
