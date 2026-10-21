using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InfoID.LicenseManager.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOnlineActivation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowOfflineActivation",
                table: "LicenseCode",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CardsIssuedReported",
                table: "ActivationRecord",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowOfflineActivation",
                table: "LicenseCode");

            migrationBuilder.DropColumn(
                name: "CardsIssuedReported",
                table: "ActivationRecord");
        }
    }
}
