namespace Lion.AbpPro.ImportExportManagement.EntityFrameworkCore
{
    [DependsOn(typeof(ImportExportManagementDomainModule), typeof(AbpEntityFrameworkCoreModule))]
    public class ImportExportManagementEntityFrameworkCoreModule : AbpModule
    {
        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            context.Services.AddAbpDbContext<ImportExportManagementDbContext>(options =>
            {
                /* Add custom repositories here. Example:
                 * options.AddRepository<Question, EfCoreQuestionRepository>();
                 */
            });
        }
    }
}
