namespace AuroraStruct3D.Data
{
    public interface IAuroraStruct3DDbSchemaMigrator
    {
        /// <summary>
        /// 执行数据库迁移（更新模式）
        /// </summary>
        Task MigrateAsync();

        /// <summary>
        /// 删除数据库并重新创建（重建模式）
        /// </summary>
        Task RebuildAsync();
    }
}
