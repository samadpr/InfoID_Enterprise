using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InfoID.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPrintHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DesignName",
                table: "PrintJob",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "TemplateId",
                table: "Card",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.CreateIndex(
                name: "IX_PrintJob_CreatedDate",
                table: "PrintJob",
                column: "CreatedDate");

            migrationBuilder.CreateIndex(
                name: "IX_Card_CardNumber",
                table: "Card",
                column: "CardNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Card_ExpiryDate",
                table: "Card",
                column: "ExpiryDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PrintJob_CreatedDate",
                table: "PrintJob");

            migrationBuilder.DropIndex(
                name: "IX_Card_CardNumber",
                table: "Card");

            migrationBuilder.DropIndex(
                name: "IX_Card_ExpiryDate",
                table: "Card");

            migrationBuilder.DropColumn(
                name: "DesignName",
                table: "PrintJob");

            migrationBuilder.AlterColumn<long>(
                name: "TemplateId",
                table: "Card",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
