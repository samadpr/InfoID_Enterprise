using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InfoID.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLogDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "AuditLog",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MachineName",
                table: "AuditLog",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_ActionDate",
                table: "AuditLog",
                column: "ActionDate");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_Module",
                table: "AuditLog",
                column: "Module");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditLog_ActionDate",
                table: "AuditLog");

            migrationBuilder.DropIndex(
                name: "IX_AuditLog_Module",
                table: "AuditLog");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "AuditLog");

            migrationBuilder.DropColumn(
                name: "MachineName",
                table: "AuditLog");
        }
    }
}
