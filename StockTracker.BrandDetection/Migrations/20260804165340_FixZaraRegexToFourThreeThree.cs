using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockTracker.BrandDetection.Migrations
{
    /// <inheritdoc />
    public partial class FixZaraRegexToFourThreeThree : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "BrandCodeSignatures",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                column: "RegexPattern",
                value: "^\\d{4}/\\d{3}/\\d{3}$");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "BrandCodeSignatures",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                column: "RegexPattern",
                value: "^\\d{5}/\\d{3}/\\d{2,3}$");
        }
    }
}
