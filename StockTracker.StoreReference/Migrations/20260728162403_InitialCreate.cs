using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace StockTracker.StoreReference.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Stores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BrandId = table.Column<Guid>(type: "uuid", nullable: false),
                    BrandName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    District = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StoreName = table.Column<string>(type: "text", nullable: true),
                    BrandSpecificStoreId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stores", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Stores",
                columns: new[] { "Id", "BrandId", "BrandName", "BrandSpecificStoreId", "City", "CreatedAt", "District", "IsActive", "StoreName" },
                values: new object[,]
                {
                    { new Guid("d1111111-0000-0000-0000-000000000001"), new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890"), "Bershka", "BSK-IST-KDK-01", "Istanbul", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Kadikoy", true, "Bershka Kadikoy" },
                    { new Guid("d1111111-0000-0000-0000-000000000002"), new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890"), "Bershka", "BSK-IST-SSL-01", "Istanbul", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Sisli", true, "Bershka Cevahir AVM" },
                    { new Guid("d1111111-0000-0000-0000-000000000003"), new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890"), "Bershka", "BSK-ANK-CNK-01", "Ankara", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Cankaya", true, "Bershka Armada AVM" },
                    { new Guid("d1111111-0000-0000-0000-000000000004"), new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890"), "Bershka", "BSK-IZM-BRN-01", "Izmir", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Bornova", true, "Bershka Forum Bornova" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Stores_BrandId_City_District",
                table: "Stores",
                columns: new[] { "BrandId", "City", "District" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Stores");
        }
    }
}
