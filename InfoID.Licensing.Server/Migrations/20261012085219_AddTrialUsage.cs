using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace InfoID.Licensing.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddTrialUsage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrialUsage",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerAccountId = table.Column<long>(type: "bigint", nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CardLimit = table.Column<int>(type: "integer", nullable: false),
                    CardsUsed = table.Column<int>(type: "integer", nullable: false),
                    CardsReserved = table.Column<int>(type: "integer", nullable: false),
                    FirstPrintDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastPrintDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ModifiedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Cancelled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrialUsage", x => x.Id);
                    table.CheckConstraint("CK_TrialUsage_NonNegative", "\"CardsUsed\" >= 0 AND \"CardsReserved\" >= 0 AND \"CardLimit\" >= 0");
                    table.ForeignKey(
                        name: "FK_TrialUsage_CustomerAccount_CustomerAccountId",
                        column: x => x.CustomerAccountId,
                        principalTable: "CustomerAccount",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TrialReservation",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReservationKey = table.Column<Guid>(type: "uuid", nullable: false),
                    TrialUsageId = table.Column<long>(type: "bigint", nullable: false),
                    CustomerAccountId = table.Column<long>(type: "bigint", nullable: false),
                    Cards = table.Column<int>(type: "integer", nullable: false),
                    CardsPrinted = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DesignName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
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
                    table.PrimaryKey("PK_TrialReservation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrialReservation_TrialUsage_TrialUsageId",
                        column: x => x.TrialUsageId,
                        principalTable: "TrialUsage",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrialReservation_ReservationKey",
                table: "TrialReservation",
                column: "ReservationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrialReservation_TrialUsageId_Status",
                table: "TrialReservation",
                columns: new[] { "TrialUsageId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TrialUsage_CustomerAccountId_ProductCode",
                table: "TrialUsage",
                columns: new[] { "CustomerAccountId", "ProductCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrialReservation");

            migrationBuilder.DropTable(
                name: "TrialUsage");
        }
    }
}
