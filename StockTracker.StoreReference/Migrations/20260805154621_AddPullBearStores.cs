using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace StockTracker.StoreReference.Migrations
{
    /// <inheritdoc />
    public partial class AddPullBearStores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Stores",
                columns: new[] { "Id", "BrandId", "BrandName", "BrandSpecificStoreId", "City", "CreatedAt", "District", "IsActive", "Latitude", "Longitude", "StoreName" },
                values: new object[,]
                {
                    { new Guid("d7777777-0000-0000-0000-000000000001"), new Guid("c3d4e5f6-a7b8-9012-cdef-123456789012"), "Pull&Bear", "16941", "Istanbul", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Kadikoy", true, 40.980039099999999, 29.099343399999999, "City's Kozyatağı AVM" },
                    { new Guid("d7777777-0000-0000-0000-000000000002"), new Guid("c3d4e5f6-a7b8-9012-cdef-123456789012"), "Pull&Bear", "5287", "Istanbul", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Sisli", true, 41.063594999999999, 28.992114999999998, "Cevahir AVM" },
                    { new Guid("d7777777-0000-0000-0000-000000000003"), new Guid("c3d4e5f6-a7b8-9012-cdef-123456789012"), "Pull&Bear", "6370", "Ankara", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Cankaya", true, 39.909011, 32.776290000000003, "Kentpark AVM" },
                    { new Guid("d7777777-0000-0000-0000-000000000004"), new Guid("c3d4e5f6-a7b8-9012-cdef-123456789012"), "Pull&Bear", "5334", "Izmir", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Bornova", true, 38.450340269999998, 27.208679100000001, "Forum Bornova AVM" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d7777777-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d7777777-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d7777777-0000-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d7777777-0000-0000-0000-000000000004"));
        }
    }
}
