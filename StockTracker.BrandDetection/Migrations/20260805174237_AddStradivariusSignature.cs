using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockTracker.BrandDetection.Migrations
{
    /// <inheritdoc />
    public partial class AddStradivariusSignature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "BrandCodeSignatures",
                columns: new[] { "Id", "BrandId", "BrandName", "Confidence", "CreatedAt", "IsActive", "RegexPattern" },
                values: new object[] { new Guid("88888888-8888-8888-8888-888888888888"), new Guid("b8c9d0e1-f2a3-4567-bcde-678901234567"), "Stradivarius", 2, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "^\\d{8}$" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "BrandCodeSignatures",
                keyColumn: "Id",
                keyValue: new Guid("88888888-8888-8888-8888-888888888888"));
        }
    }
}
