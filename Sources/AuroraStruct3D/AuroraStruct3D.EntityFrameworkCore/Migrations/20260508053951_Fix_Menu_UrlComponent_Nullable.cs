using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class Fix_Menu_UrlComponent_Nullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Url",
                table: "AbpProMenus",
                type: "text",
                nullable: true,
                comment: "内外链地址",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "内外链地址"
            );

            migrationBuilder.AlterColumn<string>(
                name: "Component",
                table: "AbpProMenus",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true,
                comment: "组件地址",
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512,
                oldComment: "组件地址"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Url",
                table: "AbpProMenus",
                type: "text",
                nullable: false,
                defaultValue: "",
                comment: "内外链地址",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "内外链地址"
            );

            migrationBuilder.AlterColumn<string>(
                name: "Component",
                table: "AbpProMenus",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "",
                comment: "组件地址",
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512,
                oldNullable: true,
                oldComment: "组件地址"
            );
        }
    }
}
