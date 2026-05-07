namespace Lion.AbpPro.TemplateManagement.EntityFrameworkCore
{
    [DependsOn(typeof(TemplateManagementDomainModule), typeof(AbpEntityFrameworkCoreModule))]
    public class TemplateManagementEntityFrameworkCoreModule : AbpModule
    {
        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            context.Services.AddAbpDbContext<TemplateManagementDbContext>(options =>
            {
                /* Add custom repositories here. Example:
                 * options.AddRepository<Question, EfCoreQuestionRepository>();
                 */
            });
        }
    }
}
