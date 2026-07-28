using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockTracker.StoreReference.Migrations
{
    /// <inheritdoc />
    public partial class UpdateBershkaStoresWithRealIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d1111111-0000-0000-0000-000000000001"),
                columns: new[] { "BrandSpecificStoreId", "StoreName" },
                values: new object[] { "16884", "City's Kozyatağı" });

            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d1111111-0000-0000-0000-000000000002"),
                columns: new[] { "BrandSpecificStoreId", "StoreName" },
                values: new object[] { "8359", "Cevahir AVM" });

            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d1111111-0000-0000-0000-000000000003"),
                columns: new[] { "BrandSpecificStoreId", "StoreName" },
                values: new object[] { "6943", "Kentpark" });

            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d1111111-0000-0000-0000-000000000004"),
                columns: new[] { "BrandSpecificStoreId", "StoreName" },
                values: new object[] { "8426", "Forum Bornova" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d1111111-0000-0000-0000-000000000001"),
                columns: new[] { "BrandSpecificStoreId", "StoreName" },
                values: new object[] { "BSK-IST-KDK-01", "Bershka Kadikoy" });

            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d1111111-0000-0000-0000-000000000002"),
                columns: new[] { "BrandSpecificStoreId", "StoreName" },
                values: new object[] { "BSK-IST-SSL-01", "Bershka Cevahir AVM" });

            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d1111111-0000-0000-0000-000000000003"),
                columns: new[] { "BrandSpecificStoreId", "StoreName" },
                values: new object[] { "BSK-ANK-CNK-01", "Bershka Armada AVM" });

            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d1111111-0000-0000-0000-000000000004"),
                columns: new[] { "BrandSpecificStoreId", "StoreName" },
                values: new object[] { "BSK-IZM-BRN-01", "Bershka Forum Bornova" });
        }
    }
}
