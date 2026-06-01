using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddCalibrationModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AbpProCalibCameraParams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalibProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CameraDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    SensorSize = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SensorWidthMm = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    SensorHeightMm = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    ImageWidthPixels = table.Column<int>(type: "integer", nullable: false),
                    ImageHeightPixels = table.Column<int>(type: "integer", nullable: false),
                    PixelSizeUm = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    LensFocalLength = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    MaxAperture = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    MinAperture = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    CurrentAperture = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    ExposureTimeMinUs = table.Column<int>(type: "integer", nullable: false),
                    ExposureTimeMaxUs = table.Column<int>(type: "integer", nullable: false),
                    GainMinDb = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    GainMaxDb = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsTemplateMode = table.Column<bool>(type: "boolean", nullable: false),
                    TemplateCategory = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProCalibCameraParams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AbpProCalibGimbalGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    MaxSpeed = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    Acceleration = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    AccelerationTime = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    DecelerationTime = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProCalibGimbalGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AbpProCalibMotorConstraints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalibProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    MotorAId = table.Column<Guid>(type: "uuid", nullable: false),
                    PositionRangeMin = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    PositionRangeMax = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    MotorBId = table.Column<Guid>(type: "uuid", nullable: false),
                    ForbiddenDirection = table.Column<int>(type: "integer", nullable: false),
                    RuleDescription = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProCalibMotorConstraints", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AbpProCalibMotorParams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalibProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    MotorAxisId = table.Column<Guid>(type: "uuid", nullable: false),
                    MotorType = table.Column<int>(type: "integer", nullable: false),
                    EncoderResolution = table.Column<int>(type: "integer", nullable: false),
                    GearRatio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    MechanicalOriginPosition = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    OriginDirection = table.Column<int>(type: "integer", nullable: false),
                    PositiveSoftLimit = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    NegativeSoftLimit = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    HomeSpeed = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    HomeAcceleration = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    IsOriginLocked = table.Column<bool>(type: "boolean", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProCalibMotorParams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AbpProCalibProjectorParams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalibProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectorDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ResolutionWidth = table.Column<int>(type: "integer", nullable: false),
                    ResolutionHeight = table.Column<int>(type: "integer", nullable: false),
                    ProjectionRatio = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    WorkingDistanceMin = table.Column<int>(type: "integer", nullable: false),
                    WorkingDistanceMax = table.Column<int>(type: "integer", nullable: false),
                    PatternType = table.Column<int>(type: "integer", nullable: false),
                    PatternCount = table.Column<int>(type: "integer", nullable: false),
                    PhaseShift = table.Column<decimal>(type: "numeric(10,6)", precision: 10, scale: 6, nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsTemplateMode = table.Column<bool>(type: "boolean", nullable: false),
                    TemplateCategory = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProCalibProjectorParams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AbpProCalibProjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    DeviceSeries = table.Column<int>(type: "integer", nullable: false),
                    DeviceType = table.Column<int>(type: "integer", nullable: false),
                    CameraCount = table.Column<int>(type: "integer", nullable: false),
                    ProjectorCount = table.Column<int>(type: "integer", nullable: false),
                    CalibStatus = table.Column<int>(type: "integer", nullable: false),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProCalibProjects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AbpProCalibDeviceBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalibProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    BindingType = table.Column<int>(type: "integer", nullable: false),
                    SourceDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetRole = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    BoundCameraParamId = table.Column<Guid>(type: "uuid", nullable: true),
                    BoundMotorParamId = table.Column<Guid>(type: "uuid", nullable: true),
                    BoundProjectorParamId = table.Column<Guid>(type: "uuid", nullable: true),
                    BoundGimbalGroupId = table.Column<Guid>(type: "uuid", nullable: true),
                    BindingStatus = table.Column<int>(type: "integer", nullable: false),
                    StatusMessage = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProCalibDeviceBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbpProCalibDeviceBindings_AbpProCalibGimbalGroups_BoundGimb~",
                        column: x => x.BoundGimbalGroupId,
                        principalTable: "AbpProCalibGimbalGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AbpProCalibGimbalBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GimbalGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    MotorAxisId = table.Column<Guid>(type: "uuid", nullable: false),
                    AxisType = table.Column<int>(type: "integer", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    CreationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProCalibGimbalBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbpProCalibGimbalBindings_AbpProCalibGimbalGroups_GimbalGro~",
                        column: x => x.GimbalGroupId,
                        principalTable: "AbpProCalibGimbalGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibCameraParams_CalibProjectId",
                table: "AbpProCalibCameraParams",
                column: "CalibProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibCameraParams_CameraDeviceId",
                table: "AbpProCalibCameraParams",
                column: "CameraDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibCameraParams_IsTemplateMode_TemplateCategory",
                table: "AbpProCalibCameraParams",
                columns: new[] { "IsTemplateMode", "TemplateCategory" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibDeviceBindings_BoundGimbalGroupId",
                table: "AbpProCalibDeviceBindings",
                column: "BoundGimbalGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibDeviceBindings_CalibProjectId",
                table: "AbpProCalibDeviceBindings",
                column: "CalibProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibDeviceBindings_CalibProjectId_BindingType",
                table: "AbpProCalibDeviceBindings",
                columns: new[] { "CalibProjectId", "BindingType" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibDeviceBindings_CalibProjectId_TargetRole",
                table: "AbpProCalibDeviceBindings",
                columns: new[] { "CalibProjectId", "TargetRole" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibGimbalBindings_GimbalGroupId",
                table: "AbpProCalibGimbalBindings",
                column: "GimbalGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibGimbalBindings_GimbalGroupId_AxisType",
                table: "AbpProCalibGimbalBindings",
                columns: new[] { "GimbalGroupId", "AxisType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibGimbalGroups_IsEnabled",
                table: "AbpProCalibGimbalGroups",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibMotorConstraints_CalibProjectId",
                table: "AbpProCalibMotorConstraints",
                column: "CalibProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibMotorConstraints_IsEnabled",
                table: "AbpProCalibMotorConstraints",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibMotorParams_CalibProjectId",
                table: "AbpProCalibMotorParams",
                column: "CalibProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibMotorParams_CalibProjectId_MotorAxisId",
                table: "AbpProCalibMotorParams",
                columns: new[] { "CalibProjectId", "MotorAxisId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibProjectorParams_CalibProjectId",
                table: "AbpProCalibProjectorParams",
                column: "CalibProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibProjectorParams_IsTemplateMode_TemplateCategory",
                table: "AbpProCalibProjectorParams",
                columns: new[] { "IsTemplateMode", "TemplateCategory" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibProjectorParams_ProjectorDeviceId",
                table: "AbpProCalibProjectorParams",
                column: "ProjectorDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibProjects_CalibStatus",
                table: "AbpProCalibProjects",
                column: "CalibStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AbpProCalibCameraParams");

            migrationBuilder.DropTable(
                name: "AbpProCalibDeviceBindings");

            migrationBuilder.DropTable(
                name: "AbpProCalibGimbalBindings");

            migrationBuilder.DropTable(
                name: "AbpProCalibMotorConstraints");

            migrationBuilder.DropTable(
                name: "AbpProCalibMotorParams");

            migrationBuilder.DropTable(
                name: "AbpProCalibProjectorParams");

            migrationBuilder.DropTable(
                name: "AbpProCalibProjects");

            migrationBuilder.DropTable(
                name: "AbpProCalibGimbalGroups");
        }
    }
}
