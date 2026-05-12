namespace AuroraStruct3D.DbMigrator
{
    /// <summary>
    /// 数据库迁移工具运行模式
    /// </summary>
    public enum MigratorMode
    {
        /// <summary>
        /// 更新数据库（执行待执行的迁移并填充种子数据）
        /// </summary>
        Update,

        /// <summary>
        /// 重建数据库（删除现有数据库后重新创建并填充种子数据）
        /// </summary>
        Rebuild,
    }

    /// <summary>
    /// 数据库迁移工具启动选项
    /// </summary>
    public class MigratorOptions
    {
        /// <summary>
        /// 运行模式，默认为更新模式
        /// </summary>
        public MigratorMode Mode { get; set; } = MigratorMode.Update;
    }
}
