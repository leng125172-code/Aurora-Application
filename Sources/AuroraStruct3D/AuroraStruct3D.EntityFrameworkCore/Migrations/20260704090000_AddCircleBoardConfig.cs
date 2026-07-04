using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations;

/// <summary>
/// 为标定项目新增圆点标定板配置字段（全部可空，保持棋盘格历史数据兼容）。
/// </summary>
public partial class AddCircleBoardConfig : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "BoardType",
            table: "AbpProCalibProjects",
            type: "integer",
            nullable: false,
            defaultValue: 0
        );

        migrationBuilder.AddColumn<int>(
            name: "CirclePatternCols",
            table: "AbpProCalibProjects",
            type: "integer",
            nullable: true
        );

        migrationBuilder.AddColumn<int>(
            name: "CirclePatternRows",
            table: "AbpProCalibProjects",
            type: "integer",
            nullable: true
        );

        migrationBuilder.AddColumn<decimal>(
            name: "CircleSpacingMm",
            table: "AbpProCalibProjects",
            type: "numeric(10,4)",
            precision: 10,
            scale: 4,
            nullable: true
        );

        migrationBuilder.AddColumn<decimal>(
            name: "CircleDiameterMm",
            table: "AbpProCalibProjects",
            type: "numeric(10,4)",
            precision: 10,
            scale: 4,
            nullable: true
        );

        migrationBuilder.AddColumn<bool>(
            name: "HasCenterMarker",
            table: "AbpProCalibProjects",
            type: "boolean",
            nullable: true
        );

        migrationBuilder.AddColumn<bool>(
            name: "HasCornerLocators",
            table: "AbpProCalibProjects",
            type: "boolean",
            nullable: true
        );

        migrationBuilder.AddColumn<int>(
            name: "MarkerRow",
            table: "AbpProCalibProjects",
            type: "integer",
            nullable: true
        );

        migrationBuilder.AddColumn<int>(
            name: "MarkerCol",
            table: "AbpProCalibProjects",
            type: "integer",
            nullable: true
        );

        migrationBuilder.AddColumn<string>(
            name: "CircleDetectorConfigJson",
            table: "AbpProCalibProjects",
            type: "character varying(4000)",
            maxLength: 4000,
            nullable: true
        );
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "BoardType", table: "AbpProCalibProjects");
        migrationBuilder.DropColumn(name: "CirclePatternCols", table: "AbpProCalibProjects");
        migrationBuilder.DropColumn(name: "CirclePatternRows", table: "AbpProCalibProjects");
        migrationBuilder.DropColumn(name: "CircleSpacingMm", table: "AbpProCalibProjects");
        migrationBuilder.DropColumn(name: "CircleDiameterMm", table: "AbpProCalibProjects");
        migrationBuilder.DropColumn(name: "HasCenterMarker", table: "AbpProCalibProjects");
        migrationBuilder.DropColumn(name: "HasCornerLocators", table: "AbpProCalibProjects");
        migrationBuilder.DropColumn(name: "MarkerRow", table: "AbpProCalibProjects");
        migrationBuilder.DropColumn(name: "MarkerCol", table: "AbpProCalibProjects");
        migrationBuilder.DropColumn(name: "CircleDetectorConfigJson", table: "AbpProCalibProjects");
    }
}
