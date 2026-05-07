using Magicodes.ExporterAndImporter.Core;

namespace Lion.AbpPro.TemplateManagement
{
    [DependsOn(
        typeof(TemplateManagementDomainModule),
        typeof(TemplateManagementApplicationContractsModule),
        typeof(AbpDddApplicationModule)
    )]
    public class TemplateManagementApplicationModule : AbpModule
    {
        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            ConfigureMagicodes(context);
        }

        /// <summary>
        /// 配置Magicodes.IE
        /// Excel导入导出
        /// </summary>
        private void ConfigureMagicodes(ServiceConfigurationContext context)
        {
            context.Services.AddTransient<IExporter, ExcelExporter>();
            context.Services.AddTransient<IExcelExporter, ExcelExporter>();
        }
    }
}
