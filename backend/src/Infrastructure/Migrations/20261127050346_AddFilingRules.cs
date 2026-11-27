using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuArchive.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFilingRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FilingRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    VendorContains = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    FileNameContains = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AssignTagId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SetCategory = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    MoveToArchiveId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AppliedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilingRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FilingRules_Archives_MoveToArchiveId",
                        column: x => x.MoveToArchiveId,
                        principalTable: "Archives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_FilingRules_Tags_AssignTagId",
                        column: x => x.AssignTagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_FilingRules_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FilingRules_AssignTagId",
                table: "FilingRules",
                column: "AssignTagId");

            migrationBuilder.CreateIndex(
                name: "IX_FilingRules_MoveToArchiveId",
                table: "FilingRules",
                column: "MoveToArchiveId");

            migrationBuilder.CreateIndex(
                name: "IX_FilingRules_WorkspaceId",
                table: "FilingRules",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_FilingRules_WorkspaceId_Name",
                table: "FilingRules",
                columns: new[] { "WorkspaceId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FilingRules");
        }
    }
}
