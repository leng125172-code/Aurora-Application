using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCalibMotorType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "MotorType", table: "AbpProCalibMotorParams");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MotorType",
                table: "AbpProCalibMotorParams",
                type: "integer",
                nullable: false,
                defaultValue: 0
            );
        }
    }
}
