using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddPostgisExtension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 启用 UUID 生成扩展
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS \"uuid-ossp\";");

            // 启用 PostGIS 空间数据扩展
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS postgis;");

            // 启用 PostGIS 拓扑扩展（依赖 postgis）
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS postgis_topology;");

            // 启用三元组模糊文本搜索扩展
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

            // 启用 B 树 GIN 索引扩展
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gin;");

            // 启用 B 树 GiST 索引扩展
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 注意：删除扩展前请确认无依赖对象，顺序与 Up 相反
            migrationBuilder.Sql("DROP EXTENSION IF EXISTS btree_gist;");
            migrationBuilder.Sql("DROP EXTENSION IF EXISTS btree_gin;");
            migrationBuilder.Sql("DROP EXTENSION IF EXISTS pg_trgm;");
            migrationBuilder.Sql("DROP EXTENSION IF EXISTS postgis_topology;");
            migrationBuilder.Sql("DROP EXTENSION IF EXISTS postgis;");
            migrationBuilder.Sql("DROP EXTENSION IF EXISTS \"uuid-ossp\";");
        }
    }
}
