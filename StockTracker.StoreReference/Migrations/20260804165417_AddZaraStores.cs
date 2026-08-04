using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace StockTracker.StoreReference.Migrations
{
    /// <inheritdoc />
    public partial class AddZaraStores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Stores",
                columns: new[] { "Id", "BrandId", "BrandName", "BrandSpecificStoreId", "City", "CreatedAt", "District", "IsActive", "StoreName" },
                values: new object[,]
                {
                    { new Guid("d2222222-0000-0000-0000-000000000001"), new Guid("b2c3d4e5-f6a7-8901-bcde-f12345678901"), "Zara", "3231", "Istanbul", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Kadikoy", true, "Bağdat Caddesi" },
                    { new Guid("d2222222-0000-0000-0000-000000000002"), new Guid("b2c3d4e5-f6a7-8901-bcde-f12345678901"), "Zara", "12692", "Istanbul", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Sisli", true, "Cevahir AVM" },
                    { new Guid("d2222222-0000-0000-0000-000000000003"), new Guid("b2c3d4e5-f6a7-8901-bcde-f12345678901"), "Zara", "251", "Ankara", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Cankaya", true, "Kentpark" },
                    { new Guid("d2222222-0000-0000-0000-000000000004"), new Guid("b2c3d4e5-f6a7-8901-bcde-f12345678901"), "Zara", "3643", "Izmir", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Bornova", true, "Forum Bornova" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d2222222-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d2222222-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d2222222-0000-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d2222222-0000-0000-0000-000000000004"));
        }
    }
}
