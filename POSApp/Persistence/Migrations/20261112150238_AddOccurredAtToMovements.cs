using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POSApp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOccurredAtToMovements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CashMovements_CashDrawerId_BusinessDate_CreatedAt_Id",
                table: "CashMovements");

            migrationBuilder.DropIndex(
                name: "IX_CashMovements_CashDrawerId_CreatedAt",
                table: "CashMovements");

            migrationBuilder.DropIndex(
                name: "IX_BankMovements_BankAccountId",
                table: "BankMovements");

            migrationBuilder.DropColumn(
                name: "RecordedAt",
                table: "CashCounts");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OccurredAt",
                table: "DebtEntries",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OccurredAt",
                table: "CashMovements",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OccurredAt",
                table: "BankMovements",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.Sql("UPDATE \"CashMovements\" SET \"OccurredAt\" = \"CreatedAt\" WHERE \"OccurredAt\" = '0001-01-01 00:00:00+00';");
            migrationBuilder.Sql("UPDATE \"BankMovements\" SET \"OccurredAt\" = \"CreatedAt\" WHERE \"OccurredAt\" = '0001-01-01 00:00:00+00';");
            migrationBuilder.Sql("UPDATE \"DebtEntries\" SET \"OccurredAt\" = \"CreatedAt\" WHERE \"OccurredAt\" = '0001-01-01 00:00:00+00';");

            migrationBuilder.CreateIndex(
                name: "IX_DebtEntries_OccurredAt",
                table: "DebtEntries",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_CashDrawerId_BusinessDate_OccurredAt_Id",
                table: "CashMovements",
                columns: new[] { "CashDrawerId", "BusinessDate", "OccurredAt", "Id" })
                .Annotation("Npgsql:IndexInclude", new[] { "Amount", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_CashDrawerId_OccurredAt",
                table: "CashMovements",
                columns: new[] { "CashDrawerId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_OccurredAt",
                table: "CashMovements",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_BankMovements_BankAccountId_OccurredAt",
                table: "BankMovements",
                columns: new[] { "BankAccountId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BankMovements_OccurredAt",
                table: "BankMovements",
                column: "OccurredAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DebtEntries_OccurredAt",
                table: "DebtEntries");

            migrationBuilder.DropIndex(
                name: "IX_CashMovements_CashDrawerId_BusinessDate_OccurredAt_Id",
                table: "CashMovements");

            migrationBuilder.DropIndex(
                name: "IX_CashMovements_CashDrawerId_OccurredAt",
                table: "CashMovements");

            migrationBuilder.DropIndex(
                name: "IX_CashMovements_OccurredAt",
                table: "CashMovements");

            migrationBuilder.DropIndex(
                name: "IX_BankMovements_BankAccountId_OccurredAt",
                table: "BankMovements");

            migrationBuilder.DropIndex(
                name: "IX_BankMovements_OccurredAt",
                table: "BankMovements");

            migrationBuilder.DropColumn(
                name: "OccurredAt",
                table: "DebtEntries");

            migrationBuilder.DropColumn(
                name: "OccurredAt",
                table: "CashMovements");

            migrationBuilder.DropColumn(
                name: "OccurredAt",
                table: "BankMovements");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RecordedAt",
                table: "CashCounts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_CashDrawerId_BusinessDate_CreatedAt_Id",
                table: "CashMovements",
                columns: new[] { "CashDrawerId", "BusinessDate", "CreatedAt", "Id" })
                .Annotation("Npgsql:IndexInclude", new[] { "Amount", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_CashDrawerId_CreatedAt",
                table: "CashMovements",
                columns: new[] { "CashDrawerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BankMovements_BankAccountId",
                table: "BankMovements",
                column: "BankAccountId");
        }
    }
}
