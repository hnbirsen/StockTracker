using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockTracker.StoreReference.Migrations
{
    /// <inheritdoc />
    public partial class UpdateStradivariusStoreIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d8888888-0000-0000-0000-000000000001"),
                column: "BrandSpecificStoreId",
                value: "16879");

            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d8888888-0000-0000-0000-000000000002"),
                column: "BrandSpecificStoreId",
                value: "2859");

            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d8888888-0000-0000-0000-000000000003"),
                column: "BrandSpecificStoreId",
                value: "2968");

            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d8888888-0000-0000-0000-000000000004"),
                column: "BrandSpecificStoreId",
                value: "2868");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d8888888-0000-0000-0000-000000000001"),
                column: "BrandSpecificStoreId",
                value: "City's Kozyatağı AVM, Kadıköy, İstanbul");

            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d8888888-0000-0000-0000-000000000002"),
                column: "BrandSpecificStoreId",
                value: "Cevahir AVM, Şişli, İstanbul");

            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d8888888-0000-0000-0000-000000000003"),
                column: "BrandSpecificStoreId",
                value: "Kentpark AVM, Çankaya, Ankara");

            migrationBuilder.UpdateData(
                table: "Stores",
                keyColumn: "Id",
                keyValue: new Guid("d8888888-0000-0000-0000-000000000004"),
                column: "BrandSpecificStoreId",
                value: "Forum Bornova AVM, Bornova, İzmir");
        }
    }
}
