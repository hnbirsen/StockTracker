using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockTracker.BrandDetection.Migrations
{
    /// <inheritdoc />
    public partial class UpdatePullBearSignature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "BrandCodeSignatures",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"),
                columns: new[] { "Confidence", "RegexPattern" },
                values: new object[] { 2, "^\\d{8}/\\d{3}$" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "BrandCodeSignatures",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"),
                columns: new[] { "Confidence", "RegexPattern" },
                values: new object[] { 1, "^\\d{8}$" });
        }
    }
}
