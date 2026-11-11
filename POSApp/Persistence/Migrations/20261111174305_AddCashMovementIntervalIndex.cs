using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POSApp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCashMovementIntervalIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_CashDrawerId_BusinessDate_CreatedAt_Id",
                table: "CashMovements",
                columns: new[] { "CashDrawerId", "BusinessDate", "CreatedAt", "Id" })
                .Annotation("Npgsql:IndexInclude", new[] { "Amount", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_CashCounts_CashDrawerId_BusinessDate_CountedAt_Id",
                table: "CashCounts",
                columns: new[] { "CashDrawerId", "BusinessDate", "CountedAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CashMovements_CashDrawerId_BusinessDate_CreatedAt_Id",
                table: "CashMovements");

            migrationBuilder.DropIndex(
                name: "IX_CashCounts_CashDrawerId_BusinessDate_CountedAt_Id",
                table: "CashCounts");
        }
    }
}
