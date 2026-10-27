using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POSApp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReportPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockTransfers_DestinationLocationId",
                table: "StockTransfers");

            migrationBuilder.DropIndex(
                name: "IX_StockTransfers_SourceLocationId",
                table: "StockTransfers");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_DestinationLocationId_OccurredAt",
                table: "StockTransfers",
                columns: new[] { "DestinationLocationId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_OccurredAt",
                table: "StockTransfers",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_SourceLocationId_OccurredAt",
                table: "StockTransfers",
                columns: new[] { "SourceLocationId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_ReceivedAt",
                table: "Purchases",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_StockLocationId_ReceivedAt",
                table: "Purchases",
                columns: new[] { "StockLocationId", "ReceivedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockTransfers_DestinationLocationId_OccurredAt",
                table: "StockTransfers");

            migrationBuilder.DropIndex(
                name: "IX_StockTransfers_OccurredAt",
                table: "StockTransfers");

            migrationBuilder.DropIndex(
                name: "IX_StockTransfers_SourceLocationId_OccurredAt",
                table: "StockTransfers");

            migrationBuilder.DropIndex(
                name: "IX_Purchases_ReceivedAt",
                table: "Purchases");

            migrationBuilder.DropIndex(
                name: "IX_Purchases_StockLocationId_ReceivedAt",
                table: "Purchases");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_DestinationLocationId",
                table: "StockTransfers",
                column: "DestinationLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_SourceLocationId",
                table: "StockTransfers",
                column: "SourceLocationId");
        }
    }
}
