using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POSApp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditVoidTimezoneAndReorderLevel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsVoided",
                table: "SupplierPayments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "SupplierPayments",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VoidedAt",
                table: "SupplierPayments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoidedByUserId",
                table: "SupplierPayments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsVoided",
                table: "StockTransfers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "StockTransfers",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VoidedAt",
                table: "StockTransfers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoidedByUserId",
                table: "StockTransfers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsVoided",
                table: "StockTakes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "StockTakes",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VoidedAt",
                table: "StockTakes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoidedByUserId",
                table: "StockTakes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsVoided",
                table: "StockAdjustments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "StockAdjustments",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VoidedAt",
                table: "StockAdjustments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoidedByUserId",
                table: "StockAdjustments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OriginalSaleId",
                table: "Sales",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReorderLevel",
                table: "Products",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<bool>(
                name: "IsVoided",
                table: "PayrollEntries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "PayrollEntries",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VoidedAt",
                table: "PayrollEntries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoidedByUserId",
                table: "PayrollEntries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsVoided",
                table: "Payments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "Payments",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VoidedAt",
                table: "Payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoidedByUserId",
                table: "Payments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsVoided",
                table: "Expenses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "Expenses",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VoidedAt",
                table: "Expenses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoidedByUserId",
                table: "Expenses",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsVoided",
                table: "DebtEntries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "DebtEntries",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VoidedAt",
                table: "DebtEntries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoidedByUserId",
                table: "DebtEntries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsVoided",
                table: "CashTransfers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "VoidReason",
                table: "CashTransfers",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "VoidedAt",
                table: "CashTransfers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VoidedByUserId",
                table: "CashTransfers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "AspNetUsers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sales_OriginalSaleId",
                table: "Sales",
                column: "OriginalSaleId");

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_Sales_OriginalSaleId",
                table: "Sales",
                column: "OriginalSaleId",
                principalTable: "Sales",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sales_Sales_OriginalSaleId",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Sales_OriginalSaleId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "IsVoided",
                table: "SupplierPayments");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "SupplierPayments");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                table: "SupplierPayments");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                table: "SupplierPayments");

            migrationBuilder.DropColumn(
                name: "IsVoided",
                table: "StockTransfers");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "StockTransfers");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                table: "StockTransfers");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                table: "StockTransfers");

            migrationBuilder.DropColumn(
                name: "IsVoided",
                table: "StockTakes");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "StockTakes");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                table: "StockTakes");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                table: "StockTakes");

            migrationBuilder.DropColumn(
                name: "IsVoided",
                table: "StockAdjustments");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "StockAdjustments");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                table: "StockAdjustments");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                table: "StockAdjustments");

            migrationBuilder.DropColumn(
                name: "OriginalSaleId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "ReorderLevel",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "IsVoided",
                table: "PayrollEntries");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "PayrollEntries");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                table: "PayrollEntries");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                table: "PayrollEntries");

            migrationBuilder.DropColumn(
                name: "IsVoided",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "IsVoided",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "IsVoided",
                table: "DebtEntries");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "DebtEntries");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                table: "DebtEntries");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                table: "DebtEntries");

            migrationBuilder.DropColumn(
                name: "IsVoided",
                table: "CashTransfers");

            migrationBuilder.DropColumn(
                name: "VoidReason",
                table: "CashTransfers");

            migrationBuilder.DropColumn(
                name: "VoidedAt",
                table: "CashTransfers");

            migrationBuilder.DropColumn(
                name: "VoidedByUserId",
                table: "CashTransfers");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "AspNetUsers");
        }
    }
}
