using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockTracker.BrandDetection.Migrations
{
    /// <inheritdoc />
    public partial class AddMangoSignature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "BrandCodeSignatures",
                columns: new[] { "Id", "BrandId", "BrandName", "Confidence", "CreatedAt", "IsActive", "RegexPattern" },
                values: new object[] { new Guid("44444444-4444-4444-4444-444444444444"), new Guid("d4e5f6a7-b8c9-0123-defa-234567890123"), "Mango", 2, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "^\\d{8}/\\d{2}$" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "BrandCodeSignatures",
                keyColumn: "Id",
                keyValue: new Guid("44444444-4444-4444-4444-444444444444"));
        }
    }
}
