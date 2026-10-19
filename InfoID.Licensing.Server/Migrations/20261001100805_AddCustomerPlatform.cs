using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace InfoID.Licensing.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerPlatform : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CustomerAccountId",
                table: "LicenseCode",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductCode",
                table: "LicenseCode",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "INFOID");

            migrationBuilder.CreateTable(
                name: "CustomerAccount",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CompanyName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PasswordHash = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    EmailVerified = table.Column<bool>(type: "boolean", nullable: false),
                    EmailVerifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedLoginCount = table.Column<int>(type: "integer", nullable: false),
                    LockoutEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastLoginDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TermsAcceptedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CustomerRecordId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Cancelled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAccount", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerAccount_CustomerRecord_CustomerRecordId",
                        column: x => x.CustomerRecordId,
                        principalTable: "CustomerRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Plan",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Tier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Tagline = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    FeaturesText = table.Column<string>(type: "text", nullable: true),
                    DurationMonths = table.Column<int>(type: "integer", nullable: true),
                    DefaultMachines = table.Column<int>(type: "integer", nullable: false),
                    MaxCardsPerPeriod = table.Column<int>(type: "integer", nullable: true),
                    CardLimitPeriod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PriceText = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Cancelled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plan", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Product",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Cancelled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Product", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerSession",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerAccountId = table.Column<long>(type: "bigint", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedFromIp = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AppVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Cancelled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerSession", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerSession_CustomerAccount_CustomerAccountId",
                        column: x => x.CustomerAccountId,
                        principalTable: "CustomerAccount",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmailCode",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerAccountId = table.Column<long>(type: "bigint", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    UsedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Cancelled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailCode", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailCode_CustomerAccount_CustomerAccountId",
                        column: x => x.CustomerAccountId,
                        principalTable: "CustomerAccount",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LicenseRequest",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PlanId = table.Column<long>(type: "bigint", nullable: false),
                    CustomerAccountId = table.Column<long>(type: "bigint", nullable: true),
                    Machines = table.Column<int>(type: "integer", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CompanyName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Country = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    StateProvince = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    StatusReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    HandledByAdminUserId = table.Column<long>(type: "bigint", nullable: true),
                    HandledDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LicenseCodeId = table.Column<long>(type: "bigint", nullable: true),
                    MachineFingerprint = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AppVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Cancelled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicenseRequest", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LicenseRequest_AdminUser_HandledByAdminUserId",
                        column: x => x.HandledByAdminUserId,
                        principalTable: "AdminUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LicenseRequest_CustomerAccount_CustomerAccountId",
                        column: x => x.CustomerAccountId,
                        principalTable: "CustomerAccount",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LicenseRequest_LicenseCode_LicenseCodeId",
                        column: x => x.LicenseCodeId,
                        principalTable: "LicenseCode",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LicenseRequest_Plan_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plan",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LicenseCode_CustomerAccountId",
                table: "LicenseCode",
                column: "CustomerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_LicenseCode_ProductCode",
                table: "LicenseCode",
                column: "ProductCode");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAccount_CustomerRecordId",
                table: "CustomerAccount",
                column: "CustomerRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAccount_Email",
                table: "CustomerAccount",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerSession_CustomerAccountId",
                table: "CustomerSession",
                column: "CustomerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerSession_TokenHash",
                table: "CustomerSession",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailCode_CustomerAccountId_Purpose",
                table: "EmailCode",
                columns: new[] { "CustomerAccountId", "Purpose" });

            migrationBuilder.CreateIndex(
                name: "IX_LicenseRequest_CustomerAccountId",
                table: "LicenseRequest",
                column: "CustomerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_LicenseRequest_Email",
                table: "LicenseRequest",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_LicenseRequest_HandledByAdminUserId",
                table: "LicenseRequest",
                column: "HandledByAdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LicenseRequest_LicenseCodeId",
                table: "LicenseRequest",
                column: "LicenseCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_LicenseRequest_PlanId",
                table: "LicenseRequest",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_LicenseRequest_Status",
                table: "LicenseRequest",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Plan_ProductCode_SortOrder",
                table: "Plan",
                columns: new[] { "ProductCode", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Product_Code",
                table: "Product",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LicenseCode_CustomerAccount_CustomerAccountId",
                table: "LicenseCode",
                column: "CustomerAccountId",
                principalTable: "CustomerAccount",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LicenseCode_CustomerAccount_CustomerAccountId",
                table: "LicenseCode");

            migrationBuilder.DropTable(
                name: "CustomerSession");

            migrationBuilder.DropTable(
                name: "EmailCode");

            migrationBuilder.DropTable(
                name: "LicenseRequest");

            migrationBuilder.DropTable(
                name: "Product");

            migrationBuilder.DropTable(
                name: "CustomerAccount");

            migrationBuilder.DropTable(
                name: "Plan");

            migrationBuilder.DropIndex(
                name: "IX_LicenseCode_CustomerAccountId",
                table: "LicenseCode");

            migrationBuilder.DropIndex(
                name: "IX_LicenseCode_ProductCode",
                table: "LicenseCode");

            migrationBuilder.DropColumn(
                name: "CustomerAccountId",
                table: "LicenseCode");

            migrationBuilder.DropColumn(
                name: "ProductCode",
                table: "LicenseCode");
        }
    }
}
