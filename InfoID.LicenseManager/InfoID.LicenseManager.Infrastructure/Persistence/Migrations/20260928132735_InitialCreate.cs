using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace InfoID.LicenseManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminUser",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Is2FAEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    TotpSecretEncrypted = table.Column<string>(type: "text", nullable: true),
                    FailedLoginCount = table.Column<int>(type: "integer", nullable: false),
                    LockoutEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastLoginDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Cancelled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminUser", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BlacklistEntry",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EntryType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Cancelled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlacklistEntry", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerRecord",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrganizationName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OrganizationType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ContactEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ContactPhone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Cancelled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailTemplate",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    BodyHtml = table.Column<string>(type: "text", nullable: false),
                    TemplateType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Cancelled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailTemplate", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdminAuditLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AdminUserId = table.Column<long>(type: "bigint", nullable: true),
                    Action = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    IPAddress = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    ActionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DetailsJson = table.Column<string>(type: "text", nullable: true),
                    PreviousHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RecordHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Cancelled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminAuditLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminAuditLog_AdminUser_AdminUserId",
                        column: x => x.AdminUserId,
                        principalTable: "AdminUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LicenseCode",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ActivationCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Tier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LicenseType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    MaxCardsPerPeriod = table.Column<int>(type: "integer", nullable: true),
                    CardLimitPeriod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    MaxActivations = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevokedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CustomerRecordId = table.Column<long>(type: "bigint", nullable: true),
                    IsUsed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByAdminUserId = table.Column<long>(type: "bigint", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Cancelled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicenseCode", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LicenseCode_AdminUser_CreatedByAdminUserId",
                        column: x => x.CreatedByAdminUserId,
                        principalTable: "AdminUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LicenseCode_CustomerRecord_CustomerRecordId",
                        column: x => x.CustomerRecordId,
                        principalTable: "CustomerRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActivationRecord",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LicenseCodeId = table.Column<long>(type: "bigint", nullable: false),
                    MachineFingerprint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SerialKeyIssued = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SoftCode = table.Column<string>(type: "text", nullable: true),
                    ActivationMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ActivationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IssuedTier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IssuedExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DeactivatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastValidationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AppVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    GeneratedByAdminUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Cancelled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivationRecord", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivationRecord_AdminUser_GeneratedByAdminUserId",
                        column: x => x.GeneratedByAdminUserId,
                        principalTable: "AdminUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivationRecord_LicenseCode_LicenseCodeId",
                        column: x => x.LicenseCodeId,
                        principalTable: "LicenseCode",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupportTicket",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerRecordId = table.Column<long>(type: "bigint", nullable: false),
                    LicenseCodeId = table.Column<long>(type: "bigint", nullable: true),
                    Subject = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AssignedAdminUserId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Cancelled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportTicket", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupportTicket_AdminUser_AssignedAdminUserId",
                        column: x => x.AssignedAdminUserId,
                        principalTable: "AdminUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportTicket_CustomerRecord_CustomerRecordId",
                        column: x => x.CustomerRecordId,
                        principalTable: "CustomerRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupportTicket_LicenseCode_LicenseCodeId",
                        column: x => x.LicenseCodeId,
                        principalTable: "LicenseCode",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FraudAlert",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ActivationRecordId = table.Column<long>(type: "bigint", nullable: true),
                    AlertType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DetectedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsResolved = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Cancelled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FraudAlert", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FraudAlert_ActivationRecord_ActivationRecordId",
                        column: x => x.ActivationRecordId,
                        principalTable: "ActivationRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransferHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LicenseCodeId = table.Column<long>(type: "bigint", nullable: false),
                    OldActivationRecordId = table.Column<long>(type: "bigint", nullable: true),
                    NewActivationRecordId = table.Column<long>(type: "bigint", nullable: true),
                    TransferDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Cancelled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransferHistory_ActivationRecord_NewActivationRecordId",
                        column: x => x.NewActivationRecordId,
                        principalTable: "ActivationRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferHistory_ActivationRecord_OldActivationRecordId",
                        column: x => x.OldActivationRecordId,
                        principalTable: "ActivationRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferHistory_LicenseCode_LicenseCodeId",
                        column: x => x.LicenseCodeId,
                        principalTable: "LicenseCode",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivationRecord_GeneratedByAdminUserId",
                table: "ActivationRecord",
                column: "GeneratedByAdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivationRecord_LicenseCodeId",
                table: "ActivationRecord",
                column: "LicenseCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivationRecord_MachineFingerprint",
                table: "ActivationRecord",
                column: "MachineFingerprint");

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditLog_ActionDate",
                table: "AdminAuditLog",
                column: "ActionDate");

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditLog_AdminUserId",
                table: "AdminAuditLog",
                column: "AdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminUser_Email",
                table: "AdminUser",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlacklistEntry_EntryType_Value",
                table: "BlacklistEntry",
                columns: new[] { "EntryType", "Value" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerRecord_ContactEmail",
                table: "CustomerRecord",
                column: "ContactEmail");

            migrationBuilder.CreateIndex(
                name: "IX_FraudAlert_ActivationRecordId",
                table: "FraudAlert",
                column: "ActivationRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_LicenseCode_ActivationCode",
                table: "LicenseCode",
                column: "ActivationCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LicenseCode_BatchId",
                table: "LicenseCode",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_LicenseCode_CreatedByAdminUserId",
                table: "LicenseCode",
                column: "CreatedByAdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LicenseCode_CustomerRecordId",
                table: "LicenseCode",
                column: "CustomerRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_LicenseCode_Status",
                table: "LicenseCode",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicket_AssignedAdminUserId",
                table: "SupportTicket",
                column: "AssignedAdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicket_CustomerRecordId",
                table: "SupportTicket",
                column: "CustomerRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_SupportTicket_LicenseCodeId",
                table: "SupportTicket",
                column: "LicenseCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferHistory_LicenseCodeId",
                table: "TransferHistory",
                column: "LicenseCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferHistory_NewActivationRecordId",
                table: "TransferHistory",
                column: "NewActivationRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferHistory_OldActivationRecordId",
                table: "TransferHistory",
                column: "OldActivationRecordId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminAuditLog");

            migrationBuilder.DropTable(
                name: "BlacklistEntry");

            migrationBuilder.DropTable(
                name: "EmailTemplate");

            migrationBuilder.DropTable(
                name: "FraudAlert");

            migrationBuilder.DropTable(
                name: "SupportTicket");

            migrationBuilder.DropTable(
                name: "TransferHistory");

            migrationBuilder.DropTable(
                name: "ActivationRecord");

            migrationBuilder.DropTable(
                name: "LicenseCode");

            migrationBuilder.DropTable(
                name: "AdminUser");

            migrationBuilder.DropTable(
                name: "CustomerRecord");
        }
    }
}
