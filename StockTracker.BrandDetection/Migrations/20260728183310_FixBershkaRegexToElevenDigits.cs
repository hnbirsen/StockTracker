using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockTracker.BrandDetection.Migrations
{
    /// <inheritdoc />
    public partial class FixBershkaRegexToElevenDigits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "BrandCodeSignatures",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                columns: new[] { "Confidence", "RegexPattern" },
                values: new object[] { 3, "^\\d{11}$" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "BrandCodeSignatures",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                columns: new[] { "Confidence", "RegexPattern" },
                values: new object[] { 2, "^\\d{7,9}$" });
        }
    }
}
