using Lion.AbpPro.ImportExport;
using Magicodes.ExporterAndImporter.Core;
using Magicodes.ExporterAndImporter.Excel;
using Volo.Abp.BlobStoring;

namespace Lion.AbpPro.ImportExportManagement
{
    [DependsOn(
        typeof(AbpDddDomainModule),
        typeof(ImportExportManagementDomainSharedModule),
        typeof(AbpCachingModule),
        typeof(AbpProImportExportModule)
    )]
    public class ImportExportManagementDomainModule : AbpModule { }
}
