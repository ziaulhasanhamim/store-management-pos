using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace POSApp.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BatchMoneyEntriesAndExpenseCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CashMovements_ExpenseId",
                table: "CashMovements");

            migrationBuilder.DropIndex(
                name: "IX_CashMovements_PaymentId",
                table: "CashMovements");

            migrationBuilder.DropIndex(
                name: "IX_CashMovements_SupplierPaymentId",
                table: "CashMovements");

            migrationBuilder.DropIndex(
                name: "IX_BankMovements_ExpenseId",
                table: "BankMovements");

            migrationBuilder.DropIndex(
                name: "IX_BankMovements_PaymentId",
                table: "BankMovements");

            migrationBuilder.DropIndex(
                name: "IX_BankMovements_SupplierPaymentId",
                table: "BankMovements");

            migrationBuilder.AddColumn<int>(
                name: "CategoryDefinitionId",
                table: "Expenses",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExpenseCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BuiltInCategory = table.Column<int>(type: "integer", nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinancialEntryBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    WorkingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ResultJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialEntryBatches", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "ExpenseCategories",
                columns: new[] { "Id", "BuiltInCategory", "IsArchived", "Name" },
                values: new object[,]
                {
                    { -7, 7, false, "Other" },
                    { -6, 6, false, "Maintenance" },
                    { -5, 5, false, "Rent" },
                    { -4, 4, false, "Packaging" },
                    { -3, 3, false, "Transport" },
                    { -2, 2, false, "Snacks / refreshments" },
                    { -1, 1, false, "Utilities" }
                });

            migrationBuilder.Sql("""
                UPDATE "Expenses"
                SET "CategoryDefinitionId" = -"Category"
                WHERE "CategoryDefinitionId" IS NULL
                    AND EXISTS (SELECT 1 FROM "ExpenseCategories" WHERE "Id" = -"Expenses"."Category");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CategoryDefinitionId",
                table: "Expenses",
                column: "CategoryDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_ExpenseId",
                table: "CashMovements",
                column: "ExpenseId");

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_PaymentId",
                table: "CashMovements",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_SupplierPaymentId",
                table: "CashMovements",
                column: "SupplierPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_BankMovements_ExpenseId",
                table: "BankMovements",
                column: "ExpenseId");

            migrationBuilder.CreateIndex(
                name: "IX_BankMovements_PaymentId",
                table: "BankMovements",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_BankMovements_SupplierPaymentId",
                table: "BankMovements",
                column: "SupplierPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCategories_Name",
                table: "ExpenseCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialEntryBatches_RequestId",
                table: "FinancialEntryBatches",
                column: "RequestId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_ExpenseCategories_CategoryDefinitionId",
                table: "Expenses",
                column: "CategoryDefinitionId",
                principalTable: "ExpenseCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Managed categories and corrected posting history cannot be removed safely. Restore a pre-upgrade backup instead.");
        }
    }
}
