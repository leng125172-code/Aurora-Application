namespace Lion.AbpPro.MasterDataManagement.EntityFrameworkCore
{
    [DependsOn(typeof(MasterDataManagementDomainModule), typeof(AbpEntityFrameworkCoreModule))]
    public class MasterDataManagementEntityFrameworkCoreModule : AbpModule
    {
        public override void ConfigureServices(ServiceConfigurationContext context)
        {
            context.Services.AddAbpDbContext<MasterDataManagementDbContext>(options =>
            {
                /* Add custom repositories here. Example:
                 * options.AddRepository<Question, EfCoreQuestionRepository>();
                 */
            });
        }
    }
}
