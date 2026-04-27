using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AuroraStruct3D.EntityFrameworkCore;

public static class AuroraStruct3DDbContextModelCreatingExtensions
{
    public static void ConfigureAuroraStruct3D(this ModelBuilder builder)
    {
        Check.NotNull(builder, nameof(builder));
    }
}