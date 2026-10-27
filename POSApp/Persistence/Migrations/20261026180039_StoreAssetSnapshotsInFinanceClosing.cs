using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POSApp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StoreAssetSnapshotsInFinanceClosing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AssetBuilt",
                table: "FinanceClosings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ClosingCustomerDue",
                table: "FinanceClosings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ClosingDebt",
                table: "FinanceClosings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ClosingNetAssets",
                table: "FinanceClosings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ClosingStock",
                table: "FinanceClosings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ClosingSupplierDue",
                table: "FinanceClosings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "OpeningAssetsCustomized",
                table: "FinanceClosings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningCustomerDue",
                table: "FinanceClosings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningDebt",
                table: "FinanceClosings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningNetAssets",
                table: "FinanceClosings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningStock",
                table: "FinanceClosings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningSupplierDue",
                table: "FinanceClosings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssetBuilt",
                table: "FinanceClosings");

            migrationBuilder.DropColumn(
                name: "ClosingCustomerDue",
                table: "FinanceClosings");

            migrationBuilder.DropColumn(
                name: "ClosingDebt",
                table: "FinanceClosings");

            migrationBuilder.DropColumn(
                name: "ClosingNetAssets",
                table: "FinanceClosings");

            migrationBuilder.DropColumn(
                name: "ClosingStock",
                table: "FinanceClosings");

            migrationBuilder.DropColumn(
                name: "ClosingSupplierDue",
                table: "FinanceClosings");

            migrationBuilder.DropColumn(
                name: "OpeningAssetsCustomized",
                table: "FinanceClosings");

            migrationBuilder.DropColumn(
                name: "OpeningCustomerDue",
                table: "FinanceClosings");

            migrationBuilder.DropColumn(
                name: "OpeningDebt",
                table: "FinanceClosings");

            migrationBuilder.DropColumn(
                name: "OpeningNetAssets",
                table: "FinanceClosings");

            migrationBuilder.DropColumn(
                name: "OpeningStock",
                table: "FinanceClosings");

            migrationBuilder.DropColumn(
                name: "OpeningSupplierDue",
                table: "FinanceClosings");
        }
    }
}
