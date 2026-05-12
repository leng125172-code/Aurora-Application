using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuroraStruct3D.EntityFrameworkCore.Migrations
{
    /// <inheritdoc />
    public partial class AddMotorAndProjectorModules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AbpProMotorAxes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    AxisIndex = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: true
                    ),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    PortName = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    BaudRate = table.Column<int>(type: "integer", nullable: false),
                    SlaveId = table.Column<int>(type: "integer", nullable: false),
                    Brand = table.Column<int>(type: "integer", nullable: false),
                    Model = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: true
                    ),
                    HomeMethod = table.Column<int>(type: "integer", nullable: false),
                    HomeDirection = table.Column<bool>(type: "boolean", nullable: false),
                    HomeHighSpeedRpm = table.Column<int>(type: "integer", nullable: false),
                    HomeLowSpeedRpm = table.Column<int>(type: "integer", nullable: false),
                    HomeAccTimeMs = table.Column<int>(type: "integer", nullable: false),
                    HomeDecTimeMs = table.Column<int>(type: "integer", nullable: false),
                    SoftLimitEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    SoftLimitPositive = table.Column<long>(type: "bigint", nullable: false),
                    SoftLimitNegative = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LastKnownPosition = table.Column<long>(type: "bigint", nullable: false),
                    LastKnownSpeed = table.Column<int>(type: "integer", nullable: false),
                    IsHomed = table.Column<bool>(type: "boolean", nullable: false),
                    LastStatusUpdateAt = table.Column<DateTime>(
                        type: "timestamp without time zone",
                        nullable: true
                    ),
                    ActiveMotionConfigId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
                        nullable: false
                    ),
                    CreationTime = table.Column<DateTime>(
                        type: "timestamp without time zone",
                        nullable: false
                    ),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(
                        type: "timestamp without time zone",
                        nullable: true
                    ),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(
                        type: "boolean",
                        nullable: false,
                        defaultValue: false
                    ),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(
                        type: "timestamp without time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProMotorAxes", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "AbpProMotorPrPaths",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MotorAxisId = table.Column<Guid>(type: "uuid", nullable: false),
                    PathIndex = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    MotionType = table.Column<int>(type: "integer", nullable: false),
                    PositionMode = table.Column<int>(type: "integer", nullable: false),
                    EnableInterrupt = table.Column<bool>(type: "boolean", nullable: false),
                    EnableOverlap = table.Column<bool>(type: "boolean", nullable: false),
                    EnableJump = table.Column<bool>(type: "boolean", nullable: false),
                    JumpToPathIndex = table.Column<int>(type: "integer", nullable: false),
                    TargetPosition = table.Column<long>(type: "bigint", nullable: false),
                    SpeedRpm = table.Column<int>(type: "integer", nullable: false),
                    AccTimeMs = table.Column<int>(type: "integer", nullable: false),
                    DecTimeMs = table.Column<int>(type: "integer", nullable: false),
                    DwellTimeMs = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProMotorPrPaths", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "AbpProProjectors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    DeviceIndex = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: true
                    ),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IpAddress = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    TcpPort = table.Column<int>(type: "integer", nullable: false),
                    ConnectTimeoutMs = table.Column<int>(type: "integer", nullable: false),
                    FirmwareVersion = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: true
                    ),
                    DeviceHardwareId = table.Column<int>(type: "integer", nullable: false),
                    ConnectionStatus = table.Column<int>(type: "integer", nullable: false),
                    LedStatus = table.Column<int>(type: "integer", nullable: false),
                    LastLightValue = table.Column<byte>(type: "smallint", nullable: false),
                    LastDisplayMode = table.Column<byte>(type: "smallint", nullable: false),
                    LastCommunicationAt = table.Column<DateTime>(
                        type: "timestamp without time zone",
                        nullable: true
                    ),
                    LastConnectedAt = table.Column<DateTime>(
                        type: "timestamp without time zone",
                        nullable: true
                    ),
                    LastDisconnectedAt = table.Column<DateTime>(
                        type: "timestamp without time zone",
                        nullable: true
                    ),
                    ExtraProperties = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyStamp = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
                        nullable: false
                    ),
                    CreationTime = table.Column<DateTime>(
                        type: "timestamp without time zone",
                        nullable: false
                    ),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(
                        type: "timestamp without time zone",
                        nullable: true
                    ),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(
                        type: "boolean",
                        nullable: false,
                        defaultValue: false
                    ),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(
                        type: "timestamp without time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProProjectors", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "AbpProMotorFaultRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MotorAxisId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTime>(
                        type: "timestamp without time zone",
                        nullable: false
                    ),
                    ClearedAt = table.Column<DateTime>(
                        type: "timestamp without time zone",
                        nullable: true
                    ),
                    RawFaultCode = table.Column<int>(type: "integer", nullable: false),
                    FaultDescription = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: false
                    ),
                    PositionAtFault = table.Column<long>(type: "bigint", nullable: false),
                    SpeedAtFault = table.Column<int>(type: "integer", nullable: false),
                    BusVoltageAtFault = table.Column<int>(type: "integer", nullable: true),
                    BusCurrentAtFault = table.Column<int>(type: "integer", nullable: true),
                    TemperatureAtFault = table.Column<int>(type: "integer", nullable: true),
                    IsHandled = table.Column<bool>(type: "boolean", nullable: false),
                    HandlingNote = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProMotorFaultRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbpProMotorFaultRecords_AbpProMotorAxes_MotorAxisId",
                        column: x => x.MotorAxisId,
                        principalTable: "AbpProMotorAxes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "AbpProMotorMotionConfigs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MotorAxisId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: false
                    ),
                    Description = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: true
                    ),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultSpeedRpm = table.Column<int>(type: "integer", nullable: false),
                    MaxSpeedRpm = table.Column<int>(type: "integer", nullable: false),
                    AccTimeMs = table.Column<int>(type: "integer", nullable: false),
                    DecTimeMs = table.Column<int>(type: "integer", nullable: false),
                    EmergencyDecTimeMs = table.Column<int>(type: "integer", nullable: false),
                    PulsesPerRevolution = table.Column<int>(type: "integer", nullable: false),
                    MaxTrackingError = table.Column<int>(type: "integer", nullable: false),
                    InPositionWindow = table.Column<int>(type: "integer", nullable: false),
                    InPositionDebounceMs = table.Column<int>(type: "integer", nullable: false),
                    PositionKp = table.Column<int>(type: "integer", nullable: true),
                    SpeedKp = table.Column<int>(type: "integer", nullable: true),
                    SpeedKi = table.Column<int>(type: "integer", nullable: true),
                    KtechAngleLimit = table.Column<long>(type: "bigint", nullable: true),
                    KtechTorqueLimit = table.Column<int>(type: "integer", nullable: true),
                    KtechCurrentRamp = table.Column<int>(type: "integer", nullable: true),
                    KtechSpeedRamp = table.Column<int>(type: "integer", nullable: true),
                    LeisaiHomeModeReg = table.Column<int>(type: "integer", nullable: true),
                    LeisaiTorqueHomeTimeMs = table.Column<int>(type: "integer", nullable: true),
                    LeisaiTorqueHomePercent = table.Column<int>(type: "integer", nullable: true),
                    CreationTime = table.Column<DateTime>(
                        type: "timestamp without time zone",
                        nullable: false
                    ),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastModificationTime = table.Column<DateTime>(
                        type: "timestamp without time zone",
                        nullable: true
                    ),
                    LastModifierId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsDeleted = table.Column<bool>(
                        type: "boolean",
                        nullable: false,
                        defaultValue: false
                    ),
                    DeleterId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeletionTime = table.Column<DateTime>(
                        type: "timestamp without time zone",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProMotorMotionConfigs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbpProMotorMotionConfigs_AbpProMotorAxes_MotorAxisId",
                        column: x => x.MotorAxisId,
                        principalTable: "AbpProMotorAxes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "AbpProProjectorOperationLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectorDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationType = table.Column<int>(type: "integer", nullable: false),
                    OccurredAt = table.Column<DateTime>(
                        type: "timestamp without time zone",
                        nullable: false
                    ),
                    IsSuccess = table.Column<bool>(type: "boolean", nullable: false),
                    RawCommand = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: true
                    ),
                    ParameterSummary = table.Column<string>(
                        type: "character varying(128)",
                        maxLength: 128,
                        nullable: true
                    ),
                    ErrorMessage = table.Column<string>(
                        type: "character varying(512)",
                        maxLength: 512,
                        nullable: true
                    ),
                    RoundTripMs = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AbpProProjectorOperationLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AbpProProjectorOperationLogs_AbpProProjectors_ProjectorDevi~",
                        column: x => x.ProjectorDeviceId,
                        principalTable: "AbpProProjectors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProMotorAxes_AxisIndex",
                table: "AbpProMotorAxes",
                column: "AxisIndex",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProMotorAxes_IsEnabled",
                table: "AbpProMotorAxes",
                column: "IsEnabled"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProMotorAxes_PortName_SlaveId",
                table: "AbpProMotorAxes",
                columns: new[] { "PortName", "SlaveId" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProMotorFaultRecords_MotorAxisId",
                table: "AbpProMotorFaultRecords",
                column: "MotorAxisId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProMotorFaultRecords_MotorAxisId_IsHandled",
                table: "AbpProMotorFaultRecords",
                columns: new[] { "MotorAxisId", "IsHandled" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProMotorFaultRecords_OccurredAt",
                table: "AbpProMotorFaultRecords",
                column: "OccurredAt"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProMotorMotionConfigs_MotorAxisId",
                table: "AbpProMotorMotionConfigs",
                column: "MotorAxisId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProMotorMotionConfigs_MotorAxisId_IsDefault",
                table: "AbpProMotorMotionConfigs",
                columns: new[] { "MotorAxisId", "IsDefault" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProMotorPrPaths_MotorAxisId",
                table: "AbpProMotorPrPaths",
                column: "MotorAxisId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProMotorPrPaths_MotorAxisId_PathIndex",
                table: "AbpProMotorPrPaths",
                columns: new[] { "MotorAxisId", "PathIndex" },
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProProjectorOperationLogs_OccurredAt",
                table: "AbpProProjectorOperationLogs",
                column: "OccurredAt"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProProjectorOperationLogs_ProjectorDeviceId",
                table: "AbpProProjectorOperationLogs",
                column: "ProjectorDeviceId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProProjectorOperationLogs_ProjectorDeviceId_IsSuccess",
                table: "AbpProProjectorOperationLogs",
                columns: new[] { "ProjectorDeviceId", "IsSuccess" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProProjectorOperationLogs_ProjectorDeviceId_OccurredAt",
                table: "AbpProProjectorOperationLogs",
                columns: new[] { "ProjectorDeviceId", "OccurredAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProProjectorOperationLogs_ProjectorDeviceId_OperationType",
                table: "AbpProProjectorOperationLogs",
                columns: new[] { "ProjectorDeviceId", "OperationType" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProProjectors_ConnectionStatus",
                table: "AbpProProjectors",
                column: "ConnectionStatus"
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProProjectors_DeviceIndex",
                table: "AbpProProjectors",
                column: "DeviceIndex",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProProjectors_IpAddress",
                table: "AbpProProjectors",
                column: "IpAddress",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_AbpProProjectors_IsEnabled",
                table: "AbpProProjectors",
                column: "IsEnabled"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "AbpProMotorFaultRecords");

            migrationBuilder.DropTable(name: "AbpProMotorMotionConfigs");

            migrationBuilder.DropTable(name: "AbpProMotorPrPaths");

            migrationBuilder.DropTable(name: "AbpProProjectorOperationLogs");

            migrationBuilder.DropTable(name: "AbpProMotorAxes");

            migrationBuilder.DropTable(name: "AbpProProjectors");
        }
    }
}
