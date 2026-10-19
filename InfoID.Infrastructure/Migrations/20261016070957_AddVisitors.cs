using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InfoID.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVisitors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Visitor",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VisitorCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    FullName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Company = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    PersonToMeet = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Purpose = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    VisitDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    PhotoPath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ExtraFieldsJson = table.Column<string>(type: "TEXT", nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    SourceName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: true),
                    SourceKey = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    ImportBatchKey = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: true),
                    ModifiedBy = table.Column<string>(type: "TEXT", nullable: true),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Visitor", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Visitor_CreatedDate",
                table: "Visitor",
                column: "CreatedDate");

            migrationBuilder.CreateIndex(
                name: "IX_Visitor_SourceKey",
                table: "Visitor",
                column: "SourceKey");

            migrationBuilder.CreateIndex(
                name: "IX_Visitor_VisitDate",
                table: "Visitor",
                column: "VisitDate");

            migrationBuilder.CreateIndex(
                name: "IX_Visitor_VisitorCode",
                table: "Visitor",
                column: "VisitorCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Visitor");
        }
    }
}
