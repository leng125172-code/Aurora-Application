using Npgsql;

namespace AuroraStruct3D.EntityFrameworkCore
{
    public class EntityFrameworkCoreAuroraStruct3DDbSchemaMigrator
        : IAuroraStruct3DDbSchemaMigrator,
            ITransientDependency
    {
        private readonly IServiceProvider _serviceProvider;

        public EntityFrameworkCoreAuroraStruct3DDbSchemaMigrator(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task MigrateAsync()
        {
            /* We intentionally resolving the AuroraStruct3DMigrationsDbContext
             * from IServiceProvider (instead of directly injecting it)
             * to properly get the connection string of the current tenant in the
             * current scope.
             */

            await _serviceProvider
                .GetRequiredService<AuroraStruct3DDbContext>()
                .Database.MigrateAsync();
        }

        public async Task RebuildAsync()
        {
            var dbContext = _serviceProvider.GetRequiredService<AuroraStruct3DDbContext>();

            // 获取连接字符串并解析目标数据库名
            var connectionString = dbContext.Database.GetConnectionString()!;
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            var targetDatabase = builder.Database!;

            // 清空 Npgsql 客户端连接池，防止池化连接阻止 DROP DATABASE
            NpgsqlConnection.ClearAllPools();

            // 切换到 postgres 维护库执行删库操作
            builder.Database = "postgres";
            await using var adminConn = new NpgsqlConnection(builder.ConnectionString);
            await adminConn.OpenAsync();

            // WITH (FORCE) 会自动终止所有活动连接后再删除（PostgreSQL 13+）
            var escapedDb = targetDatabase.Replace("\"", "\"\"");
            await using (var cmd = adminConn.CreateCommand())
            {
                cmd.CommandText = $"DROP DATABASE IF EXISTS \"{escapedDb}\" WITH (FORCE);";
                await cmd.ExecuteNonQueryAsync();
            }

            // 重新创建数据库并执行所有迁移
            await dbContext.Database.MigrateAsync();
        }
    }
}
