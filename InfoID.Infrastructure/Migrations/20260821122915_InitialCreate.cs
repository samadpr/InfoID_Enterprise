using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InfoID.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BackupRestoreLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OperationType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    StartedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupRestoreLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Organization",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 250, nullable: false),
                    OrgType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Country = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Address = table.Column<string>(type: "TEXT", nullable: true),
                    LogoPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    AccentColorHex = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organization", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PluginRegistration",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    PluginType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    AssemblyPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PluginRegistration", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccessControlPlatform",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrganizationId = table.Column<long>(type: "INTEGER", nullable: false),
                    PlatformName = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ApiEndpoint = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    CredentialsEncrypted = table.Column<string>(type: "TEXT", nullable: true),
                    FieldMappingJson = table.Column<string>(type: "TEXT", nullable: true),
                    SyncMode = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessControlPlatform", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessControlPlatform_Organization_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organization",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AppUser",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrganizationId = table.Column<long>(type: "INTEGER", nullable: false),
                    FullName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    PreferredLanguage = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    ThemeMode = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    IsPasswordProtected = table.Column<bool>(type: "INTEGER", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    AutoLockMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    LastLoginDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUser", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppUser_Organization_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organization",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomFieldDefinition",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrganizationId = table.Column<long>(type: "INTEGER", nullable: false),
                    FieldKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    DataType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    IsRequired = table.Column<bool>(type: "INTEGER", nullable: false),
                    AppliesToCategory = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomFieldDefinition", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomFieldDefinition_Organization_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organization",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DataSourceConnection",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrganizationId = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SourceType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ConnectionStringEncrypted = table.Column<string>(type: "TEXT", nullable: true),
                    IsScheduled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ScheduleCron = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    LastImportDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataSourceConnection", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DataSourceConnection_Organization_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organization",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LicenseRecord",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrganizationId = table.Column<long>(type: "INTEGER", nullable: false),
                    ActivationCode = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SerialKey = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    MachineFingerprint = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    LicenseTier = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    LicenseType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ActivationMode = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    ActivationDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExpiryDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MaxCardsPerPeriod = table.Column<int>(type: "INTEGER", nullable: true),
                    CardsIssuedThisPeriod = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    GracePeriodDays = table.Column<int>(type: "INTEGER", nullable: false),
                    LastValidationDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RevalidationIntervalDays = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicenseRecord", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LicenseRecord_Organization_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organization",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PrinterProfile",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrganizationId = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Brand = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    SupportsDuplex = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsLamination = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsEncodingInTandem = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConnectionType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    RibbonCostEstimate = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: true),
                    CardCostEstimate = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrinterProfile", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrinterProfile_Organization_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organization",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RfidKeyProfile",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrganizationId = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    CardTechnology = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    SectorMappingJson = table.Column<string>(type: "TEXT", nullable: true),
                    KeyDataEncrypted = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RfidKeyProfile", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RfidKeyProfile_Organization_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organization",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SystemSetting",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrganizationId = table.Column<long>(type: "INTEGER", nullable: true),
                    SettingKey = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    SettingValue = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSetting", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SystemSetting_Organization_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organization",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalRecord",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ApprovedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    Context = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ContextReferenceId = table.Column<long>(type: "INTEGER", nullable: true),
                    SignedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SignatureDataEncrypted = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalRecord", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApprovalRecord_AppUser_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "AppUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AuditLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AppUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    Module = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EntityName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    EntityId = table.Column<long>(type: "INTEGER", nullable: true),
                    BeforeValueJson = table.Column<string>(type: "TEXT", nullable: true),
                    AfterValueJson = table.Column<string>(type: "TEXT", nullable: true),
                    ActionDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PreviousHash = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    RecordHash = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLog_AppUser_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AppUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    NotificationType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    IsRead = table.Column<bool>(type: "INTEGER", nullable: false),
                    SentDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationLog_AppUser_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReportDefinition",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrganizationId = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ReportType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    QueryJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportDefinition", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportDefinition_AppUser_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AppUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReportDefinition_Organization_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organization",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LicenseValidationLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LicenseRecordId = table.Column<long>(type: "INTEGER", nullable: false),
                    ValidationDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ValidationResult = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicenseValidationLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LicenseValidationLog_LicenseRecord_LicenseRecordId",
                        column: x => x.LicenseRecordId,
                        principalTable: "LicenseRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AccessControlSyncLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AccessControlPlatformId = table.Column<long>(type: "INTEGER", nullable: false),
                    CardId = table.Column<long>(type: "INTEGER", nullable: true),
                    Direction = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    SyncDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Result = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessControlSyncLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessControlSyncLog_AccessControlPlatform_AccessControlPlatformId",
                        column: x => x.AccessControlPlatformId,
                        principalTable: "AccessControlPlatform",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Branch",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrganizationId = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Address = table.Column<string>(type: "TEXT", nullable: true),
                    NumberingPrefix = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    DefaultPrinterProfileId = table.Column<long>(type: "INTEGER", nullable: true),
                    DefaultTemplateId = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Branch", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Branch_Organization_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organization",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Branch_PrinterProfile_DefaultPrinterProfileId",
                        column: x => x.DefaultPrinterProfileId,
                        principalTable: "PrinterProfile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Department",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BranchId = table.Column<long>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ParentDepartmentId = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Department", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Department_Branch_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branch",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Department_Department_ParentDepartmentId",
                        column: x => x.ParentDepartmentId,
                        principalTable: "Department",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Template",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrganizationId = table.Column<long>(type: "INTEGER", nullable: false),
                    BranchId = table.Column<long>(type: "INTEGER", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CardSize = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    FrontDesignJson = table.Column<string>(type: "TEXT", nullable: true),
                    BackDesignJson = table.Column<string>(type: "TEXT", nullable: true),
                    IsGalleryTemplate = table.Column<bool>(type: "INTEGER", nullable: false),
                    CurrentVersionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Template", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Template_Branch_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branch",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Template_Organization_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organization",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FieldMappingProfile",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DataSourceConnectionId = table.Column<long>(type: "INTEGER", nullable: false),
                    TemplateId = table.Column<long>(type: "INTEGER", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    MappingJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FieldMappingProfile", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FieldMappingProfile_DataSourceConnection_DataSourceConnectionId",
                        column: x => x.DataSourceConnectionId,
                        principalTable: "DataSourceConnection",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FieldMappingProfile_Template_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "Template",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PrintJob",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BranchId = table.Column<long>(type: "INTEGER", nullable: true),
                    PrinterProfileId = table.Column<long>(type: "INTEGER", nullable: false),
                    TemplateId = table.Column<long>(type: "INTEGER", nullable: true),
                    ApprovalRecordId = table.Column<long>(type: "INTEGER", nullable: true),
                    RequestedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    JobType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    TotalCards = table.Column<int>(type: "INTEGER", nullable: false),
                    SuccessCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FailureCount = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintJob", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrintJob_AppUser_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "AppUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrintJob_ApprovalRecord_ApprovalRecordId",
                        column: x => x.ApprovalRecordId,
                        principalTable: "ApprovalRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrintJob_Branch_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branch",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrintJob_PrinterProfile_PrinterProfileId",
                        column: x => x.PrinterProfileId,
                        principalTable: "PrinterProfile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrintJob_Template_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "Template",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TemplateElement",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TemplateId = table.Column<long>(type: "INTEGER", nullable: false),
                    Side = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ElementType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    BoundFieldName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    PositionX = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    PositionY = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    Width = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    Height = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: false),
                    Rotation = table.Column<decimal>(type: "TEXT", precision: 6, scale: 2, nullable: false),
                    ZIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    LayerLocked = table.Column<bool>(type: "INTEGER", nullable: false),
                    LayerVisible = table.Column<bool>(type: "INTEGER", nullable: false),
                    StyleJson = table.Column<string>(type: "TEXT", nullable: true),
                    ConditionalVisibilityRule = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateElement", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TemplateElement_Template_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "Template",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TemplateVersion",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TemplateId = table.Column<long>(type: "INTEGER", nullable: false),
                    VersionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    SnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                    ChangeNote = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateVersion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TemplateVersion_Template_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "Template",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ImportBatch",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DataSourceConnectionId = table.Column<long>(type: "INTEGER", nullable: false),
                    FieldMappingProfileId = table.Column<long>(type: "INTEGER", nullable: true),
                    BranchId = table.Column<long>(type: "INTEGER", nullable: true),
                    StartedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TotalRecords = table.Column<int>(type: "INTEGER", nullable: false),
                    SuccessCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatch", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportBatch_Branch_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branch",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ImportBatch_DataSourceConnection_DataSourceConnectionId",
                        column: x => x.DataSourceConnectionId,
                        principalTable: "DataSourceConnection",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ImportBatch_FieldMappingProfile_FieldMappingProfileId",
                        column: x => x.FieldMappingProfileId,
                        principalTable: "FieldMappingProfile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ImportErrorLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ImportBatchId = table.Column<long>(type: "INTEGER", nullable: false),
                    RowNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    FieldName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: false),
                    RawValue = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportErrorLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportErrorLog_ImportBatch_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "ImportBatch",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Card",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CardholderId = table.Column<long>(type: "INTEGER", nullable: false),
                    TemplateId = table.Column<long>(type: "INTEGER", nullable: false),
                    BranchId = table.Column<long>(type: "INTEGER", nullable: true),
                    CardNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    RFIDUid = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    IssuedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExpiryDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReplacesCardId = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Card", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Card_Branch_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branch",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Card_Card_ReplacesCardId",
                        column: x => x.ReplacesCardId,
                        principalTable: "Card",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Card_Template_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "Template",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CardStatusHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CardId = table.Column<long>(type: "INTEGER", nullable: false),
                    FromStatus = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    ToStatus = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ChangedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    ChangedByUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardStatusHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardStatusHistory_AppUser_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "AppUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CardStatusHistory_Card_CardId",
                        column: x => x.CardId,
                        principalTable: "Card",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrintJobItem",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PrintJobId = table.Column<long>(type: "INTEGER", nullable: false),
                    CardId = table.Column<long>(type: "INTEGER", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ReprintReasonCode = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    PrintedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintJobItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrintJobItem_Card_CardId",
                        column: x => x.CardId,
                        principalTable: "Card",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrintJobItem_PrintJob_PrintJobId",
                        column: x => x.PrintJobId,
                        principalTable: "PrintJob",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RfidEncodeLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CardId = table.Column<long>(type: "INTEGER", nullable: false),
                    RFIDKeyProfileId = table.Column<long>(type: "INTEGER", nullable: true),
                    Operation = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Result = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    EncodedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RfidEncodeLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RfidEncodeLog_Card_CardId",
                        column: x => x.CardId,
                        principalTable: "Card",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RfidEncodeLog_RfidKeyProfile_RFIDKeyProfileId",
                        column: x => x.RFIDKeyProfileId,
                        principalTable: "RfidKeyProfile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Cardholder",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrganizationId = table.Column<long>(type: "INTEGER", nullable: false),
                    BranchId = table.Column<long>(type: "INTEGER", nullable: true),
                    ImportBatchId = table.Column<long>(type: "INTEGER", nullable: true),
                    FullName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ExternalRefId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    DateOfBirth = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Gender = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Department = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    Designation = table.Column<string>(type: "TEXT", maxLength: 150, nullable: true),
                    IdNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CurrentPhotoId = table.Column<long>(type: "INTEGER", nullable: true),
                    CurrentSignatureId = table.Column<long>(type: "INTEGER", nullable: true),
                    IsAnonymized = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cardholder", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cardholder_Branch_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branch",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Cardholder_ImportBatch_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "ImportBatch",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Cardholder_Organization_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organization",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CardholderCustomFieldValue",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CardholderId = table.Column<long>(type: "INTEGER", nullable: false),
                    CustomFieldDefinitionId = table.Column<long>(type: "INTEGER", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardholderCustomFieldValue", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardholderCustomFieldValue_Cardholder_CardholderId",
                        column: x => x.CardholderId,
                        principalTable: "Cardholder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CardholderCustomFieldValue_CustomFieldDefinition_CustomFieldDefinitionId",
                        column: x => x.CustomFieldDefinitionId,
                        principalTable: "CustomFieldDefinition",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CardholderPhoto",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CardholderId = table.Column<long>(type: "INTEGER", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CaptureSource = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    ComplianceStatus = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    VersionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    IsCurrent = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardholderPhoto", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardholderPhoto_Cardholder_CardholderId",
                        column: x => x.CardholderId,
                        principalTable: "Cardholder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CardholderSignature",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CardholderId = table.Column<long>(type: "INTEGER", nullable: false),
                    FilePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CaptureSource = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    VersionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    IsCurrent = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardholderSignature", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CardholderSignature_Cardholder_CardholderId",
                        column: x => x.CardholderId,
                        principalTable: "Cardholder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MobileEnrollmentSession",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CardholderId = table.Column<long>(type: "INTEGER", nullable: true),
                    BranchId = table.Column<long>(type: "INTEGER", nullable: true),
                    SessionToken = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    QrCodeValue = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    SubmittedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SyncedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsKioskMode = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MobileEnrollmentSession", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MobileEnrollmentSession_Branch_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branch",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MobileEnrollmentSession_Cardholder_CardholderId",
                        column: x => x.CardholderId,
                        principalTable: "Cardholder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VisitorBadge",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CardholderId = table.Column<long>(type: "INTEGER", nullable: false),
                    HostAppUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    ValidFrom = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ValidTo = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AutoExpired = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisitorBadge", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VisitorBadge_AppUser_HostAppUserId",
                        column: x => x.HostAppUserId,
                        principalTable: "AppUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VisitorBadge_Cardholder_CardholderId",
                        column: x => x.CardholderId,
                        principalTable: "Cardholder",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VisitorCheckInLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VisitorBadgeId = table.Column<long>(type: "INTEGER", nullable: false),
                    CheckInDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CheckOutDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisitorCheckInLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VisitorCheckInLog_VisitorBadge_VisitorBadgeId",
                        column: x => x.VisitorBadgeId,
                        principalTable: "VisitorBadge",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VisitorPreRegistration",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VisitorBadgeId = table.Column<long>(type: "INTEGER", nullable: true),
                    FullName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    HostAppUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    ExpectedVisitDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ApprovalStatus = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisitorPreRegistration", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VisitorPreRegistration_AppUser_HostAppUserId",
                        column: x => x.HostAppUserId,
                        principalTable: "AppUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VisitorPreRegistration_VisitorBadge_VisitorBadgeId",
                        column: x => x.VisitorBadgeId,
                        principalTable: "VisitorBadge",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessControlPlatform_OrganizationId",
                table: "AccessControlPlatform",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessControlSyncLog_AccessControlPlatformId",
                table: "AccessControlSyncLog",
                column: "AccessControlPlatformId");

            migrationBuilder.CreateIndex(
                name: "IX_AccessControlSyncLog_CardId",
                table: "AccessControlSyncLog",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalRecord_ApprovedByUserId",
                table: "ApprovalRecord",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AppUser_OrganizationId",
                table: "AppUser",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_AppUserId",
                table: "AuditLog",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Branch_DefaultPrinterProfileId",
                table: "Branch",
                column: "DefaultPrinterProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Branch_DefaultTemplateId",
                table: "Branch",
                column: "DefaultTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_Branch_OrganizationId",
                table: "Branch",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Card_BranchId",
                table: "Card",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Card_CardholderId",
                table: "Card",
                column: "CardholderId");

            migrationBuilder.CreateIndex(
                name: "IX_Card_ReplacesCardId",
                table: "Card",
                column: "ReplacesCardId");

            migrationBuilder.CreateIndex(
                name: "IX_Card_TemplateId",
                table: "Card",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_Cardholder_BranchId",
                table: "Cardholder",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Cardholder_CurrentPhotoId",
                table: "Cardholder",
                column: "CurrentPhotoId");

            migrationBuilder.CreateIndex(
                name: "IX_Cardholder_CurrentSignatureId",
                table: "Cardholder",
                column: "CurrentSignatureId");

            migrationBuilder.CreateIndex(
                name: "IX_Cardholder_ImportBatchId",
                table: "Cardholder",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Cardholder_OrganizationId",
                table: "Cardholder",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_CardholderCustomFieldValue_CardholderId",
                table: "CardholderCustomFieldValue",
                column: "CardholderId");

            migrationBuilder.CreateIndex(
                name: "IX_CardholderCustomFieldValue_CustomFieldDefinitionId",
                table: "CardholderCustomFieldValue",
                column: "CustomFieldDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_CardholderPhoto_CardholderId",
                table: "CardholderPhoto",
                column: "CardholderId");

            migrationBuilder.CreateIndex(
                name: "IX_CardholderSignature_CardholderId",
                table: "CardholderSignature",
                column: "CardholderId");

            migrationBuilder.CreateIndex(
                name: "IX_CardStatusHistory_CardId",
                table: "CardStatusHistory",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_CardStatusHistory_ChangedByUserId",
                table: "CardStatusHistory",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomFieldDefinition_OrganizationId",
                table: "CustomFieldDefinition",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_DataSourceConnection_OrganizationId",
                table: "DataSourceConnection",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Department_BranchId",
                table: "Department",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Department_ParentDepartmentId",
                table: "Department",
                column: "ParentDepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_FieldMappingProfile_DataSourceConnectionId",
                table: "FieldMappingProfile",
                column: "DataSourceConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_FieldMappingProfile_TemplateId",
                table: "FieldMappingProfile",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatch_BranchId",
                table: "ImportBatch",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatch_DataSourceConnectionId",
                table: "ImportBatch",
                column: "DataSourceConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatch_FieldMappingProfileId",
                table: "ImportBatch",
                column: "FieldMappingProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportErrorLog_ImportBatchId",
                table: "ImportErrorLog",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_LicenseRecord_OrganizationId",
                table: "LicenseRecord",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_LicenseValidationLog_LicenseRecordId",
                table: "LicenseValidationLog",
                column: "LicenseRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_MobileEnrollmentSession_BranchId",
                table: "MobileEnrollmentSession",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_MobileEnrollmentSession_CardholderId",
                table: "MobileEnrollmentSession",
                column: "CardholderId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLog_AppUserId",
                table: "NotificationLog",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PrinterProfile_OrganizationId",
                table: "PrinterProfile",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_PrintJob_ApprovalRecordId",
                table: "PrintJob",
                column: "ApprovalRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_PrintJob_BranchId",
                table: "PrintJob",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_PrintJob_PrinterProfileId",
                table: "PrintJob",
                column: "PrinterProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_PrintJob_RequestedByUserId",
                table: "PrintJob",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PrintJob_TemplateId",
                table: "PrintJob",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobItem_CardId",
                table: "PrintJobItem",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobItem_PrintJobId",
                table: "PrintJobItem",
                column: "PrintJobId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportDefinition_CreatedByUserId",
                table: "ReportDefinition",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportDefinition_OrganizationId",
                table: "ReportDefinition",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_RfidEncodeLog_CardId",
                table: "RfidEncodeLog",
                column: "CardId");

            migrationBuilder.CreateIndex(
                name: "IX_RfidEncodeLog_RFIDKeyProfileId",
                table: "RfidEncodeLog",
                column: "RFIDKeyProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_RfidKeyProfile_OrganizationId",
                table: "RfidKeyProfile",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemSetting_OrganizationId",
                table: "SystemSetting",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Template_BranchId",
                table: "Template",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Template_OrganizationId",
                table: "Template",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_TemplateElement_TemplateId",
                table: "TemplateElement",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_TemplateVersion_TemplateId",
                table: "TemplateVersion",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_VisitorBadge_CardholderId",
                table: "VisitorBadge",
                column: "CardholderId");

            migrationBuilder.CreateIndex(
                name: "IX_VisitorBadge_HostAppUserId",
                table: "VisitorBadge",
                column: "HostAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VisitorCheckInLog_VisitorBadgeId",
                table: "VisitorCheckInLog",
                column: "VisitorBadgeId");

            migrationBuilder.CreateIndex(
                name: "IX_VisitorPreRegistration_HostAppUserId",
                table: "VisitorPreRegistration",
                column: "HostAppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_VisitorPreRegistration_VisitorBadgeId",
                table: "VisitorPreRegistration",
                column: "VisitorBadgeId");

            migrationBuilder.AddForeignKey(
                name: "FK_AccessControlSyncLog_Card_CardId",
                table: "AccessControlSyncLog",
                column: "CardId",
                principalTable: "Card",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Branch_Template_DefaultTemplateId",
                table: "Branch",
                column: "DefaultTemplateId",
                principalTable: "Template",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Card_Cardholder_CardholderId",
                table: "Card",
                column: "CardholderId",
                principalTable: "Cardholder",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Cardholder_CardholderPhoto_CurrentPhotoId",
                table: "Cardholder",
                column: "CurrentPhotoId",
                principalTable: "CardholderPhoto",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Cardholder_CardholderSignature_CurrentSignatureId",
                table: "Cardholder",
                column: "CurrentSignatureId",
                principalTable: "CardholderSignature",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Branch_Organization_OrganizationId",
                table: "Branch");

            migrationBuilder.DropForeignKey(
                name: "FK_Cardholder_Organization_OrganizationId",
                table: "Cardholder");

            migrationBuilder.DropForeignKey(
                name: "FK_DataSourceConnection_Organization_OrganizationId",
                table: "DataSourceConnection");

            migrationBuilder.DropForeignKey(
                name: "FK_PrinterProfile_Organization_OrganizationId",
                table: "PrinterProfile");

            migrationBuilder.DropForeignKey(
                name: "FK_Template_Organization_OrganizationId",
                table: "Template");

            migrationBuilder.DropForeignKey(
                name: "FK_Branch_PrinterProfile_DefaultPrinterProfileId",
                table: "Branch");

            migrationBuilder.DropForeignKey(
                name: "FK_Branch_Template_DefaultTemplateId",
                table: "Branch");

            migrationBuilder.DropForeignKey(
                name: "FK_FieldMappingProfile_Template_TemplateId",
                table: "FieldMappingProfile");

            migrationBuilder.DropForeignKey(
                name: "FK_Cardholder_Branch_BranchId",
                table: "Cardholder");

            migrationBuilder.DropForeignKey(
                name: "FK_ImportBatch_Branch_BranchId",
                table: "ImportBatch");

            migrationBuilder.DropForeignKey(
                name: "FK_CardholderPhoto_Cardholder_CardholderId",
                table: "CardholderPhoto");

            migrationBuilder.DropForeignKey(
                name: "FK_CardholderSignature_Cardholder_CardholderId",
                table: "CardholderSignature");

            migrationBuilder.DropTable(
                name: "AccessControlSyncLog");

            migrationBuilder.DropTable(
                name: "AuditLog");

            migrationBuilder.DropTable(
                name: "BackupRestoreLog");

            migrationBuilder.DropTable(
                name: "CardholderCustomFieldValue");

            migrationBuilder.DropTable(
                name: "CardStatusHistory");

            migrationBuilder.DropTable(
                name: "Department");

            migrationBuilder.DropTable(
                name: "ImportErrorLog");

            migrationBuilder.DropTable(
                name: "LicenseValidationLog");

            migrationBuilder.DropTable(
                name: "MobileEnrollmentSession");

            migrationBuilder.DropTable(
                name: "NotificationLog");

            migrationBuilder.DropTable(
                name: "PluginRegistration");

            migrationBuilder.DropTable(
                name: "PrintJobItem");

            migrationBuilder.DropTable(
                name: "ReportDefinition");

            migrationBuilder.DropTable(
                name: "RfidEncodeLog");

            migrationBuilder.DropTable(
                name: "SystemSetting");

            migrationBuilder.DropTable(
                name: "TemplateElement");

            migrationBuilder.DropTable(
                name: "TemplateVersion");

            migrationBuilder.DropTable(
                name: "VisitorCheckInLog");

            migrationBuilder.DropTable(
                name: "VisitorPreRegistration");

            migrationBuilder.DropTable(
                name: "AccessControlPlatform");

            migrationBuilder.DropTable(
                name: "CustomFieldDefinition");

            migrationBuilder.DropTable(
                name: "LicenseRecord");

            migrationBuilder.DropTable(
                name: "PrintJob");

            migrationBuilder.DropTable(
                name: "Card");

            migrationBuilder.DropTable(
                name: "RfidKeyProfile");

            migrationBuilder.DropTable(
                name: "VisitorBadge");

            migrationBuilder.DropTable(
                name: "ApprovalRecord");

            migrationBuilder.DropTable(
                name: "AppUser");

            migrationBuilder.DropTable(
                name: "Organization");

            migrationBuilder.DropTable(
                name: "PrinterProfile");

            migrationBuilder.DropTable(
                name: "Template");

            migrationBuilder.DropTable(
                name: "Branch");

            migrationBuilder.DropTable(
                name: "Cardholder");

            migrationBuilder.DropTable(
                name: "CardholderPhoto");

            migrationBuilder.DropTable(
                name: "CardholderSignature");

            migrationBuilder.DropTable(
                name: "ImportBatch");

            migrationBuilder.DropTable(
                name: "FieldMappingProfile");

            migrationBuilder.DropTable(
                name: "DataSourceConnection");
        }
    }
}
