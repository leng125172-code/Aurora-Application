using System;
using Devart.Data.Oracle.Entity;
using JetBrains.Annotations;

namespace Volo.Abp.EntityFrameworkCore;

public static class AbpDbContextOptionsOracleDevartExtensions
{
    public static void UseOracle(
        [NotNull] this AbpDbContextOptions options,
        Action<OracleDbContextOptionsBuilder>? oracleOptionsAction = null,
        bool useExistingConnectionIfAvailable = false
    )
    {
        options.Configure(context =>
        {
            context.UseOracle(oracleOptionsAction, useExistingConnectionIfAvailable);
        });
    }

    public static void UseOracle<TDbContext>(
        [NotNull] this AbpDbContextOptions options,
        Action<OracleDbContextOptionsBuilder>? oracleOptionsAction = null,
        bool useExistingConnectionIfAvailable = false
    )
        where TDbContext : AbpDbContext<TDbContext>
    {
        options.Configure<TDbContext>(context =>
        {
            context.UseOracle(oracleOptionsAction, useExistingConnectionIfAvailable);
        });
    }
}
