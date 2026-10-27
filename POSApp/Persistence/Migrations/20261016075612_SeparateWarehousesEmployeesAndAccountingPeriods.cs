using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace POSApp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeparateWarehousesEmployeesAndAccountingPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StaffAccounts_AspNetUsers_UserId",
                table: "StaffAccounts");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "StaffAccounts",
                newName: "EmployeeId");

            migrationBuilder.RenameIndex(
                name: "IX_StaffAccounts_UserId",
                table: "StaffAccounts",
                newName: "IX_StaffAccounts_EmployeeId");

            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "SupplierBalanceEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CanSell",
                table: "StockLocations",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "MonthlySalaryId",
                table: "PayrollEntries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StockTransferId",
                table: "InventoryMovements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "CustomerBalanceEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "StockLocationId",
                table: "CashSessions",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<DateOnly>(
                name: "BusinessDate",
                table: "CashMovements",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.CreateTable(
                name: "AccountingSelections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingSelections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountingSelections_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "text", nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OpeningDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CurrentBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MonthlySalary = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinanceClosings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    OpeningCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OpeningBank = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CashIn = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CashOut = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BankIn = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BankOut = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosingCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosingBank = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Revenue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Cogs = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Expenses = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SalaryExpense = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SupplierPaid = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PayrollPaid = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SupplierDue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CustomerDue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    StockValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceClosings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockTransfers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceLocationId = table.Column<int>(type: "integer", nullable: false),
                    DestinationLocationId = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockTransfers_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransfers_StockLocations_DestinationLocationId",
                        column: x => x.DestinationLocationId,
                        principalTable: "StockLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransfers_StockLocations_SourceLocationId",
                        column: x => x.SourceLocationId,
                        principalTable: "StockLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankMovements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    BankAccountId = table.Column<int>(type: "integer", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    PaymentId = table.Column<int>(type: "integer", nullable: true),
                    SupplierPaymentId = table.Column<int>(type: "integer", nullable: true),
                    ExpenseId = table.Column<int>(type: "integer", nullable: true),
                    PayrollEntryId = table.Column<int>(type: "integer", nullable: true),
                    ReversesMovementId = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankMovements_BankAccounts_BankAccountId",
                        column: x => x.BankAccountId,
                        principalTable: "BankAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankMovements_BankMovements_ReversesMovementId",
                        column: x => x.ReversesMovementId,
                        principalTable: "BankMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankMovements_Expenses_ExpenseId",
                        column: x => x.ExpenseId,
                        principalTable: "Expenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankMovements_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankMovements_PayrollEntries_PayrollEntryId",
                        column: x => x.PayrollEntryId,
                        principalTable: "PayrollEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankMovements_SupplierPayments_SupplierPaymentId",
                        column: x => x.SupplierPaymentId,
                        principalTable: "SupplierPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MonthlySalaries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeId = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SettledAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlySalaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonthlySalaries_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierBalanceEntries_RequestId",
                table: "SupplierBalanceEntries",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollEntries_MonthlySalaryId",
                table: "PayrollEntries",
                column: "MonthlySalaryId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryMovements_StockTransferId",
                table: "InventoryMovements",
                column: "StockTransferId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBalanceEntries_RequestId",
                table: "CustomerBalanceEntries",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_BusinessDate",
                table: "CashMovements",
                column: "BusinessDate");

            migrationBuilder.CreateIndex(
                name: "IX_AccountingSelections_UserId",
                table: "AccountingSelections",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankAccounts_Code",
                table: "BankAccounts",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankMovements_BankAccountId",
                table: "BankMovements",
                column: "BankAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BankMovements_BusinessDate",
                table: "BankMovements",
                column: "BusinessDate");

            migrationBuilder.CreateIndex(
                name: "IX_BankMovements_ExpenseId",
                table: "BankMovements",
                column: "ExpenseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankMovements_PaymentId",
                table: "BankMovements",
                column: "PaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankMovements_PayrollEntryId",
                table: "BankMovements",
                column: "PayrollEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankMovements_RequestId",
                table: "BankMovements",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankMovements_ReversesMovementId",
                table: "BankMovements",
                column: "ReversesMovementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankMovements_SupplierPaymentId",
                table: "BankMovements",
                column: "SupplierPaymentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinanceClosings_Kind_Date",
                table: "FinanceClosings",
                columns: new[] { "Kind", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonthlySalaries_EmployeeId_Month",
                table: "MonthlySalaries",
                columns: new[] { "EmployeeId", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MonthlySalaries_OccurredAt",
                table: "MonthlySalaries",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_DestinationLocationId",
                table: "StockTransfers",
                column: "DestinationLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_ProductId",
                table: "StockTransfers",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_RequestId",
                table: "StockTransfers",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_SourceLocationId",
                table: "StockTransfers",
                column: "SourceLocationId");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryMovements_StockTransfers_StockTransferId",
                table: "InventoryMovements",
                column: "StockTransferId",
                principalTable: "StockTransfers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PayrollEntries_MonthlySalaries_MonthlySalaryId",
                table: "PayrollEntries",
                column: "MonthlySalaryId",
                principalTable: "MonthlySalaries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Preserve existing payroll as independent employees and settled salary records.
            // Cash and digital ledgers keep their original business dates on upgrade.
            migrationBuilder.Sql("""
                UPDATE "CashMovements"
                SET "BusinessDate" = ("CreatedAt" AT TIME ZONE 'Asia/Dhaka')::date;

                INSERT INTO "Employees" ("Id", "Name", "Phone", "MonthlySalary", "CreatedAt", "IsDeleted")
                SELECT account."EmployeeId", LEFT(COALESCE(person."UserName", 'Legacy employee'), 200), LEFT(person."PhoneNumber", 64), 0,
                    account."CreatedAt", FALSE
                FROM "StaffAccounts" AS account
                JOIN "AspNetUsers" AS person ON person."Id" = account."EmployeeId";

                SELECT setval(pg_get_serial_sequence('"Employees"', 'Id'),
                    COALESCE((SELECT MAX("Id") FROM "Employees"), 1), EXISTS (SELECT 1 FROM "Employees"));

                INSERT INTO "MonthlySalaries" ("EmployeeId", "Month", "OccurredAt", "Amount", "SettledAmount", "Notes", "CreatedAt")
                SELECT account."EmployeeId", entry."PayrollMonth",
                    entry."PayrollMonth"::timestamp AT TIME ZONE 'Asia/Dhaka',
                    SUM(entry."Amount"), SUM(entry."Amount"), 'Imported settled payroll', MIN(entry."CreatedAt")
                FROM "PayrollEntries" AS entry
                JOIN "StaffAccounts" AS account ON account."Id" = entry."StaffAccountId"
                WHERE entry."Kind" = 1
                GROUP BY account."EmployeeId", entry."PayrollMonth";

                UPDATE "PayrollEntries" AS entry
                SET "MonthlySalaryId" = salary."Id"
                FROM "StaffAccounts" AS account, "MonthlySalaries" AS salary
                WHERE entry."StaffAccountId" = account."Id" AND entry."Kind" = 1
                    AND salary."EmployeeId" = account."EmployeeId" AND salary."Month" = entry."PayrollMonth";

                INSERT INTO "BankAccounts" ("Code", "OpeningBalance", "CurrentBalance", "CreatedAt")
                VALUES ('MAIN', 0, 0, CURRENT_TIMESTAMP);

                WITH source AS (
                    SELECT 'sale-' || payment."Id" AS key, payment."Id" AS source_id, 1 AS source_order,
                        (payment."PaidAt" AT TIME ZONE 'Asia/Dhaka')::date AS business_date,
                        1 AS kind, payment."Method" AS method, payment."Amount" AS amount,
                        'Imported sale payment #' || payment."Id" AS notes,
                        payment."Id" AS payment_id, NULL::integer AS supplier_payment_id,
                        NULL::integer AS expense_id, NULL::integer AS payroll_id,
                        payment."CreatedAt" AS created_at, payment."CreatedByUserId" AS actor
                    FROM "Payments" AS payment
                    LEFT JOIN "Sales" AS sale ON sale."Id" = payment."SaleId"
                    WHERE payment."Method" <> 1 AND (sale."Id" IS NULL OR NOT sale."IsDeleted")
                    UNION ALL
                    SELECT 'supplier-' || payment."Id", payment."Id", 2,
                        (payment."PaidAt" AT TIME ZONE 'Asia/Dhaka')::date, 2, payment."Method", -payment."Amount",
                        'Imported supplier payment #' || payment."Id", NULL, payment."Id", NULL, NULL,
                        payment."CreatedAt", payment."CreatedByUserId"
                    FROM "SupplierPayments" AS payment
                    LEFT JOIN "Purchases" AS purchase ON purchase."Id" = payment."PurchaseId"
                    WHERE payment."Method" <> 1 AND (purchase."Id" IS NULL OR NOT purchase."IsDeleted")
                    UNION ALL
                    SELECT 'expense-' || expense."Id", expense."Id", 3,
                        (expense."OccurredAt" AT TIME ZONE 'Asia/Dhaka')::date, 3, expense."Method", -expense."Amount",
                        'Imported expense #' || expense."Id", NULL, NULL, expense."Id", NULL,
                        expense."CreatedAt", expense."CreatedByUserId"
                    FROM "Expenses" AS expense WHERE expense."Method" <> 1
                    UNION ALL
                    SELECT 'payroll-' || entry."Id", entry."Id", 4,
                        (entry."OccurredAt" AT TIME ZONE 'Asia/Dhaka')::date, 4, entry."Method", -entry."PaidAmount",
                        'Imported payroll #' || entry."Id", NULL, NULL, NULL, entry."Id",
                        entry."CreatedAt", entry."CreatedByUserId"
                    FROM "PayrollEntries" AS entry WHERE entry."Method" <> 1 AND entry."PaidAmount" <> 0
                )
                INSERT INTO "BankMovements" ("RequestId", "BankAccountId", "BusinessDate", "Kind", "Method", "Amount", "BalanceAfter",
                    "Notes", "PaymentId", "SupplierPaymentId", "ExpenseId", "PayrollEntryId", "CreatedAt", "CreatedByUserId")
                SELECT md5('jas-bank-upgrade-' || source.key)::uuid, account."Id", source.business_date, source.kind, source.method,
                    source.amount, SUM(source.amount) OVER (ORDER BY source.created_at, source.source_order, source.source_id),
                    source.notes, source.payment_id, source.supplier_payment_id, source.expense_id, source.payroll_id, source.created_at, source.actor
                FROM source CROSS JOIN "BankAccounts" AS account WHERE account."Code" = 'MAIN'
                ORDER BY source.created_at, source.source_order, source.source_id;

                UPDATE "BankAccounts" SET "CurrentBalance" = COALESCE((SELECT SUM("Amount") FROM "BankMovements"), 0)
                WHERE "Code" = 'MAIN';
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_StaffAccounts_Employees_EmployeeId",
                table: "StaffAccounts",
                column: "EmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Employees no longer map to login users and independent drawers have no required stock location. Restore a pre-upgrade backup for rollback; do not discard financial history.");
        }
    }
}
