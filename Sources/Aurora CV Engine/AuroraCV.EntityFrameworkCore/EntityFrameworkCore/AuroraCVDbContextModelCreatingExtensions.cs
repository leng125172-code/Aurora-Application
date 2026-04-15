using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AuroraCV.EntityFrameworkCore;

public static class AuroraCVDbContextModelCreatingExtensions
{
    public static void ConfigureAuroraCV(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));
    }
}
