namespace Lion.AbpPro.CodeManagement.EntityFrameworkCore
{
    [DependsOn(typeof(CodeManagementDomainModule), typeof(AbpEntityFrameworkCoreModule))]
    public class CodeManagementEntityFrameworkCoreModule : AbpModule
    {
        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            context.Services.AddAbpDbContext<CodeManagementDbContext>(options =>
            {
                /* Add custom repositories here. Example:
                 * options.AddRepository<Question, EfCoreQuestionRepository>();
                 */
            });
        }
    }
}
