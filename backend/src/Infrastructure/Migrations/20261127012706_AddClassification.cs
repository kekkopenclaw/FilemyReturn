using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuArchive.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "ClassificationConfidence",
                table: "Documents",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClassificationReason",
                table: "Documents",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmedCategory",
                table: "Documents",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NeedsReview",
                table: "Documents",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_NeedsReview",
                table: "Documents",
                column: "NeedsReview");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_NeedsReview",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ClassificationConfidence",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ClassificationReason",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ConfirmedCategory",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "NeedsReview",
                table: "Documents");
        }
    }
}
