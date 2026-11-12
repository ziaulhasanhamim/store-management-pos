using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POSApp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBusinessDateFromMovements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CashMovements_BusinessDate",
                table: "CashMovements");

            migrationBuilder.DropIndex(
                name: "IX_CashMovements_CashDrawerId_BusinessDate_OccurredAt_Id",
                table: "CashMovements");

            migrationBuilder.DropIndex(
                name: "IX_BankMovements_BusinessDate",
                table: "BankMovements");

            // 1. Repair CashMovements with ancient OccurredAt from linked Sales
            migrationBuilder.Sql(@"
                UPDATE ""CashMovements"" m
                SET ""OccurredAt"" = s.""SoldAt""
                FROM ""Payments"" p
                JOIN ""Sales"" s ON p.""SaleId"" = s.""Id""
                WHERE m.""PaymentId"" = p.""Id"" AND m.""OccurredAt"" < '2020-01-01' AND s.""SoldAt"" >= '2020-01-01';
            ");

            // 2. Repair CashMovements with ancient OccurredAt from linked SupplierPayments
            migrationBuilder.Sql(@"
                UPDATE ""CashMovements"" m
                SET ""OccurredAt"" = sp.""PaidAt""
                FROM ""SupplierPayments"" sp
                WHERE m.""SupplierPaymentId"" = sp.""Id"" AND m.""OccurredAt"" < '2020-01-01' AND sp.""PaidAt"" >= '2020-01-01';
            ");

            // 3. Fallback: Repair any remaining ancient CashMovements using BusinessDate + CreatedAt time in Asia/Dhaka
            migrationBuilder.Sql(@"
                UPDATE ""CashMovements""
                SET ""OccurredAt"" = (""BusinessDate""::text || ' ' || to_char(""CreatedAt"" AT TIME ZONE 'Asia/Dhaka', 'HH24:MI:SS.US'))::timestamp AT TIME ZONE 'Asia/Dhaka'
                WHERE ""OccurredAt"" < '2020-01-01';
            ");

            // 4. Ensure monotonic ordering with millisecond spacing based on Id
            migrationBuilder.Sql(@"
                UPDATE ""CashMovements""
                SET ""OccurredAt"" = ""OccurredAt"" + (""Id"" % 1000) * interval '1 millisecond'
                WHERE ""OccurredAt"" >= '2020-01-01';
            ");

            // 5. Repair BankMovements with ancient OccurredAt from linked Sales
            migrationBuilder.Sql(@"
                UPDATE ""BankMovements"" m
                SET ""OccurredAt"" = s.""SoldAt""
                FROM ""Payments"" p
                JOIN ""Sales"" s ON p.""SaleId"" = s.""Id""
                WHERE m.""PaymentId"" = p.""Id"" AND m.""OccurredAt"" < '2020-01-01' AND s.""SoldAt"" >= '2020-01-01';
            ");

            // 6. Repair BankMovements with ancient OccurredAt from linked SupplierPayments
            migrationBuilder.Sql(@"
                UPDATE ""BankMovements"" m
                SET ""OccurredAt"" = sp.""PaidAt""
                FROM ""SupplierPayments"" sp
                WHERE m.""SupplierPaymentId"" = sp.""Id"" AND m.""OccurredAt"" < '2020-01-01' AND sp.""PaidAt"" >= '2020-01-01';
            ");

            // 7. Fallback: Repair any remaining ancient BankMovements using BusinessDate + CreatedAt time in Asia/Dhaka
            migrationBuilder.Sql(@"
                UPDATE ""BankMovements""
                SET ""OccurredAt"" = (""BusinessDate""::text || ' ' || to_char(""CreatedAt"" AT TIME ZONE 'Asia/Dhaka', 'HH24:MI:SS.US'))::timestamp AT TIME ZONE 'Asia/Dhaka'
                WHERE ""OccurredAt"" < '2020-01-01';
            ");

            // 8. Ensure monotonic ordering for BankMovements
            migrationBuilder.Sql(@"
                UPDATE ""BankMovements""
                SET ""OccurredAt"" = ""OccurredAt"" + (""Id"" % 1000) * interval '1 millisecond'
                WHERE ""OccurredAt"" >= '2020-01-01';
            ");

            migrationBuilder.DropColumn(
                name: "BusinessDate",
                table: "CashMovements");

            migrationBuilder.DropColumn(
                name: "BusinessDate",
                table: "BankMovements");

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_CashDrawerId_OccurredAt_Id",
                table: "CashMovements",
                columns: new[] { "CashDrawerId", "OccurredAt", "Id" })
                .Annotation("Npgsql:IndexInclude", new[] { "Amount", "Kind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CashMovements_CashDrawerId_OccurredAt_Id",
                table: "CashMovements");

            migrationBuilder.AddColumn<DateOnly>(
                name: "BusinessDate",
                table: "CashMovements",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<DateOnly>(
                name: "BusinessDate",
                table: "BankMovements",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_BusinessDate",
                table: "CashMovements",
                column: "BusinessDate");

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_CashDrawerId_BusinessDate_OccurredAt_Id",
                table: "CashMovements",
                columns: new[] { "CashDrawerId", "BusinessDate", "OccurredAt", "Id" })
                .Annotation("Npgsql:IndexInclude", new[] { "Amount", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_BankMovements_BusinessDate",
                table: "BankMovements",
                column: "BusinessDate");
        }
    }
}
