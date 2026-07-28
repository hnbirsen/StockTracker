using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace StockTracker.BrandDetection.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BrandCodeSignatures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BrandId = table.Column<Guid>(type: "uuid", nullable: false),
                    BrandName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RegexPattern = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Confidence = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrandCodeSignatures", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "BrandCodeSignatures",
                columns: new[] { "Id", "BrandId", "BrandName", "Confidence", "CreatedAt", "IsActive", "RegexPattern" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111111"), new Guid("a1b2c3d4-e5f6-7890-abcd-ef1234567890"), "Bershka", 2, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "^\\d{7,9}$" },
                    { new Guid("22222222-2222-2222-2222-222222222222"), new Guid("b2c3d4e5-f6a7-8901-bcde-f12345678901"), "Zara", 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "^\\d{5}/\\d{3}/\\d{2,3}$" },
                    { new Guid("33333333-3333-3333-3333-333333333333"), new Guid("c3d4e5f6-a7b8-9012-cdef-123456789012"), "Pull&Bear", 1, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "^\\d{8}$" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BrandCodeSignatures");
        }
    }
}
