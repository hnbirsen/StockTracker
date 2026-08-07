using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace StockTracker.StoreReference.Migrations
{
    /// <inheritdoc />
    public partial class AddMaviStores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Stores",
                columns: new[] { "Id", "BrandId", "BrandName", "BrandSpecificStoreId", "City", "CreatedAt", "District", "IsActive", "Latitude", "Longitude", "StoreName" },
                values: new object[,]
                {
                    { new Guid("e0000000-0000-0000-0000-000000000001"), new Guid("d0e1f2a3-b4c5-6789-defa-890123456789"), "Mavi", "505", "Istanbul", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Kadikoy", true, 40.980039099999999, 29.099343399999999, "İçerenköy Carrefour" },
                    { new Guid("e0000000-0000-0000-0000-000000000002"), new Guid("d0e1f2a3-b4c5-6789-defa-890123456789"), "Mavi", "507", "Istanbul", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Sisli", true, 41.063594999999999, 28.992114999999998, "Cevahir AVM" },
                    { new Guid("e0000000-0000-0000-0000-000000000003"), new Guid("d0e1f2a3-b4c5-6789-defa-890123456789"), "Mavi", "823", "Ankara", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Cankaya", true, 39.909011, 32.776290000000003, "Ankara Kentpark" },
                    { new Guid("e0000000-0000-0000-0000-000000000004"), new Guid("d0e1f2a3-b4c5-6789-defa-890123456789"), "Mavi", "618", "Izmir", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Bornova", true, 38.450340269999998, 27.208679100000001, "İzmir Forum" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("e0000000-0000-0000-0000-000000000004"));
        }
    }
}
