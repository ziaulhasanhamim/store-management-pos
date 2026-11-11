using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace POSApp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveWorkingDaysAndClosingTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DayFinances");

            migrationBuilder.DropTable(
                name: "WorkingDays");

            migrationBuilder.DropTable(
                name: "FinanceClosings");

            migrationBuilder.DropIndex(
                name: "IX_CashSessions_CashDrawerId_BusinessDate",
                table: "CashSessions");

            migrationBuilder.AlterColumn<int>(
                name: "CashSessionId",
                table: "CashMovements",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<decimal>(
                name: "BalanceAfter",
                table: "CashMovements",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "CashDrawerId",
                table: "CashMovements",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastCountedAt",
                table: "CashDrawers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LastCountedCash",
                table: "CashDrawers",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE ""CashMovements"" m
                SET ""CashDrawerId"" = s.""CashDrawerId""
                FROM ""CashSessions"" s
                WHERE m.""CashSessionId"" = s.""Id"" AND m.""CashDrawerId"" = 0;

                UPDATE ""CashMovements"" m
                SET ""CashDrawerId"" = COALESCE(
                    (SELECT ""Id"" FROM ""CashDrawers"" ORDER BY ""Id"" LIMIT 1),
                    1
                )
                WHERE m.""CashDrawerId"" = 0;
            ");

            migrationBuilder.Sql(@"
                UPDATE ""CashDrawers"" d
                SET ""LastCountedAt"" = sub.""ClosedAt"", ""LastCountedCash"" = sub.""ActualClosingCash""
                FROM (
                    SELECT DISTINCT ON (""CashDrawerId"") ""CashDrawerId"", ""ClosedAt"", ""ActualClosingCash""
                    FROM ""CashSessions""
                    WHERE ""ClosedAt"" IS NOT NULL AND ""ActualClosingCash"" IS NOT NULL
                    ORDER BY ""CashDrawerId"", ""ClosedAt"" DESC
                ) sub
                WHERE d.""Id"" = sub.""CashDrawerId"" AND d.""LastCountedAt"" IS NULL;
            ");

            migrationBuilder.CreateTable(
                name: "CashCounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashDrawerId = table.Column<int>(type: "integer", nullable: false),
                    StockLocationId = table.Column<int>(type: "integer", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CountedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CountedByUserId = table.Column<int>(type: "integer", nullable: false),
                    CountedCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ExpectedCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedByUserId = table.Column<int>(type: "integer", nullable: true),
                    VarianceAcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VarianceAcceptedByUserId = table.Column<int>(type: "integer", nullable: true),
                    VarianceMovementId = table.Column<int>(type: "integer", nullable: true),
                    IsVoided = table.Column<bool>(type: "boolean", nullable: false),
                    VoidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VoidedByUserId = table.Column<int>(type: "integer", nullable: true),
                    VoidReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashCounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashCounts_AspNetUsers_CountedByUserId",
                        column: x => x.CountedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashCounts_CashDrawers_CashDrawerId",
                        column: x => x.CashDrawerId,
                        principalTable: "CashDrawers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashCounts_CashMovements_VarianceMovementId",
                        column: x => x.VarianceMovementId,
                        principalTable: "CashMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashCounts_StockLocations_StockLocationId",
                        column: x => x.StockLocationId,
                        principalTable: "StockLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_CashDrawerId_CreatedAt",
                table: "CashMovements",
                columns: new[] { "CashDrawerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CashCounts_CashDrawerId_CountedAt",
                table: "CashCounts",
                columns: new[] { "CashDrawerId", "CountedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CashCounts_CountedByUserId",
                table: "CashCounts",
                column: "CountedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CashCounts_RequestId",
                table: "CashCounts",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashCounts_StockLocationId_BusinessDate",
                table: "CashCounts",
                columns: new[] { "StockLocationId", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CashCounts_VarianceMovementId",
                table: "CashCounts",
                column: "VarianceMovementId");

            migrationBuilder.AddForeignKey(
                name: "FK_CashMovements_CashDrawers_CashDrawerId",
                table: "CashMovements",
                column: "CashDrawerId",
                principalTable: "CashDrawers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CashMovements_CashDrawers_CashDrawerId",
                table: "CashMovements");

            migrationBuilder.DropTable(
                name: "CashCounts");

            migrationBuilder.DropIndex(
                name: "IX_CashMovements_CashDrawerId_CreatedAt",
                table: "CashMovements");

            migrationBuilder.DropColumn(
                name: "BalanceAfter",
                table: "CashMovements");

            migrationBuilder.DropColumn(
                name: "CashDrawerId",
                table: "CashMovements");

            migrationBuilder.DropColumn(
                name: "LastCountedAt",
                table: "CashDrawers");

            migrationBuilder.DropColumn(
                name: "LastCountedCash",
                table: "CashDrawers");

            migrationBuilder.AlterColumn<int>(
                name: "CashSessionId",
                table: "CashMovements",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "FinanceClosings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AdvancesPaid = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AssetBuilt = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BankIn = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BankOut = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CashIn = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CashOut = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CashVariance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosingBank = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosingCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosingCustomerDue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosingDebt = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosingNetAssets = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosingStock = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosingSupplierDue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Cogs = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    CustomerCollections = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CustomerDue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    DebtBorrowed = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DebtRepaid = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Expenses = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    MonthlyDetailsJson = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    OpeningAssetsCustomized = table.Column<bool>(type: "boolean", nullable: false),
                    OpeningBank = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OpeningCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OpeningCustomerDue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OpeningDebt = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OpeningNetAssets = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OpeningStock = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OpeningSupplierDue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OutstandingDebt = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OwnerContributions = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OwnerWithdrawals = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PayrollPaid = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PurchaseTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Revenue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SalaryExpense = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SalesReceipts = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    StockValue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    StockVariance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SupplierDue = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SupplierPaid = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceClosings", x => x.Id);
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

            migrationBuilder.CreateTable(
                name: "DayFinances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FinanceClosingId = table.Column<int>(type: "integer", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CountedCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Difference = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ExpectedCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OpeningCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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

            migrationBuilder.CreateIndex(
                name: "IX_CashSessions_CashDrawerId_BusinessDate",
                table: "CashSessions",
                columns: new[] { "CashDrawerId", "BusinessDate" });

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

            migrationBuilder.CreateIndex(
                name: "IX_FinanceClosings_Kind_Date",
                table: "FinanceClosings",
                columns: new[] { "Kind", "Date" },
                unique: true);
        }
    }
}
