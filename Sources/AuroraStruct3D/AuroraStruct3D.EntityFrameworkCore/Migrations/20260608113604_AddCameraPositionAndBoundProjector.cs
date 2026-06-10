using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddCameraPositionAndBoundProjector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BoundProjectorDeviceId",
                table: "AbpProCalibProjects",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CameraPosition",
                table: "AbpProCalibCameraParams",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BoundProjectorDeviceId",
                table: "AbpProCalibProjects");

            migrationBuilder.DropColumn(
                name: "CameraPosition",
                table: "AbpProCalibCameraParams");
        }
    }
}
