using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockTracker.Product.Migrations
{
    /// <inheritdoc />
    public partial class AddMaviBrand : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Brands",
                columns: new[] { "Id", "CreatedAt", "IsActive", "Name", "ScraperQueueName", "SearchEndpoint" },
                values: new object[] { new Guid("d0e1f2a3-b4c5-6789-defa-890123456789"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Mavi", "mavi", "https://www.mavi.com/arama" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Brands",
                keyColumn: "Id",
                keyValue: new Guid("d0e1f2a3-b4c5-6789-defa-890123456789"));
        }
    }
}
