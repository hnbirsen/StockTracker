using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockTracker.Notification.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Size = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: true),
                    Channel = table.Column<string>(type: "text", nullable: false),
                    CommandId = table.Column<Guid>(type: "uuid", nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WatchGroupNotificationStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Size = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StoreId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastKnownStatus = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchGroupNotificationStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_CommandId_UserId_Channel",
                table: "NotificationLogs",
                columns: new[] { "CommandId", "UserId", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WatchGroupNotificationStates_ProductCode_Size_StoreId",
                table: "WatchGroupNotificationStates",
                columns: new[] { "ProductCode", "Size", "StoreId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NotificationLogs");

            migrationBuilder.DropTable(
                name: "WatchGroupNotificationStates");
        }
    }
}
