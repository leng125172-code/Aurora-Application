using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations;

[DbContext(typeof(AuroraStruct3DDbContext))]
[Migration("20260803090000_AddCalibMotorHomingOptions")]
public partial class AddCalibMotorHomingOptions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "MoveAfterHome",
            table: "AbpProCalibMotorParams",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "WithZSignal",
            table: "AbpProCalibMotorParams",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "MoveAfterHome", table: "AbpProCalibMotorParams");
        migrationBuilder.DropColumn(name: "WithZSignal", table: "AbpProCalibMotorParams");
    }
}
