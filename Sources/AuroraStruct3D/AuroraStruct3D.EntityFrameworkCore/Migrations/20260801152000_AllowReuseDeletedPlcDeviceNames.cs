using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations;

[DbContext(typeof(AuroraStruct3DDbContext))]
[Migration("20260801152000_AllowReuseDeletedPlcDeviceNames")]
public partial class AllowReuseDeletedPlcDeviceNames : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_AbpProPlcDevices_Name",
            table: "AbpProPlcDevices");

        migrationBuilder.CreateIndex(
            name: "IX_AbpProPlcDevices_Name",
            table: "AbpProPlcDevices",
            column: "Name",
            unique: true,
            filter: "\"IsDeleted\" = false");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_AbpProPlcDevices_Name",
            table: "AbpProPlcDevices");

        migrationBuilder.CreateIndex(
            name: "IX_AbpProPlcDevices_Name",
            table: "AbpProPlcDevices",
            column: "Name",
            unique: true);
    }
}
