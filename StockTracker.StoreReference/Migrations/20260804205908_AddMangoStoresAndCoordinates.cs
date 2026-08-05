using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace StockTracker.StoreReference.Migrations
{
    /// <inheritdoc />
    public partial class AddMangoStoresAndCoordinates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "Stores",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "Stores",
                type: "double precision",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d1111111-0000-0000-0000-000000000001"),
                columns: new[] { "Latitude", "Longitude" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d1111111-0000-0000-0000-000000000002"),
                columns: new[] { "Latitude", "Longitude" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d1111111-0000-0000-0000-000000000003"),
                columns: new[] { "Latitude", "Longitude" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d1111111-0000-0000-0000-000000000004"),
                columns: new[] { "Latitude", "Longitude" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d2222222-0000-0000-0000-000000000001"),
                columns: new[] { "Latitude", "Longitude" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d2222222-0000-0000-0000-000000000002"),
                columns: new[] { "Latitude", "Longitude" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d2222222-0000-0000-0000-000000000003"),
                columns: new[] { "Latitude", "Longitude" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d2222222-0000-0000-0000-000000000004"),
                columns: new[] { "Latitude", "Longitude" },
                values: new object[] { null, null });

            migrationBuilder.InsertData(
                table: "Stores",
                columns: new[] { "Id", "BrandId", "BrandName", "BrandSpecificStoreId", "City", "CreatedAt", "District", "IsActive", "Latitude", "Longitude", "StoreName" },
                values: new object[,]
                {
                    { new Guid("d3333333-0000-0000-0000-000000000001"), new Guid("d4e5f6a7-b8c9-0123-defa-234567890123"), "Mango", "10389", "Istanbul", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Kadikoy", true, 40.959937009724001, 29.080951331352001, "Bağdat Caddesi (Suadiye)" },
                    { new Guid("d3333333-0000-0000-0000-000000000002"), new Guid("d4e5f6a7-b8c9-0123-defa-234567890123"), "Mango", "10277", "Istanbul", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Sisli", true, 41.062784014649999, 28.992832831243, "Cevahir AVM" },
                    { new Guid("d3333333-0000-0000-0000-000000000003"), new Guid("d4e5f6a7-b8c9-0123-defa-234567890123"), "Mango", "10403", "Ankara", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Cankaya", true, 39.909714541850001, 32.778216907751002, "CEPA AVM" },
                    { new Guid("d3333333-0000-0000-0000-000000000004"), new Guid("d4e5f6a7-b8c9-0123-defa-234567890123"), "Mango", "10711", "Izmir", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Bornova", true, 38.450381582437998, 27.209401193083, "Forum Bornova" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d3333333-0000-0000-0000-000000000001"));

            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d3333333-0000-0000-0000-000000000002"));

            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d3333333-0000-0000-0000-000000000003"));

            migrationBuilder.DeleteData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d3333333-0000-0000-0000-000000000004"));

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Stores");
        }
    }
}
