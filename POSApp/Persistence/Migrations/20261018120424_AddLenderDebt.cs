using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace POSApp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLenderDebt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DebtBorrowed",
                table: "FinanceClosings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DebtRepaid",
                table: "FinanceClosings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OutstandingDebt",
                table: "FinanceClosings",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "DebtEntryId",
                table: "CashMovements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DebtEntryId",
                table: "BankMovements",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Lenders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lenders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LenderBalances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LenderId = table.Column<int>(type: "integer", nullable: false),
                    OpeningDate = table.Column<DateOnly>(type: "date", nullable: false),
                    OpeningBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrentBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LenderBalances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LenderBalances_Lenders_LenderId",
                        column: x => x.LenderId,
                        principalTable: "Lenders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DebtEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    LenderBalanceId = table.Column<int>(type: "integer", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: true),
                    CashLocationId = table.Column<int>(type: "integer", nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DebtEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DebtEntries_LenderBalances_LenderBalanceId",
                        column: x => x.LenderBalanceId,
                        principalTable: "LenderBalances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DebtEntries_StockLocations_CashLocationId",
                        column: x => x.CashLocationId,
                        principalTable: "StockLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_DebtEntryId",
                table: "CashMovements",
                column: "DebtEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankMovements_DebtEntryId",
                table: "BankMovements",
                column: "DebtEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DebtEntries_BusinessDate",
                table: "DebtEntries",
                column: "BusinessDate");

            migrationBuilder.CreateIndex(
                name: "IX_DebtEntries_CashLocationId",
                table: "DebtEntries",
                column: "CashLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_DebtEntries_LenderBalanceId_BusinessDate",
                table: "DebtEntries",
                columns: new[] { "LenderBalanceId", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_DebtEntries_RequestId",
                table: "DebtEntries",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LenderBalances_LenderId",
                table: "LenderBalances",
                column: "LenderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lenders_RequestId",
                table: "Lenders",
                column: "RequestId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BankMovements_DebtEntries_DebtEntryId",
                table: "BankMovements",
                column: "DebtEntryId",
                principalTable: "DebtEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CashMovements_DebtEntries_DebtEntryId",
                table: "CashMovements",
                column: "DebtEntryId",
                principalTable: "DebtEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BankMovements_DebtEntries_DebtEntryId",
                table: "BankMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_CashMovements_DebtEntries_DebtEntryId",
                table: "CashMovements");

            migrationBuilder.DropTable(
                name: "DebtEntries");

            migrationBuilder.DropTable(
                name: "LenderBalances");

            migrationBuilder.DropTable(
                name: "Lenders");

            migrationBuilder.DropIndex(
                name: "IX_CashMovements_DebtEntryId",
                table: "CashMovements");

            migrationBuilder.DropIndex(
                name: "IX_BankMovements_DebtEntryId",
                table: "BankMovements");

            migrationBuilder.DropColumn(
                name: "DebtBorrowed",
                table: "FinanceClosings");

            migrationBuilder.DropColumn(
                name: "DebtRepaid",
                table: "FinanceClosings");

            migrationBuilder.DropColumn(
                name: "OutstandingDebt",
                table: "FinanceClosings");

            migrationBuilder.DropColumn(
                name: "DebtEntryId",
                table: "CashMovements");

            migrationBuilder.DropColumn(
                name: "DebtEntryId",
                table: "BankMovements");
        }
    }
}
