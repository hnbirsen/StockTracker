using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace StockTracker.Product.Migrations
{
    /// <inheritdoc />
    public partial class AddMissingBrandSeeds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Brands",
                columns: new[] { "Id", "CreatedAt", "IsActive", "Name", "ScraperQueueName", "SearchEndpoint" },
                values: new object[,]
                {
                    { new Guid("b2c3d4e5-f6a7-8901-bcde-f12345678901"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Zara", "zara", "https://www.zara.com/tr/search" },
                    { new Guid("c3d4e5f6-a7b8-9012-cdef-123456789012"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Pull&Bear", "pullbear", "https://www.pullandbear.com/tr/search" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Brands",
                keyColumn: "Id",
                keyValue: new Guid("b2c3d4e5-f6a7-8901-bcde-f12345678901"));

            migrationBuilder.DeleteData(
                table: "Brands",
                keyColumn: "Id",
                keyValue: new Guid("c3d4e5f6-a7b8-9012-cdef-123456789012"));
        }
    }
}
