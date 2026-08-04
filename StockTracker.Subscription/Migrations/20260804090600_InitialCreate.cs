using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockTracker.Subscription.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WatchGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Size = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastCheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastKnownStatus = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserWatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    WatchGroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserWatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserWatches_WatchGroups_WatchGroupId",
                        column: x => x.WatchGroupId,
                        principalTable: "WatchGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserWatches_UserId_WatchGroupId",
                table: "UserWatches",
                columns: new[] { "UserId", "WatchGroupId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserWatches_WatchGroupId",
                table: "UserWatches",
                column: "WatchGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_WatchGroups_ProductCode_Size_StoreId",
                table: "WatchGroups",
                columns: new[] { "ProductCode", "Size", "StoreId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserWatches");

            migrationBuilder.DropTable(
                name: "WatchGroups");
        }
    }
}
