using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectDeviceBindingsToCalibProject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DistanceMotorAxisId",
                table: "AbpProCalibProjects",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MainCameraDeviceId",
                table: "AbpProCalibProjects",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MainCameraMotorAxisId",
                table: "AbpProCalibProjects",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SecondaryCameraDeviceId",
                table: "AbpProCalibProjects",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SecondaryCameraMotorAxisId",
                table: "AbpProCalibProjects",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DistanceMotorAxisId",
                table: "AbpProCalibProjects");

            migrationBuilder.DropColumn(
                name: "MainCameraDeviceId",
                table: "AbpProCalibProjects");

            migrationBuilder.DropColumn(
                name: "MainCameraMotorAxisId",
                table: "AbpProCalibProjects");

            migrationBuilder.DropColumn(
                name: "SecondaryCameraDeviceId",
                table: "AbpProCalibProjects");

            migrationBuilder.DropColumn(
                name: "SecondaryCameraMotorAxisId",
                table: "AbpProCalibProjects");
        }
    }
}
