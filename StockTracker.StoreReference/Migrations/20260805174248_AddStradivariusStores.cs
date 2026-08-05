using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace StockTracker.StoreReference.Migrations
{
    /// <inheritdoc />
    public partial class AddStradivariusStores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Stores",
                columns: new[] { "Id", "BrandId", "BrandName", "BrandSpecificStoreId", "City", "CreatedAt", "District", "IsActive", "Latitude", "Longitude", "StoreName" },
                values: new object[,]
                {
                    { new Guid("d8888888-0000-0000-0000-000000000001"), new Guid("b8c9d0e1-f2a3-4567-bcde-678901234567"), "Stradivarius", "City's Kozyatağı AVM, Kadıköy, İstanbul", "Istanbul", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Kadikoy", true, 40.980039099999999, 29.099343399999999, "City's Kozyatağı AVM" },
                    { new Guid("d8888888-0000-0000-0000-000000000002"), new Guid("b8c9d0e1-f2a3-4567-bcde-678901234567"), "Stradivarius", "Cevahir AVM, Şişli, İstanbul", "Istanbul", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Sisli", true, 41.063594999999999, 28.992114999999998, "Cevahir AVM" },
                    { new Guid("d8888888-0000-0000-0000-000000000003"), new Guid("b8c9d0e1-f2a3-4567-bcde-678901234567"), "Stradivarius", "Kentpark AVM, Çankaya, Ankara", "Ankara", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Cankaya", true, 39.909011, 32.776290000000003, "Kentpark AVM" },
                    { new Guid("d8888888-0000-0000-0000-000000000004"), new Guid("b8c9d0e1-f2a3-4567-bcde-678901234567"), "Stradivarius", "Forum Bornova AVM, Bornova, İzmir", "Izmir", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Bornova", true, 38.450340269999998, 27.208679100000001, "Forum Bornova AVM" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d8888888-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d8888888-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d8888888-0000-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d8888888-0000-0000-0000-000000000004"));
        }
    }
}
