namespace Lion.AbpPro.DynamicMenuManagement.EntityFrameworkCore
{
    [DependsOn(typeof(DynamicMenuManagementDomainModule), typeof(AbpEntityFrameworkCoreModule))]
    public class DynamicMenuManagementEntityFrameworkCoreModule : AbpModule
    {
        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            context.Services.AddAbpDbContext<DynamicMenuManagementDbContext>(options =>
            {
                /* Add custom repositories here. Example:
                 * options.AddRepository<Question, EfCoreQuestionRepository>();
                 */
            });
        }
    }
}
