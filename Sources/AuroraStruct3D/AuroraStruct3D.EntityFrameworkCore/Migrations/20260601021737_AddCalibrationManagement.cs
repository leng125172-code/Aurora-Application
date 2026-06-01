using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddCalibrationManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AbpProCalibCameraTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CameraModel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ParametersJson = table.Column<string>(type: "text", nullable: false),
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
                    table.PrimaryKey("PK_AbpProCalibCameraTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AbpProCalibDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DeviceType = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_AbpProCalibDevices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AbpProCalibProjectorTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProjectorModel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ParametersJson = table.Column<string>(type: "text", nullable: false),
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
                    table.PrimaryKey("PK_AbpProCalibProjectorTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AbpProCalibProjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CalibrationDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    CompletedTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    BoardType = table.Column<int>(type: "integer", nullable: false),
                    BoardRows = table.Column<int>(type: "integer", nullable: false),
                    BoardCols = table.Column<int>(type: "integer", nullable: false),
                    SquareSizeMm = table.Column<double>(type: "double precision", nullable: true),
                    CircleDiameterMm = table.Column<double>(type: "double precision", nullable: true),
                    CircleSpacingMm = table.Column<double>(type: "double precision", nullable: true),
                    AprilTagFamily = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AprilTagSizeMm = table.Column<double>(type: "double precision", nullable: true),
                    AprilTagSpacingMm = table.Column<double>(type: "double precision", nullable: true),
                    BoardManufactureAccuracyMm = table.Column<double>(type: "double precision", nullable: false),
                    TargetCaptureCount = table.Column<int>(type: "integer", nullable: false),
                    UnifiedExposureUs = table.Column<double>(type: "double precision", nullable: false),
                    UnifiedGainDb = table.Column<double>(type: "double precision", nullable: false),
                    UnifiedWhiteBalance = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ImageFormat = table.Column<int>(type: "integer", nullable: false),
                    StructuredLightBrightness = table.Column<int>(type: "integer", nullable: true),
                    PatternIntervalMs = table.Column<int>(type: "integer", nullable: true),
                    CapturesPerPhase = table.Column<int>(type: "integer", nullable: true),
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
                name: "AbpProCalibResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalibrationProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CalibrationDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ComputedTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CameraIntrinsicsJson = table.Column<string>(type: "text", nullable: false),
                    CameraExtrinsicsJson = table.Column<string>(type: "text", nullable: false),
                    StructuredLightCalibrationJson = table.Column<string>(type: "text", nullable: true),
                    OverallReprojectionError = table.Column<double>(type: "double precision", nullable: false),
                    MaxError = table.Column<double>(type: "double precision", nullable: false),
                    MinError = table.Column<double>(type: "double precision", nullable: false),
                    MeanError = table.Column<double>(type: "double precision", nullable: false),
                    RmsError = table.Column<double>(type: "double precision", nullable: false),
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
                    table.PrimaryKey("PK_AbpProCalibResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AbpProCalibCameraBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalibrationDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CameraDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProCalibCameraBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbpProCalibCameraBindings_AbpProCalibDevices_CalibrationDev~",
                        column: x => x.CalibrationDeviceId,
                        principalTable: "AbpProCalibDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AbpProCalibCameraParameters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalibrationDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CameraDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    CmosSize = table.Column<int>(type: "integer", nullable: false),
                    CmosWidthMm = table.Column<double>(type: "double precision", nullable: false),
                    CmosHeightMm = table.Column<double>(type: "double precision", nullable: false),
                    ResolutionWidthPx = table.Column<int>(type: "integer", nullable: false),
                    ResolutionHeightPx = table.Column<int>(type: "integer", nullable: false),
                    NominalFocalLengthMm = table.Column<double>(type: "double precision", nullable: false),
                    MaxAperture = table.Column<double>(type: "double precision", nullable: false),
                    MinAperture = table.Column<double>(type: "double precision", nullable: false),
                    CurrentAperture = table.Column<double>(type: "double precision", nullable: false),
                    MinExposureUs = table.Column<double>(type: "double precision", nullable: false),
                    MaxExposureUs = table.Column<double>(type: "double precision", nullable: false),
                    MinGainDb = table.Column<double>(type: "double precision", nullable: false),
                    MaxGainDb = table.Column<double>(type: "double precision", nullable: false),
                    PixelSizeUm = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProCalibCameraParameters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbpProCalibCameraParameters_AbpProCalibDevices_CalibrationD~",
                        column: x => x.CalibrationDeviceId,
                        principalTable: "AbpProCalibDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AbpProCalibGimbalGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalibrationDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    XAxisMotorId = table.Column<Guid>(type: "uuid", nullable: false),
                    YAxisMotorId = table.Column<Guid>(type: "uuid", nullable: false),
                    ZRotateAxisMotorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ZTranslateAxisMotorId = table.Column<Guid>(type: "uuid", nullable: true),
                    MaxVelocity = table.Column<double>(type: "double precision", nullable: false),
                    Acceleration = table.Column<double>(type: "double precision", nullable: false),
                    AccelDecelTime = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProCalibGimbalGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbpProCalibGimbalGroups_AbpProCalibDevices_CalibrationDevic~",
                        column: x => x.CalibrationDeviceId,
                        principalTable: "AbpProCalibDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AbpProCalibMotorBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalibrationDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    MotorAxisId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    GimbalGroupId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProCalibMotorBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbpProCalibMotorBindings_AbpProCalibDevices_CalibrationDevi~",
                        column: x => x.CalibrationDeviceId,
                        principalTable: "AbpProCalibDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AbpProCalibMotorInterlockRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalibrationDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceMotorAxisId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcePositionMin = table.Column<double>(type: "double precision", nullable: false),
                    SourcePositionMax = table.Column<double>(type: "double precision", nullable: false),
                    TargetMotorAxisId = table.Column<Guid>(type: "uuid", nullable: false),
                    BlockedDirection = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProCalibMotorInterlockRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbpProCalibMotorInterlockRules_AbpProCalibDevices_Calibrati~",
                        column: x => x.CalibrationDeviceId,
                        principalTable: "AbpProCalibDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AbpProCalibMotorParameters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalibrationDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    MotorAxisId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    EncoderResolution = table.Column<int>(type: "integer", nullable: false),
                    GearRatio = table.Column<double>(type: "double precision", nullable: false),
                    HomePosition = table.Column<double>(type: "double precision", nullable: false),
                    HomeDirection = table.Column<int>(type: "integer", nullable: false),
                    SoftLimitMin = table.Column<double>(type: "double precision", nullable: false),
                    SoftLimitMax = table.Column<double>(type: "double precision", nullable: false),
                    HomingVelocity = table.Column<double>(type: "double precision", nullable: false),
                    HomingAcceleration = table.Column<double>(type: "double precision", nullable: false),
                    IsHomed = table.Column<bool>(type: "boolean", nullable: false),
                    LastHomedTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProCalibMotorParameters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbpProCalibMotorParameters_AbpProCalibDevices_CalibrationDe~",
                        column: x => x.CalibrationDeviceId,
                        principalTable: "AbpProCalibDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AbpProCalibProjectorBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalibrationDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectorDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProCalibProjectorBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbpProCalibProjectorBindings_AbpProCalibDevices_Calibration~",
                        column: x => x.CalibrationDeviceId,
                        principalTable: "AbpProCalibDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AbpProCalibProjectorParameters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalibrationDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectorDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResolutionWidthPx = table.Column<int>(type: "integer", nullable: false),
                    ResolutionHeightPx = table.Column<int>(type: "integer", nullable: false),
                    ThrowRatio = table.Column<double>(type: "double precision", nullable: false),
                    MinWorkingDistanceMm = table.Column<double>(type: "double precision", nullable: false),
                    MaxWorkingDistanceMm = table.Column<double>(type: "double precision", nullable: false),
                    Pattern = table.Column<int>(type: "integer", nullable: false),
                    PatternCount = table.Column<int>(type: "integer", nullable: false),
                    PhaseShift = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProCalibProjectorParameters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbpProCalibProjectorParameters_AbpProCalibDevices_Calibrati~",
                        column: x => x.CalibrationDeviceId,
                        principalTable: "AbpProCalibDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AbpProCalibCaptureFrames",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalibrationProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    FrameIndex = table.Column<int>(type: "integer", nullable: false),
                    CapturedTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    IsAccepted = table.Column<bool>(type: "boolean", nullable: false),
                    RejectionReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProCalibCaptureFrames", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbpProCalibCaptureFrames_AbpProCalibProjects_CalibrationPro~",
                        column: x => x.CalibrationProjectId,
                        principalTable: "AbpProCalibProjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AbpProCalibValidationRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalibrationResultId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidationType = table.Column<int>(type: "integer", nullable: false),
                    IsPassed = table.Column<bool>(type: "boolean", nullable: false),
                    MetricsJson = table.Column<string>(type: "text", nullable: true),
                    ReportBlobName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Remarks = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ValidatedTime = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProCalibValidationRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbpProCalibValidationRecords_AbpProCalibResults_Calibration~",
                        column: x => x.CalibrationResultId,
                        principalTable: "AbpProCalibResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AbpProCalibGimbalPresets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GimbalGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Remarks = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    XPosition = table.Column<double>(type: "double precision", nullable: false),
                    YPosition = table.Column<double>(type: "double precision", nullable: false),
                    ZRotatePosition = table.Column<double>(type: "double precision", nullable: true),
                    ZTranslatePosition = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProCalibGimbalPresets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbpProCalibGimbalPresets_AbpProCalibGimbalGroups_GimbalGrou~",
                        column: x => x.GimbalGroupId,
                        principalTable: "AbpProCalibGimbalGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AbpProCalibCaptureImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CalibrationCaptureFrameId = table.Column<Guid>(type: "uuid", nullable: false),
                    CameraDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CameraRole = table.Column<int>(type: "integer", nullable: false),
                    BlobName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ReprojectionError = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProCalibCaptureImages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbpProCalibCaptureImages_AbpProCalibCaptureFrames_Calibrati~",
                        column: x => x.CalibrationCaptureFrameId,
                        principalTable: "AbpProCalibCaptureFrames",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibCameraBindings_CalibrationDeviceId",
                table: "AbpProCalibCameraBindings",
                column: "CalibrationDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibCameraBindings_CalibrationDeviceId_Role",
                table: "AbpProCalibCameraBindings",
                columns: new[] { "CalibrationDeviceId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibCameraBindings_CameraDeviceId",
                table: "AbpProCalibCameraBindings",
                column: "CameraDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibCameraParameters_CalibrationDeviceId",
                table: "AbpProCalibCameraParameters",
                column: "CalibrationDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibCameraParameters_CalibrationDeviceId_CameraDevic~",
                table: "AbpProCalibCameraParameters",
                columns: new[] { "CalibrationDeviceId", "CameraDeviceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibCameraParameters_CameraDeviceId",
                table: "AbpProCalibCameraParameters",
                column: "CameraDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibCameraTemplates_CameraModel",
                table: "AbpProCalibCameraTemplates",
                column: "CameraModel");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibCameraTemplates_Name",
                table: "AbpProCalibCameraTemplates",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibCaptureFrames_CalibrationProjectId",
                table: "AbpProCalibCaptureFrames",
                column: "CalibrationProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibCaptureFrames_CalibrationProjectId_FrameIndex",
                table: "AbpProCalibCaptureFrames",
                columns: new[] { "CalibrationProjectId", "FrameIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibCaptureImages_CalibrationCaptureFrameId",
                table: "AbpProCalibCaptureImages",
                column: "CalibrationCaptureFrameId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibCaptureImages_CameraDeviceId",
                table: "AbpProCalibCaptureImages",
                column: "CameraDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibDevices_DeviceType",
                table: "AbpProCalibDevices",
                column: "DeviceType");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibDevices_IsActive",
                table: "AbpProCalibDevices",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibDevices_Name",
                table: "AbpProCalibDevices",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibGimbalGroups_CalibrationDeviceId",
                table: "AbpProCalibGimbalGroups",
                column: "CalibrationDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibGimbalGroups_CalibrationDeviceId_Name",
                table: "AbpProCalibGimbalGroups",
                columns: new[] { "CalibrationDeviceId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibGimbalPresets_GimbalGroupId",
                table: "AbpProCalibGimbalPresets",
                column: "GimbalGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibMotorBindings_CalibrationDeviceId",
                table: "AbpProCalibMotorBindings",
                column: "CalibrationDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibMotorBindings_GimbalGroupId",
                table: "AbpProCalibMotorBindings",
                column: "GimbalGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibMotorBindings_MotorAxisId",
                table: "AbpProCalibMotorBindings",
                column: "MotorAxisId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibMotorInterlockRules_CalibrationDeviceId",
                table: "AbpProCalibMotorInterlockRules",
                column: "CalibrationDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibMotorInterlockRules_SourceMotorAxisId",
                table: "AbpProCalibMotorInterlockRules",
                column: "SourceMotorAxisId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibMotorInterlockRules_TargetMotorAxisId",
                table: "AbpProCalibMotorInterlockRules",
                column: "TargetMotorAxisId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibMotorParameters_CalibrationDeviceId",
                table: "AbpProCalibMotorParameters",
                column: "CalibrationDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibMotorParameters_CalibrationDeviceId_MotorAxisId",
                table: "AbpProCalibMotorParameters",
                columns: new[] { "CalibrationDeviceId", "MotorAxisId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibMotorParameters_MotorAxisId",
                table: "AbpProCalibMotorParameters",
                column: "MotorAxisId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibProjectorBindings_CalibrationDeviceId",
                table: "AbpProCalibProjectorBindings",
                column: "CalibrationDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibProjectorBindings_ProjectorDeviceId",
                table: "AbpProCalibProjectorBindings",
                column: "ProjectorDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibProjectorParameters_CalibrationDeviceId",
                table: "AbpProCalibProjectorParameters",
                column: "CalibrationDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibProjectorParameters_CalibrationDeviceId_Projecto~",
                table: "AbpProCalibProjectorParameters",
                columns: new[] { "CalibrationDeviceId", "ProjectorDeviceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibProjectorParameters_ProjectorDeviceId",
                table: "AbpProCalibProjectorParameters",
                column: "ProjectorDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibProjectorTemplates_Name",
                table: "AbpProCalibProjectorTemplates",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibProjectorTemplates_ProjectorModel",
                table: "AbpProCalibProjectorTemplates",
                column: "ProjectorModel");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibProjects_CalibrationDeviceId",
                table: "AbpProCalibProjects",
                column: "CalibrationDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibProjects_CreationTime",
                table: "AbpProCalibProjects",
                column: "CreationTime");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibProjects_Status",
                table: "AbpProCalibProjects",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibResults_CalibrationDeviceId",
                table: "AbpProCalibResults",
                column: "CalibrationDeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibResults_CalibrationDeviceId_IsActive",
                table: "AbpProCalibResults",
                columns: new[] { "CalibrationDeviceId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibResults_CalibrationProjectId",
                table: "AbpProCalibResults",
                column: "CalibrationProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibResults_CalibrationProjectId_Version",
                table: "AbpProCalibResults",
                columns: new[] { "CalibrationProjectId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibValidationRecords_CalibrationResultId",
                table: "AbpProCalibValidationRecords",
                column: "CalibrationResultId");

            migrationBuilder.CreateIndex(
                name: "IX_AbpProCalibValidationRecords_ValidationType",
                table: "AbpProCalibValidationRecords",
                column: "ValidationType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AbpProCalibCameraBindings");

            migrationBuilder.DropTable(
                name: "AbpProCalibCameraParameters");

            migrationBuilder.DropTable(
                name: "AbpProCalibCameraTemplates");

            migrationBuilder.DropTable(
                name: "AbpProCalibCaptureImages");

            migrationBuilder.DropTable(
                name: "AbpProCalibGimbalPresets");

            migrationBuilder.DropTable(
                name: "AbpProCalibMotorBindings");

            migrationBuilder.DropTable(
                name: "AbpProCalibMotorInterlockRules");

            migrationBuilder.DropTable(
                name: "AbpProCalibMotorParameters");

            migrationBuilder.DropTable(
                name: "AbpProCalibProjectorBindings");

            migrationBuilder.DropTable(
                name: "AbpProCalibProjectorParameters");

            migrationBuilder.DropTable(
                name: "AbpProCalibProjectorTemplates");

            migrationBuilder.DropTable(
                name: "AbpProCalibValidationRecords");

            migrationBuilder.DropTable(
                name: "AbpProCalibCaptureFrames");

            migrationBuilder.DropTable(
                name: "AbpProCalibGimbalGroups");

            migrationBuilder.DropTable(
                name: "AbpProCalibResults");

            migrationBuilder.DropTable(
                name: "AbpProCalibProjects");

            migrationBuilder.DropTable(
                name: "AbpProCalibDevices");
        }
    }
}
