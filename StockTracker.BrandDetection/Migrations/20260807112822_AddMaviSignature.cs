using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockTracker.BrandDetection.Migrations
{
    /// <inheritdoc />
    public partial class AddMaviSignature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "BrandCodeSignatures",
                columns: new[] { "Id", "BrandId", "BrandName", "Confidence", "CreatedAt", "IsActive", "RegexPattern" },
                values: new object[] { new Guid("aaaaaaaa-0000-0000-0000-000000000000"), new Guid("d0e1f2a3-b4c5-6789-defa-890123456789"), "Mavi", 2, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "^\\d{7}-[A-Z0-9]{4,6}$" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "BrandCodeSignatures",
                keyColumn: "Id",
                keyValue: new Guid("aaaaaaaa-0000-0000-0000-000000000000"));
        }
    }
}
