using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace StockTracker.Product.Migrations
{
    /// <inheritdoc />
    public partial class AddRemainingBrandSeeds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Brands",
                columns: new[] { "Id", "CreatedAt", "IsActive", "Name", "ScraperQueueName", "SearchEndpoint" },
                values: new object[,]
                {
                    { new Guid("a7b8c9d0-e1f2-3456-abcd-567890123456"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Beymen", "beymen", "https://www.beymen.com/tr/arama" },
                    { new Guid("b8c9d0e1-f2a3-4567-bcde-678901234567"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Stradivarius", "stradivarius", "https://www.stradivarius.com/tr/search" },
                    { new Guid("c9d0e1f2-a3b4-5678-cdef-789012345678"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Oysho", "oysho", "https://www.oysho.com/tr/search" },
                    { new Guid("d4e5f6a7-b8c9-0123-defa-234567890123"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Mango", "mango", "https://shop.mango.com/tr/search" },
                    { new Guid("e5f6a7b8-c9d0-1234-eabc-345678901234"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "H&M", "hm", "https://www2.hm.com/tr_tr/search-results.html" },
                    { new Guid("f6a7b8c9-d0e1-2345-fabc-456789012345"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Massimo Dutti", "massimodutti", "https://www.massimodutti.com/tr/search" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Brands",
                keyColumn: "Id",
                keyValue: new Guid("a7b8c9d0-e1f2-3456-abcd-567890123456"));

            migrationBuilder.DeleteData(
                table: "Brands",
                keyColumn: "Id",
                keyValue: new Guid("b8c9d0e1-f2a3-4567-bcde-678901234567"));

            migrationBuilder.DeleteData(
                table: "Brands",
                keyColumn: "Id",
                keyValue: new Guid("c9d0e1f2-a3b4-5678-cdef-789012345678"));

            migrationBuilder.DeleteData(
                table: "Brands",
                keyColumn: "Id",
                keyValue: new Guid("d4e5f6a7-b8c9-0123-defa-234567890123"));

            migrationBuilder.DeleteData(
                table: "Brands",
                keyColumn: "Id",
                keyValue: new Guid("e5f6a7b8-c9d0-1234-eabc-345678901234"));

            migrationBuilder.DeleteData(
                table: "Brands",
                keyColumn: "Id",
                keyValue: new Guid("f6a7b8c9-d0e1-2345-fabc-456789012345"));
        }
    }
}
