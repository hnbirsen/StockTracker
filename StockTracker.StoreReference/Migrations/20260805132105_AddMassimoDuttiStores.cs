using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace StockTracker.StoreReference.Migrations
{
    /// <inheritdoc />
    public partial class AddMassimoDuttiStores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Stores",
                columns: new[] { "Id", "BrandId", "BrandName", "BrandSpecificStoreId", "City", "CreatedAt", "District", "IsActive", "Latitude", "Longitude", "StoreName" },
                values: new object[,]
                {
                    { new Guid("d5555555-0000-0000-0000-000000000001"), new Guid("f6a7b8c9-d0e1-2345-fabc-456789012345"), "Massimo Dutti", "12013", "Istanbul", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Kadikoy", true, 40.953105999999998, 29.121725000000001, "Hilltown AVM" },
                    { new Guid("d5555555-0000-0000-0000-000000000002"), new Guid("f6a7b8c9-d0e1-2345-fabc-456789012345"), "Massimo Dutti", "4483", "Istanbul", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Sisli", true, 41.063594999999999, 28.992114999999998, "Cevahir AVM" },
                    { new Guid("d5555555-0000-0000-0000-000000000003"), new Guid("f6a7b8c9-d0e1-2345-fabc-456789012345"), "Massimo Dutti", "4009", "Ankara", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Cankaya", true, 39.909011, 32.776290000000003, "Kentpark AVM" },
                    { new Guid("d5555555-0000-0000-0000-000000000004"), new Guid("f6a7b8c9-d0e1-2345-fabc-456789012345"), "Massimo Dutti", "12840", "Izmir", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Bornova", true, 38.478435099999999, 27.074343200000001, "Karşıyaka Rönesans AVM" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d5555555-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d5555555-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d5555555-0000-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d5555555-0000-0000-0000-000000000004"));
        }
    }
}
