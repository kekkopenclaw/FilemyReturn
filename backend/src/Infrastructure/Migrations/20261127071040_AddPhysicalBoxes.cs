using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuArchive.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPhysicalBoxes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PhysicalBoxId",
                table: "Documents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PhysicalBoxes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Location = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhysicalBoxes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhysicalBoxes_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_PhysicalBoxId",
                table: "Documents",
                column: "PhysicalBoxId");

            migrationBuilder.CreateIndex(
                name: "IX_PhysicalBoxes_Code",
                table: "PhysicalBoxes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhysicalBoxes_WorkspaceId",
                table: "PhysicalBoxes",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_PhysicalBoxes_WorkspaceId_Name",
                table: "PhysicalBoxes",
                columns: new[] { "WorkspaceId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_PhysicalBoxes_PhysicalBoxId",
                table: "Documents",
                column: "PhysicalBoxId",
                principalTable: "PhysicalBoxes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_PhysicalBoxes_PhysicalBoxId",
                table: "Documents");

            migrationBuilder.DropTable(
                name: "PhysicalBoxes");

            migrationBuilder.DropIndex(
                name: "IX_Documents_PhysicalBoxId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "PhysicalBoxId",
                table: "Documents");
        }
    }
}
