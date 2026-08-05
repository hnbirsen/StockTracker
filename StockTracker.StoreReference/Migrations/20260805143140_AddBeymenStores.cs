using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace StockTracker.StoreReference.Migrations
{
    /// <inheritdoc />
    public partial class AddBeymenStores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Stores",
                columns: new[] { "Id", "BrandId", "BrandName", "BrandSpecificStoreId", "City", "CreatedAt", "District", "IsActive", "Latitude", "Longitude", "StoreName" },
                values: new object[,]
                {
                    { new Guid("d6666666-0000-0000-0000-000000000001"), new Guid("a7b8c9d0-e1f2-3456-abcd-567890123456"), "Beymen", "Beymen Suadiye", "Istanbul", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Kadikoy", true, 40.957216000000003, 29.087569999999999, "Beymen Suadiye" },
                    { new Guid("d6666666-0000-0000-0000-000000000002"), new Guid("a7b8c9d0-e1f2-3456-abcd-567890123456"), "Beymen", "Beymen Nişantaşı", "Istanbul", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Sisli", true, 41.049605, 28.992227, "Beymen Nişantaşı" },
                    { new Guid("d6666666-0000-0000-0000-000000000003"), new Guid("a7b8c9d0-e1f2-3456-abcd-567890123456"), "Beymen", "Beymen Panora", "Ankara", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Cankaya", true, 39.84796, 32.832813000000002, "Beymen Panora" },
                    { new Guid("d6666666-0000-0000-0000-000000000004"), new Guid("a7b8c9d0-e1f2-3456-abcd-567890123456"), "Beymen", "Beymen Hilltown İzmir", "Izmir", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Bornova", true, 38.478248999999998, 27.073872000000001, "Beymen Hilltown İzmir" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d6666666-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d6666666-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d6666666-0000-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d6666666-0000-0000-0000-000000000004"));
        }
    }
}
