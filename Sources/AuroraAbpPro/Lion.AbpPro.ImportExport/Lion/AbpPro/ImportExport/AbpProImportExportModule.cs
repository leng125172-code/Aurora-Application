using Lion.AbpPro.ImportExport.Import;
using Magicodes.ExporterAndImporter.Core;
using Volo.Abp.Modularity;

namespace Lion.AbpPro.ImportExport;

[DependsOn(typeof(AbpBlobStoringModule), typeof(AbpProSignalRModule))]
public class AbpProImportExportModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddTransient<IExporter, ExcelExporter>();
        context.Services.AddTransient<IExcelExporter, ExcelExporter>();
        context.Services.AddTransient<IExcelImporter, ExcelImporter>();
        context.Services.AddTransient<IImporter, ExcelImporter>();
    }
}
