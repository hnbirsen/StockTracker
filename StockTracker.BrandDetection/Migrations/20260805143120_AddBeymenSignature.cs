using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockTracker.BrandDetection.Migrations
{
    /// <inheritdoc />
    public partial class AddBeymenSignature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "BrandCodeSignatures",
                columns: new[] { "Id", "BrandId", "BrandName", "Confidence", "CreatedAt", "IsActive", "RegexPattern" },
                values: new object[] { new Guid("77777777-7777-7777-7777-777777777777"), new Guid("a7b8c9d0-e1f2-3456-abcd-567890123456"), "Beymen", 2, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "^\\d{7}$" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "BrandCodeSignatures",
                keyColumn: "Id",
                keyValue: new Guid("77777777-7777-7777-7777-777777777777"));
        }
    }
}
