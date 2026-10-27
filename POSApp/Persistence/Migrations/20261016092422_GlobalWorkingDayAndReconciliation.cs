using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace POSApp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GlobalWorkingDayAndReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AdvanceCreated",
                table: "PayrollEntries",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "AdvancesPaid",
                table: "FinanceClosings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CashVariance",
                table: "FinanceClosings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CustomerCollections",
                table: "FinanceClosings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PurchaseTotal",
                table: "FinanceClosings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SalesReceipts",
                table: "FinanceClosings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "DayFinances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    OpeningCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ExpectedCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CountedCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Difference = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinanceClosingId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DayFinances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DayFinances_FinanceClosings_FinanceClosingId",
                        column: x => x.FinanceClosingId,
                        principalTable: "FinanceClosings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkingDays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkingDays", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DayFinances_Date",
                table: "DayFinances",
                column: "Date",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DayFinances_FinanceClosingId",
                table: "DayFinances",
                column: "FinanceClosingId",
                unique: true);
            // Preserve legacy history while choosing one shared date from the latest saved selection.
            migrationBuilder.Sql("""
                INSERT INTO "WorkingDays" ("Id", "Date")
                SELECT 1, MAX("Date") FROM "AccountingSelections" HAVING COUNT(*) > 0;

                INSERT INTO "DayFinances"
                    ("Date", "OpeningCash", "ExpectedCash", "CountedCash", "Difference", "ClosedAt",
                     "FinanceClosingId", "CreatedAt", "UpdatedAt", "CreatedByUserId", "UpdatedByUserId")
                SELECT "Date", "OpeningCash", "ClosingCash", NULL, NULL, "ClosedAt", "Id",
                       "CreatedAt", "UpdatedAt", "CreatedByUserId", "UpdatedByUserId"
                FROM "FinanceClosings"
                WHERE "Kind" = 1 AND ("ClosedAt" IS NOT NULL OR "Date" = (SELECT "Date" FROM "WorkingDays" WHERE "Id" = 1));

                INSERT INTO "DayFinances" ("Date", "OpeningCash", "ExpectedCash", "CreatedAt")
                SELECT w."Date", COALESCE(p."ClosingCash", 0), COALESCE(p."ClosingCash", 0), now()
                FROM "WorkingDays" w
                LEFT JOIN LATERAL (
                    SELECT "ClosingCash" FROM "FinanceClosings"
                    WHERE "Kind" = 1 AND "ClosedAt" IS NOT NULL AND "Date" < w."Date"
                    ORDER BY "Date" DESC LIMIT 1
                ) p ON TRUE
                WHERE NOT EXISTS (SELECT 1 FROM "DayFinances" d WHERE d."Date" = w."Date");

                UPDATE "PayrollEntries" SET "AdvanceCreated" = "PaidAmount" WHERE "Kind" = 2;

                UPDATE "FinanceClosings" f SET
                    "SalaryExpense" = f."PayrollPaid",
                    "AdvancesPaid" = COALESCE((
                        SELECT SUM(p."AdvanceCreated") FROM "PayrollEntries" p
                        WHERE (p."OccurredAt" AT TIME ZONE 'Asia/Dhaka')::date >= f."Date"
                          AND (p."OccurredAt" AT TIME ZONE 'Asia/Dhaka')::date <
                              CASE WHEN f."Kind" = 2 THEN (f."Date" + interval '1 month')::date ELSE f."Date" + 1 END
                    ), 0),
                    "PurchaseTotal" = COALESCE((
                        SELECT SUM(p."TotalAmount") FROM "Purchases" p
                        WHERE NOT p."IsDeleted" AND (p."ReceivedAt" AT TIME ZONE 'Asia/Dhaka')::date >= f."Date"
                          AND (p."ReceivedAt" AT TIME ZONE 'Asia/Dhaka')::date <
                              CASE WHEN f."Kind" = 2 THEN (f."Date" + interval '1 month')::date ELSE f."Date" + 1 END
                    ), 0);

                WITH receipts AS (
                    SELECT m."BusinessDate", m."Amount", m."Kind", p."SaleId", m."PaymentId"
                    FROM "CashMovements" m LEFT JOIN "Payments" p ON p."Id" = m."PaymentId"
                    WHERE m."ReversesMovementId" IS NULL
                      AND NOT EXISTS (SELECT 1 FROM "CashMovements" r WHERE r."ReversesMovementId" = m."Id")
                    UNION ALL
                    SELECT m."BusinessDate", m."Amount", m."Kind", p."SaleId", m."PaymentId"
                    FROM "BankMovements" m LEFT JOIN "Payments" p ON p."Id" = m."PaymentId"
                    WHERE m."ReversesMovementId" IS NULL
                      AND NOT EXISTS (SELECT 1 FROM "BankMovements" r WHERE r."ReversesMovementId" = m."Id")
                )
                UPDATE "FinanceClosings" f SET
                    "SalesReceipts" = COALESCE((SELECT SUM(r."Amount") FROM receipts r
                        WHERE r."Kind" = 1 AND r."SaleId" IS NOT NULL AND r."BusinessDate" >= f."Date"
                          AND r."BusinessDate" < CASE WHEN f."Kind" = 2 THEN (f."Date" + interval '1 month')::date ELSE f."Date" + 1 END), 0),
                    "CustomerCollections" = COALESCE((SELECT SUM(r."Amount") FROM receipts r
                        WHERE r."PaymentId" IS NOT NULL AND r."SaleId" IS NULL AND r."BusinessDate" >= f."Date"
                          AND r."BusinessDate" < CASE WHEN f."Kind" = 2 THEN (f."Date" + interval '1 month')::date ELSE f."Date" + 1 END), 0);
                """);

            migrationBuilder.DropTable(name: "AccountingSelections");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Restoring per-user working dates would discard shared daily cash counts and reconciliation history. Restore a pre-upgrade database backup instead.");
        }
    }
}
