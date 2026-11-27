using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocuArchive.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptExtraction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtractedCurrency",
                table: "Documents",
                type: "TEXT",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExtractedDate",
                table: "Documents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExtractedTotal",
                table: "Documents",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractedVendor",
                table: "Documents",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "OcrConfidence",
                table: "Documents",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuggestedCategory",
                table: "Documents",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_OcrStatus",
                table: "Documents",
                column: "OcrStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_OcrStatus",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ExtractedCurrency",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ExtractedDate",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ExtractedTotal",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ExtractedVendor",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "OcrConfidence",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "SuggestedCategory",
                table: "Documents");
        }
    }
}
