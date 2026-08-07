using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockTracker.Product.Migrations
{
    /// <inheritdoc />
    public partial class AddProductUrlIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ProductBrandMaps_ProductUrl",
                table: "ProductBrandMaps",
                column: "ProductUrl");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductBrandMaps_ProductUrl",
                table: "ProductBrandMaps");
        }
    }
}
